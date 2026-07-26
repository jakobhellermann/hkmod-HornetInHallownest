using HornetInHallownest.HornetInHallownest.Modules.Hero;
using HornetInHallownest.Playground;
using Modding;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.HornetInHallownest.Modules;

internal enum ActiveHero {
    Knight,
    Hornet
}

// Which character the player controls (Knight = HK's hero, Hornet = the spawned Silksong hero). Owns the switch: the
// outgoing hero goes inert, the incoming one active (KnightPresence/HornetPresence), then the shared HK-owned state
// (camera, vignette, HUD, audio, the PlayMaker "Hero" var) is re-pointed at the new hero via ReassertEnvironment - on
// the switch and on scene entry (HornetSpawner), the two points where HK resets that state to its Knight. No per-frame
// work: the only live driver is HeroSwitchInput (the switch key poll).
internal static class HeroSwitch {
    private static GameObject? go;

    private static readonly KnightPresence knight = new();
    private static readonly HornetPresence hornet = new();

    internal static ActiveHero Active { get; private set; } = ActiveHero.Knight;
    internal static bool HornetActive => Active == ActiveHero.Hornet;

    private static HeroPresence ActivePresence => HornetActive ? hornet : knight;
    private static HeroPresence InactivePresence => HornetActive ? knight : hornet;

    // The active hero's GameObject: Hornet while she's active, else HK's Knight. Null only before the Knight exists.
    internal static GameObject? ActiveHeroGameObject => ActivePresence.Root;

    // Resolve a GameObject's Transform via Unity's overloaded truthiness (not `go?.transform`): `?.` is a raw reference
    // compare that doesn't see a destroyed-but-uncollected object and would touch a dead native pointer.
    internal static Transform? TransformOf(GameObject? go) => go ? go.transform : null;

    internal static void Install() {
        if (go) return;
        go = new GameObject("HornetInHallownest.HeroSwitch");
        go.AddComponent<InputPoller>();
        Object.DontDestroyOnLoad(go);
    }

    internal static void Cleanup() {
        if (go) {
            Object.Destroy(go);
            go = null;
        }

        Active = ActiveHero.Knight; // leave HK's Knight controllable after unload
        knight.Activate();
        GameCamerasBootstrap.RetargetCamera(TransformOf(ActiveHeroGameObject));
    }

    // While the player is on Hornet, move the Knight onto her spot, so he takes over there rather than at his stale
    // pre-switch coords. Must run while Hornet still exists.
    internal static void TpKnightToActiveHornet() {
        if (!HornetActive) return;
        var hornetGo = hornet.Root;
        var knightGo = knight.Root;
        if (!hornetGo || !knightGo) return;
        knightGo.transform.position = hornetGo.transform.position;
        var rb = knightGo.GetComponent<Rigidbody2D>();
        if (rb && rb.simulated) rb.linearVelocity = Vector2.zero;
    }

    internal static object Toggle() {
        return SetActive(HornetActive ? ActiveHero.Knight : ActiveHero.Hornet);
    }

    internal static object SetActive(ActiveHero who) {
        if (who == ActiveHero.Hornet && !HornetSpawner.Hornet)
            return new { error = "Hornet not spawned (POST /spawn-real first)", active = Active.ToString() };

        var prev = Active;
        Active = who;
        var knightTransform = TransformOf(knight.Root);
        var hornetTransform = TransformOf(hornet.Root);

        // Reparent the vignette off the Knight (onto the camera) before the presence sweep, so deactivating the Knight
        // doesn't disable its Darkness Control FSM along with his other FSMs.
        GameCamerasBootstrap.EnsureVignetteOnCamera();

        if (who == ActiveHero.Hornet) {
            knight.Deactivate();
            hornet.Activate();
        }
        else {
            hornet.Deactivate();
            knight.Activate();
        }

        // Hand off in place: move the newly-active hero to where the previously-active one stood, so control + camera
        // stay on the same spot (only the character changes). Skip when re-applying the same hero (e.g. at spawn).
        if (who != prev) {
            var newT = who == ActiveHero.Hornet ? hornetTransform : knightTransform;
            var oldT = prev == ActiveHero.Hornet ? hornetTransform : knightTransform;
            if (newT && oldT && newT != oldT) {
                newT.position = oldT.position;
                var rb = newT.GetComponent<Rigidbody2D>();
                if (rb && rb.simulated) rb.linearVelocity = Vector2.zero;
            }
        }

        ReassertEnvironment();
        return new { active = Active.ToString() };
    }

    // Re-point the HK-owned state (camera/vignette/HUD/audio + the PlayMaker "Hero" var) at the active hero, and re-hide
    // the inactive one. HK resets all of this to its Knight on a switch and on scene entry, so both call this.
    internal static void ReassertEnvironment() {
        GameCamerasBootstrap.SyncToActiveHero();
        HeroTargetModule.SyncGlobal();
        InactivePresence.ReassertHidden();
    }

    // The one always-on part: polls the SwitchHero bind (via HK's InputManager, so it works in both hero states and on
    // keyboard + gamepad) and toggles. Everything else here is event-driven.
    private sealed class InputPoller : MonoBehaviour {
        private HornetInputActions? binds;

        private void Start() {
            binds = new HornetInputActions(1);
            var bind = InputModule.Settings.SwitchHero;
            if (string.IsNullOrEmpty(bind)) return;
            if (KeybindUtil.ParseBinding(bind) is { } parsed) binds.Slots[0].AddKeyOrMouseBinding(parsed);
            else Log.Error($"[HeroSwitch] unparseable SwitchHero bind '{bind}'");
        }

        private void OnDestroy() {
            binds?.Destroy();
            binds = null;
        }

        private void Update() {
            if (binds == null || !binds.Slots[0].WasPressed) return;

            // The inventory belongs to the active hero and freezes the world by disabling HK's main camera (via
            // DisplayFrozenCamera); switching under that would desync. Disabled main camera = the signal.
            var gc = GameCameras.instance;
            if (gc && gc.mainCamera && !gc.mainCamera.enabled) {
                Log.Debug("[HeroSwitch] hero switch ignored during pause");
                return;
            }

            Toggle();
        }
    }
}
