extern alias Silksong;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

internal enum ActiveHero { Knight, Hornet }

// Switch which character the player controls (Knight = HK's hero, Hornet = the spawned Silksong hero). The OTHER stays
// visible (its renderer keeps drawing) but inert: HeroController disabled (stops input/movement) + Rigidbody2D.simulated
// off (so it doesn't fall away under gravity while frozen).
//
// Camera: HK owns the single rendering camera. The chain is GameCameras.instance.cameraController -> follows
// cameraTarget (CameraTarget), which in FOLLOW_HERO/LOCK_ZONE follows its private `heroTransform` (normally
// HeroController.instance.transform == the Knight). We retarget by pointing `heroTransform` at the active hero — so ALL
// of HK's native camera behaviour (damping, lock zones, scene bounds) is reused for free, no per-frame position driving.
// hero_ctrl is left as the Knight (non-null; CameraTarget only null-checks it, CameraController uses it for the
// cosmetic look-up/down offset) so we don't disturb HK's camera bring-up.
internal static class HeroSwitch {
    private static GameObject? go;
    private static FieldInfo? heroTransformField;

    internal static ActiveHero Active { get; private set; } = ActiveHero.Knight;
    internal static bool HornetActive => Active == ActiveHero.Hornet;

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.HeroSwitch");
        go.AddComponent<CameraSwitchDriver>();
        Object.DontDestroyOnLoad(go);
        Log.Info("[HeroSwitch] installed (Tab toggles Knight<->Hornet; /switch route)");
    }

    internal static void Cleanup() {
        if (go != null) { Object.Destroy(go); go = null; }
        Active = ActiveHero.Knight; // leave HK's Knight controllable after unload
        SetInert(global::HeroController.instance != null ? global::HeroController.instance.gameObject : null, false);
        RetargetCamera(global::HeroController.instance != null ? global::HeroController.instance.transform : null);
    }

    internal static object Toggle() => SetActive(HornetActive ? ActiveHero.Knight : ActiveHero.Hornet);

    internal static object SetActive(ActiveHero who) {
        var hornet = BundleSpike.RealHero;
        if (who == ActiveHero.Hornet && hornet == null)
            return new { error = "Hornet not spawned (POST /spawn-real first)", active = Active.ToString() };

        Active = who;
        var knightGo = global::HeroController.instance != null ? global::HeroController.instance.gameObject : null;
        var hornetGo = BundleSpike.HornetRoot;

        // Knight inert when Hornet is active, and vice-versa. Hornet's enabled state is owned per-frame by
        // HornetEnvironmentAdapter (gated on HornetActive) — here we only flip her Rigidbody so she doesn't fall.
        SetInert(knightGo, who != ActiveHero.Knight);
        SetInert(hornetGo, who != ActiveHero.Hornet);
        if (hornet != null) hornet.enabled = who == ActiveHero.Hornet; // adapter re-asserts true while HornetActive

        var follow = who == ActiveHero.Hornet ? hornetGo?.transform : knightGo?.transform;
        RetargetCamera(follow);

        // Log.Info($"[HeroSwitch] active={Active} following={(follow != null ? follow.name : "?")}");
        return new { active = Active.ToString(), following = follow != null ? follow.name : null };
    }

    // Visible-but-frozen: keep the renderer, stop physics so it doesn't drift/fall. HeroController of the Knight is
    // toggled here (HK's hero); Hornet's HeroController is left to the adapter.
    private static void SetInert(GameObject? hero, bool inert) {
        if (hero == null) return;
        // GetComponent<global::HeroController> only matches HK's Knight (Hornet is a Silksong.HeroController), so the
        // vignette handling below applies exclusively to the Knight.
        var hk = hero.GetComponent<global::HeroController>();
        if (hk != null) {
            hk.enabled = !inert;
            // The Knight's screen-edge vignette would otherwise keep darkening the view while she's the inactive hero;
            // kill the renderer + its FSM (so nothing re-enables it), restore when she's active again.
            if (hk.vignette != null) hk.vignette.enabled = !inert;
            if (hk.vignetteFSM != null) hk.vignetteFSM.enabled = !inert;
        } else {
            // Hornet's "Vignette": the soft radial darkening (its own SpriteRenderer) follows her active state — show it
            // only while she's the active, camera-centred hero.
            var v = hero.transform.Find("Vignette");
            if (v != null) {
                v.gameObject.SetActive(!inert);
                // "Darkness Border" (hard black frame, black_solid*) is meant to be positioned by Silksong's camera rig;
                // standalone it's pinned to Hornet and blacks out a chunk of HK's screen wherever she stands. It never
                // works here -> kill it outright, keep only the radial vignette.
                var border = v.Find("Darkness Border");
                if (border != null) border.gameObject.SetActive(false);
            }
        }
        var rb = hero.GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = !inert;

        // Freeze animation while inert. The DRIVER (HeroAnimationController, both games) must go first: it runs every
        // frame independently of HeroController and calls Play() -> dereferences animator.CurrentClip.name. We then
        // PAUSE the tk2d animator instead of disabling it — disabling runs OnDisable->Stop which nulls CurrentClip, so
        // the driver's Play() NullRefs (while inert AND on reactivation). Pause keeps the current clip + frame.
        foreach (var d in hero.GetComponentsInChildren<MonoBehaviour>(true))
            if (d != null && d.GetType().Name == "HeroAnimationController") d.enabled = !inert;
        foreach (var a in hero.GetComponentsInChildren<tk2dSpriteAnimator>(true)) { if (inert) a.Pause(); else a.Resume(); }
        foreach (var a in hero.GetComponentsInChildren<Animator>(true)) a.enabled = !inert;
    }

    // Point HK's CameraTarget at `t` so HK's native camera chain follows it. Idempotent + cheap (one reflected set).
    internal static void RetargetCamera(Transform? t) {
        if (t == null) return;
        var gc = global::GameCameras.instance;
        var camTarget = gc != null ? gc.cameraTarget : null;
        if (camTarget == null) return;
        heroTransformField ??= typeof(global::CameraTarget)
            .GetField("heroTransform", BindingFlags.Instance | BindingFlags.NonPublic);
        heroTransformField?.SetValue(camTarget, t);
    }
}

// Drives the active-hero switch: Tab toggles; re-asserts the camera target each frame (cheap) so it survives HK
// re-grabbing HeroController.instance on scene init. Early execution order so the retarget lands before
// CameraTarget.Update (order 0) reads heroTransform the same frame.
//
// Scene transitions: only HK's Knight is HK's transition vehicle — it gets relocated to the new scene's entry gate.
// Hornet is DontDestroyOnLoad and keeps her old world coordinates, so after a transition she's stranded in random
// geometry / off in nirvana, and the camera (following her, or her after a Tab) points there. So on every scene change
// we snap Hornet onto the Knight once HK reports the Knight is positioned (isHeroInPosition), which keeps both heroes in
// the playable area of the new scene.
[DefaultExecutionOrder(-8000)]
internal sealed class CameraSwitchDriver : MonoBehaviour {
    private string? lastScene;
    private bool pendingSnap;

    private void Update() {
        var knight = global::HeroController.instance;

        // Detect a scene change; defer the Hornet snap until the Knight has actually been placed at the new entry.
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != lastScene) { lastScene = scene; pendingSnap = true; }
        if (pendingSnap && knight != null && knight.isHeroInPosition) {
            // When Hornet is the active hero, run her REAL Silksong scene-entry (walk/drop-in animation + entry FSMs)
            // from HK's mirrored gate. When the Knight is active, Hornet is an inert prop -> just relocate her.
            if (HeroSwitch.HornetActive && HornetSceneEntry.Enabled && knight.sceneEntryGate != null)
                StartCoroutine(HornetSceneEntry.Run(knight));
            else
                SnapHornetToKnight(knight);
            pendingSnap = false;
        }

        if (Input.GetKeyDown(KeyCode.Tab)) HeroSwitch.Toggle();

        var follow = HeroSwitch.HornetActive
            ? BundleSpike.HornetRoot?.transform
            : (knight != null ? knight.transform : null);
        HeroSwitch.RetargetCamera(follow);
    }

    private static void SnapHornetToKnight(global::HeroController knight) {
        var hornet = BundleSpike.HornetRoot;
        if (hornet == null) return;
        hornet.transform.position = knight.transform.position;
        var rb = hornet.GetComponent<Rigidbody2D>();
        if (rb != null && rb.simulated) rb.linearVelocity = Vector2.zero; // don't carry pre-transition momentum
    }
}
