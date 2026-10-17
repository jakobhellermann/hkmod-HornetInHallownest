extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Modules;

// Hornet's Needolin stands in for the Dream Nail: any song drains a dreamer from inside its dream, and Elegy of the Deep
// (Needolin played holding down) acts as a Dream Nail on nearby HK dream-reactive objects. Next to an HK dream entrance
// the Elegy plays as at a Silksong memory: it costs no silk, and once the dream takes her she plays on, locked.
public sealed class NeedolinDreamNailModule : ModuleBase {
    private const float Radius = 6f; // Needolin noise radius
    private const int HeroAttackLayer = 17; // HK's dream nail Hitbox layer
    private const string DreamAttackTag = "Dream Attack";
    internal const string DreamImpact = "DREAM IMPACT";

    // A Silksong memory waits 3.5 s of the Elegy in its range, takes Hornet after 1 s more, closes the camera in on her
    // for 4.7 s, then she collapses into it.
    private const float DreamEnterDelay = 4.5f;
    private const float DeepEnterTime = 4.7f;
    private const float CollapseTimeout = 3f;
    private const float EntranceTimeout = 10f;

    // Silksong's Needolin skips its silk tick while this is set, as it is in a memory's range.
    private const string MemoryInRange = "needolinPlayingMemoryInRange";

    private const string EnterEffectPath = "Assets/Prefabs/Effects/Deep Memory Enter Black.prefab";

    // Silksong's memories spawn that effect from their scenes, so it has no Addressables key: its bundle and the bundles
    // its renderers and audio draw from load by hand. File -> the name a loaded bundle reports. The scripts come from the
    // mod's own MonoScript bundle, which Addressables already loaded.
    private static readonly (string File, string Name)[] enterEffectBundles = [
        ("572d76c166f4ec5c67aedff26a920797_unitybuiltinassets.bundle",
            "572d76c166f4ec5c67aedff26a920797_unitybuiltinassets.bundle"),
        ("animations_assets_areamemory.bundle", "a12664b4690aa17aa916207cbf14643e.bundle"),
        ("atlases_assets_assets/sprites/_atlases/vignette.spriteatlas.bundle",
            "edba1e38920beda3dc88319a7f94bf77.bundle"),
        ("audioobjects_assets_all.bundle", "7aaf7f68ff23ebc6ebe53ed010fdb9ff.bundle"),
        ("materials_assets_areacradle.bundle", "355b45bbbf8db6b94e36d9b7548c71eb.bundle"),
        ("materials_assets_shared.bundle", "99ab81edb7d192f5b8d3d9e8cc3f5e55.bundle"),
        ("sfxstatic_assets_areaantareacloverareacoralareamemoryareashellwood.bundle",
            "c64b12ac51f7387a86a676740f457da3.bundle"),
        ("sfxstatic_assets_boss.bundle", "70b4fe45692f2808ae0761db431d28ae.bundle"),
        ("sfxstatic_assets_ui.bundle", "6057154eb531e1268d7f6787f1808b6f.bundle"),
        ("textures_assets_.bundle", "21ad84bb4c96c60b6ca47439a54c1c69.bundle"),
        ("localpoolprefabs_assets_areamemory.bundle", "089f45ec7ba2f5871ddf2a3b0bb17b88.bundle")
    ];

    private static readonly Dictionary<GameObject, bool> dreamEntrances = [];
    private static GameObject? enterEffect;
    private static readonly Collider2D[] overlaps = new Collider2D[128];

    private GameObject? hitboxGo;
    private Collider2D? collider;
    private bool wasPerforming;
    private bool wasPlayingElegy;
    private float elegyStartTime;
    private bool impactArmed;
    private float rangeEntryTime = -1f;
    private readonly object inputBlocker = new();
    private string? enteringScene;
    private ForceCameraAspect? zoomedCamera;
    private static string? drainEndScene;

    public override string Id => "needolin-dreamnail";

    public override void Initialize() {
        Detour(typeof(Fsm), "SwitchState", OnSwitchState, typeof(FsmState));
    }

    private static void OnSwitchState(Action<Fsm, FsmState> orig, Fsm self, FsmState to) {
        orig(self, to);
        if (self is { Name: "Control", GameObjectName: "Dreamer NPC" } && to.Name == "End Start")
            drainEndScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }

    // From "End Start" the dreamer sets the Knight's Spell Control "Dream End" and he finishes draining without input.
    // Hold Hornet every frame until the dream unloads: Needolin hands control back whenever it stops playing.
    private static void HoldThroughDrainEnd(Silksong::HeroController hero) {
        if (drainEndScene == null) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != drainEndScene) {
            drainEndScene = null;
            return;
        }

        if (!hero.controlReqlinquished || hero.acceptingInput) hero.RelinquishControl();
    }

    protected override void OnDeinitialize() {
        if (hitboxGo) Object.Destroy(hitboxGo);
        hitboxGo = null;
        collider = null;
        wasPerforming = false;
        wasPlayingElegy = false;
        impactArmed = false;
        SetMemoryInRange(false);
        dreamEntrances.Clear();
        if (zoomedCamera) zoomedCamera!.SetFovOffset(0f, 0f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        zoomedCamera = null;
        enteringScene = null;
    }

    public override void HornetActiveUpdate(Silksong::HeroController hero) {
        HoldThroughDrainEnd(hero);
        if (enteringScene != null && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != enteringScene)
            EndDreamEntry(hero);

        // Not Silk Specials' "Needolin Sub" state: holding the button while CanPlayNeedolin() fails re-enters it every
        // few frames, and each re-entry would dream-impact everything in range.
        var performing = Silksong::HeroPerformanceRegion.IsPerforming;
        if (performing != wasPerforming) {
            wasPerforming = performing;
            FocusToggled(performing);
        }

        var playingElegy = performing && hero.cState.needolinPlayingMemory;
        if (playingElegy != wasPlayingElegy) {
            wasPlayingElegy = playingElegy;
            ElegyToggled(hero, playingElegy);
        }

        var nearEntrance = playingElegy && IsDreamEntranceInRange(hero.transform.position);
        SetMemoryInRange(nearEntrance);
        if (!nearEntrance) rangeEntryTime = -1f;
        else if (rangeEntryTime < 0f) rangeEntryTime = Time.time;
        if (!playingElegy || impactArmed) return;

        if (nearEntrance) {
            if (Time.time < rangeEntryTime + DreamEnterDelay) return;
            impactArmed = true;
            StartCoroutine(EnterDream(hero));
        }
        else if (Time.time >= elegyStartTime + DreamEnterDelay) {
            ArmImpact();
        }
    }

    private static void FocusToggled(bool performing) {
        if (!performing) {
            // Releasing Focus broadcasts this in vanilla: a draining dreamer goes back to waiting instead of draining on
            // while Hornet walks off its platform.
            PlayMakerFSM.BroadcastEvent("DREAM FOCUS END");
            return;
        }

        // Frees an already-wounded HK dreamer (drains on "DREAM FOCUS START"). Vanilla broadcasts this while Focus is
        // held; Needolin is Hornet's sustained-focus equivalent. Global like vanilla, only a wounded dreamer reacts.
        PlayMakerFSM.BroadcastEvent("DREAM FOCUS START");
    }

    private void ElegyToggled(Silksong::HeroController hero, bool playing) {
        impactArmed = false;
        if (!playing) {
            if (collider) collider.enabled = false;
            return;
        }

        EnsureHitbox(hero);
        elegyStartTime = Time.time;
        dreamEntrances.Clear();
    }

    private static void SetMemoryInRange(bool inRange) {
        if (Silksong::StaticVariableList.GetValue(MemoryInRange, false) != inRange)
            Silksong::StaticVariableList.SetValue(MemoryInRange, inRange);
    }

    private static bool IsDreamEntranceInRange(Vector2 position) {
        var count = Physics2D.OverlapCircle(position, Radius, new ContactFilter2D().NoFilter(), overlaps);
        for (var i = 0; i < count; i++)
            if (overlaps[i] && IsDreamEntrance(overlaps[i].gameObject))
                return true;
        return false;
    }

    // A dream entrance is what the Dream Nail sends into a dream: its FSM takes the Dream Nail's hit and starts the scene
    // transition itself. Doors start one too, and so does the hidden Knight's own Dream Nail FSM, which stays on Hornet.
    private static bool IsDreamEntrance(GameObject go) {
        if (dreamEntrances.TryGetValue(go, out var known)) return known;
        var isEntrance = go.GetComponentsInParent<PlayMakerFSM>(true).Any(fsm => {
            var actions = fsm.FsmStates.Where(state => state.ActionsLoaded).SelectMany(state => state.Actions)
                .ToArray();
            return actions.Any(a => a is BeginSceneTransition) &&
                   (actions.Any(a => a is Trigger2dEvent { collideTag.Value: DreamAttackTag }) ||
                    fsm.FsmStates.Any(state => state.Transitions.Any(t => t.EventName == DreamImpact)));
        });
        dreamEntrances[go] = isEntrance;
        return isEntrance;
    }

    // Silksong's "Deep Memory Enter": her Needolin plays on, locked, while the camera closes in, then she stops and
    // collapses, and the HK dream entrance takes her in.
    private IEnumerator EnterDream(Silksong::HeroController hero) {
        enteringScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Silksong::EventRegister.SendEvent("NEEDOLIN LOCK");
        hero.AddInputBlocker(inputBlocker);
        var effect = LoadEnterEffect();
        if (effect) Object.Instantiate(effect, hero.transform.position, Quaternion.identity);
        ZoomCamera(-1f, DeepEnterTime);
        yield return new WaitForSeconds(DeepEnterTime);
        if (!hero) yield break;

        Silksong::EventRegister.SendEvent("FSM CANCEL");
        hero.RelinquishControl();
        hero.StopAnimationControl();
        ZoomCamera(-2f, 2f);
        hero.AnimCtrl.PlayClipForced("Needolin Deep End");
        var collapse = hero.AnimCtrl.animator.CurrentClip;
        for (var t = 0f; t < CollapseTimeout && hero && hero.AnimCtrl.animator.IsPlaying(collapse); t += Time.deltaTime)
            yield return null;
        ArmImpact();
        // The song stopped before this impact, so nothing else switches the hitbox off: left on, it would send her
        // straight back in when she returns next to the entrance.
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        if (collider) collider!.enabled = false;

        // An entrance that doesn't take her in after all would leave her collapsed and blocked.
        yield return new WaitForSeconds(EntranceTimeout);
        if (!hero || enteringScene == null) yield break;
        EndDreamEntry(hero);
        hero.RegainControl();
        hero.StartAnimationControlToIdle();
    }

    private static GameObject? LoadEnterEffect() {
        if (enterEffect) return enterEffect;
        AssetBundle? bundle = null;
        foreach (var (file, name) in enterEffectBundles)
            bundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b && b.name == name) ??
                     AssetBundle.LoadFromFile(Paths.SilksongAddressablesBundle(file));
        enterEffect = bundle ? bundle.LoadAsset<GameObject>(EnterEffectPath) : null;
        if (!enterEffect) Log.Error($"[{nameof(NeedolinDreamNailModule)}] {EnterEffectPath} didn't load");
        return enterEffect;
    }

    private void EndDreamEntry(Silksong::HeroController hero) {
        enteringScene = null;
        hero.RemoveInputBlocker(inputBlocker);
        if (zoomedCamera) zoomedCamera!.SetFovOffset(0f, 0f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
        zoomedCamera = null;
    }

    private void ZoomCamera(float offset, float time) {
        var cameras = GameCameras.instance;
        var camera = cameras ? cameras.tk2dCam.GetComponent<ForceCameraAspect>() : null;
        if (!camera) return;
        zoomedCamera = camera;
        camera.SetFovOffset(offset, time, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
    }

    private void ArmImpact() {
        impactArmed = true;
        if (!collider) return;
        // Re-enable so Unity re-emits OnTriggerEnter2D for everything already inside the radius (Hornet is stationary
        // while playing, so a freshly-enabled trigger cleanly reports current overlaps).
        collider.enabled = false;
        collider.enabled = true;
    }

    private void EnsureHitbox(Silksong::HeroController hero) {
        if (hitboxGo) return;
        hitboxGo = new GameObject("hp_needolin_dreamnail") { tag = DreamAttackTag, layer = HeroAttackLayer };
        hitboxGo.transform.SetParent(hero.transform, false);
        var c = hitboxGo.AddComponent<CircleCollider2D>();
        c.isTrigger = true;
        c.radius = Radius;
        c.enabled = false;
        collider = c;
        hitboxGo.AddComponent<NeedolinHitbox>();
    }
}

// Port of HK's dream-nail "Send Event" FSM state, which is what dream entrances and dream bosses listen for.
internal sealed class NeedolinHitbox : MonoBehaviour {
    private void OnTriggerEnter2D(Collider2D other) =>
        FSMUtility.SendEventToGameObject(other.gameObject, NeedolinDreamNailModule.DreamImpact);
}
