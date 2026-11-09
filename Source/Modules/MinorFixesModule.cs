extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GlobalEnums;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using USceneManager = UnityEngine.SceneManagement.SceneManager;
using SBumpCheck = Silksong::CharacterBumpCheck;
using SCollisionSide = Silksong::GlobalEnums.CollisionSide;
using SFreeze = Silksong::GlobalEnums.FreezeMomentTypes;
using SFsm = SilksongPM::HutongGames.PlayMaker.Fsm;
using SFsmState = SilksongPM::HutongGames.PlayMaker.FsmState;

namespace HornetInHallownest.Modules;

// Small fixes for the intentionally inactive Silksong GameManager GO (the seam), whose StartCoroutine calls misfire:
//   - StartCoroutine: on an inactive GO Unity silently drops the coroutine (no log), breaking hazard respawn / death
//     sequences (GameManager.HazardRespawn -> StartCoroutine(hero.HazardRespawn())). Redirect those to our always-active
//     HornetRuntime; the coroutine captures its own `this`, so it runs correctly (the host just pumps it per frame).
//   - FreezeMoment: the GM's hit-stop coroutine can't start on the inactive GM ("Coroutine couldn't be started" per hit).
//     Pure juice (a brief global timeScale dip), so no-op it, but still invoke onFinish so death-sequence chains don't hang.
public sealed class MinorFixesModule : ModuleBase {
    private delegate void CheckForBumpOrig(SBumpCheck self, SCollisionSide side, out bool hitBump, out bool hitWall,
        out bool hitHighWall);

    private delegate void CheckForBumpHook(CheckForBumpOrig orig, SBumpCheck self, SCollisionSide side,
        out bool hitBump, out bool hitWall, out bool hitHighWall);

    // Above any ledge top Hornet's vault or clamber accepts (at most 0.94 over her head).
    private const float ClamberLandingHeight = 1.2f;

    private static readonly int haloEndState = Animator.StringToHash("End");

    private static readonly RaycastHit2D[] wallHits = new RaycastHit2D[10];
    private static readonly List<(Rect Bounds, Vector2[] Points)> terrainOutlines = [];
    private static readonly Dictionary<Collider2D, bool> ignoresHero = [];
    private static SBumpCheck? bumpCheck;
    private static Collider2D bumpCollider = null!;
    private static Rigidbody2D bumpBody = null!;
    private static LayerMask bumpGroundMask;
    private MonoBehaviour? host;

    public override string Id => "minor-fixes";

    public override void Initialize() {
        host = UnityApi.FindAny<HornetRuntime>();
        Detour(typeof(MonoBehaviour), "StartCoroutine", OnStartCoroutine, typeof(IEnumerator));
        Detour(typeof(Silksong::GameManager), "FreezeMoment", OnFreezeMoment, typeof(SFreeze), typeof(Action));
        Detour(typeof(Silksong::HeroController), "RelinquishControl", OnRelinquishControl);
        Detour(typeof(Silksong::HeroController), nameof(Silksong::HeroController.PreventCastByDialogueEnd),
            OnPreventCastByDialogueEnd);
        Detour(typeof(IgnoreHeroCollision), "Ignore", OnIgnoreHeroCollision);
        Detour(typeof(SBumpCheck), nameof(SBumpCheck.CheckForBump), (CheckForBumpHook)OnCheckForBump);
        Detour(typeof(Silksong::NoClamberRegion), "get_IsClamberBlocked", OnIsClamberBlocked);
        Detour(typeof(SFsm), "SwitchState", OnSilksongSwitchState, typeof(SFsmState));
        USceneManager.activeSceneChanged += OnActiveSceneChanged;
        CollectTerrainOutlines(USceneManager.GetActiveScene());
    }

    protected override void OnDeinitialize() => USceneManager.activeSceneChanged -= OnActiveSceneChanged;

    private static void OnActiveSceneChanged(Scene from, Scene to) {
        ignoresHero.Clear();
        CollectTerrainOutlines(to);
    }

    // A bind interrupted by damage goes through "Cancel All", which, unlike finishing it ("Shaman End?") or cancelling it
    // mid-air ("Shaman Air Cancel"), never plays the Shaman crest halo's "End", so the halo stays on Hornet.
    private static void OnSilksongSwitchState(Action<SFsm, SFsmState> orig, SFsm self, SFsmState to) {
        orig(self, to);
        if (self.Name != "Bind" || to.Name != "Cancel All") return;
        var halo = self.GameObject.transform.Find("Bind Effects/shaman_focus_halo");
        if (!halo || !halo.gameObject.activeSelf || !halo.TryGetComponent<Animator>(out var animator)) return;
        if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash != haloEndState) animator.Play(haloEndState);
    }

    #region Sprint + cutscene RelinquishControl

    // RelinquishControl does nothing if control is already relinquished (e.g. during dash), so ResetMotion never ran
    // for the abyss exit cutscene -> camera follows Hornet into nirvana.
    private static void OnRelinquishControl(Action<Silksong::HeroController> orig, Silksong::HeroController self) {
        if (self.cState is { isSprinting: true, dead: false })
            self.acceptingInput = true;
        orig(self);
    }

    #endregion

    private Coroutine OnStartCoroutine(Func<MonoBehaviour, IEnumerator, Coroutine> orig, MonoBehaviour self,
        IEnumerator? routine) {
        if (routine == null) return orig(self, null!);
        // Redirect only when the calling GO is inactive; normal calls pass through untouched.
        if (!host || (self && self.gameObject && self.gameObject.activeInHierarchy)) return orig(self, routine);
        return orig(host, routine);
    }

    private static void OnFreezeMoment(Action<Silksong::GameManager, SFreeze, Action> orig,
        Silksong::GameManager self, SFreeze type, Action? onFinish) {
        onFinish?.Invoke();
    }

    private static void
        OnPreventCastByDialogueEnd(Action<Silksong::HeroController> orig, Silksong::HeroController self) {
        // Silksong never calls the method, and never decrements the resulting timer, leading to cast/taunt being broken
        // after HK calls this on dialogue close.
    }

    // IgnoreHeroCollision unsubscribes from the Knight's heroInPosition only after Ignore succeeds. A handler left over
    // from a destroyed object throws inside HeroController.EnterScene, which aborts the scene entry on a black screen.
    private static void OnIgnoreHeroCollision(Action<IgnoreHeroCollision> orig, IgnoreHeroCollision self) {
        if (!self) return;
        orig(self);
        var hornet = HornetSpawner.HornetRoot;
        if (!hornet) return;
        var collider = self.GetComponent<Collider2D>();
        foreach (var heroCollider in hornet.GetComponents<Collider2D>())
            Physics2D.IgnoreCollision(collider, heroCollider);
    }

    // Hornet's sprint and dash find walls with raycasts, which still see the objects above.
    private static void OnCheckForBump(CheckForBumpOrig orig, SBumpCheck self, SCollisionSide side, out bool hitBump,
        out bool hitWall, out bool hitHighWall) {
        orig(self, side, out hitBump, out hitWall, out hitHighWall);
        if (!hitWall || side is not (SCollisionSide.left or SCollisionSide.right)) return;

        if (!ReferenceEquals(self, bumpCheck)) {
            bumpCheck = self;
            bumpCollider = self.GetFieldValue<Collider2D>("collider")!;
            bumpBody = self.GetFieldValue<Rigidbody2D>("body")!;
            bumpGroundMask = self.GetFieldValue<LayerMask>("groundMask");
        }

        var bounds = bumpCollider.bounds;
        var body = bumpBody;
        var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = bumpGroundMask };
        var right = side == SCollisionSide.right;
        var x = right ? bounds.max.x - 0.1f : bounds.min.x + 0.1f;
        var direction = right ? Vector2.right : Vector2.left;
        var distance = 0.13f + Mathf.Abs(body.GetVelocity().x * Time.deltaTime);
        var highY = bounds.max.y + Physics2D.defaultContactOffset * 2f + 0.001f;
        hitHighWall = HitsWall(new Vector2(x, highY), direction, filter, distance);
        hitWall = hitHighWall || HitsWall(new Vector2(x, bounds.min.y + 0.2f), direction, filter, distance);
    }

    // HK's tilemap terrain is an outline of edges, hollow inside, where Silksong's terrain is solid. Under a ceiling that
    // tops a wall, Hornet's vault and clamber rays find nothing past the wall and would carry her into the rock.
    private static bool OnIsClamberBlocked(Func<bool> orig) {
        if (orig()) return true;
        var hero = HornetSpawner.Hornet!;
        var bounds = HornetSpawner.TerrainCollider!.bounds;
        var ahead = hero.cState.facingRight ? bounds.max.x + bounds.extents.x : bounds.min.x - bounds.extents.x;
        return IsInsideTerrain(new Vector2(ahead, bounds.max.y + ClamberLandingHeight));
    }

    // Even-odd over the closed terrain outlines, so an open room inside a chunk's outline counts as open.
    private static bool IsInsideTerrain(Vector2 point) {
        var inside = false;
        foreach (var (bounds, points) in terrainOutlines) {
            if (!bounds.Contains(point)) continue;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                if (points[i].y > point.y != points[j].y > point.y &&
                    point.x < points[i].x + (point.y - points[i].y) * (points[j].x - points[i].x) /
                    (points[j].y - points[i].y))
                    inside = !inside;
        }

        return inside;
    }

    private static void CollectTerrainOutlines(Scene scene) {
        terrainOutlines.Clear();
        foreach (var root in scene.GetRootGameObjects())
        foreach (var edge in root.GetComponentsInChildren<EdgeCollider2D>(true)) {
            var local = edge.points;
            if (edge.gameObject.layer != (int)PhysLayers.TERRAIN || local.Length < 4 || local[0] != local[local.Length - 1]) continue;
            var points = local.Select(p => (Vector2)edge.transform.TransformPoint(p + edge.offset)).ToArray();
            var min = new Vector2(points.Min(p => p.x), points.Min(p => p.y));
            var max = new Vector2(points.Max(p => p.x), points.Max(p => p.y));
            terrainOutlines.Add((new Rect(min, max - min), points));
        }
    }

    private static bool HitsWall(Vector2 origin, Vector2 direction, ContactFilter2D filter, float distance) {
        var count = Physics2D.Raycast(origin, direction, filter, wallHits, distance);
        for (var i = 0; i < count; i++)
            if (!IgnoresHero(wallHits[i].collider))
                return true;
        return false;
    }

    private static bool IgnoresHero(Collider2D collider) {
        if (!ignoresHero.TryGetValue(collider, out var ignores))
            ignoresHero[collider] = ignores = collider.GetComponent<IgnoreHeroCollision>();
        return ignores;
    }
}
