using HutongGames.PlayMaker;
using HkHero = HeroController;

namespace HornetPlayer.Playground;

// HK enemy/cutscene/UI FSMs reference the player through a PlayMaker GLOBAL FsmGameObject var "Hero" (set, in HK, to
// the Knight). A census over every HK FSM (playmakerfsm/examples/hero_usage.rs) shows it's consumed as:
//   - transform/physics (14644): ChaseObject*/FaceObject/DistanceFly*/GetPosition/SetVelocity2d/SetScale/SetMeshRenderer/FindChild
//   - REFLECT (4874): CallMethodProper -> GetComponent("HeroController").<method> — 33 distinct HeroController.* methods
//   - message/fsmvar (4519): SendMessage/SendEvent by-name, Set/GetFsm* at the PlayMaker level
// We point the global at Hornet's real GameObject while she's active: her transform/Rigidbody2D/renderer/tk2d animator
// satisfy the physics+structural consumers directly, and CallMethodProper's GetComponent("HeroController") resolves to
// HER Silksong HeroController natively (same simple type name) -> control/recoil/state methods hit the active hero. No
// proxy, no shim, no cross-game type collision.
//
// KNOWN GAPS (surface, then map — see Tk2dClipShim + the census): Tk2dPlayAnimation clipName values are HK Knight clips
// ("Collect Normal", "Dreamer Land", …) absent from Hornet's tk2d collection -> the play no-ops (cosmetic) or, for the
// *WithEvents variants that gate a cutscene on the anim-complete event, the FSM hangs (e.g. item-collect). The ~6
// HK-subsystem methods (TakeGeo/AddGeo/SetBenchRespawn/GetEntryGateName/AddMPCharge/CanTalk) now hit Silksong's
// HeroController instead of HK's -> may misbehave; handle per-method if/when they fire.
public static class HeroProxy {
    private static FsmGameObject? heroVar;
    private static bool warned;

    // Point HK's PlayMaker global "Hero" at the active hero. Called per frame from CameraSwitchDriver.Update (HK may
    // re-bind it on scene entry — same reason HeroSwitch re-asserts the vignette/HUD per frame). Cheap: a cached var +
    // a write only on change. While the Knight is active it points back at the Knight.
    internal static void SyncGlobal() {
        if (heroVar == null) {
            var globals = PlayMakerGlobals.Instance;
            if (globals == null) return;
            heroVar = globals.Variables.FindFsmGameObject("Hero");
            if (heroVar == null) {
                if (!warned) {
                    Log.Error("[HeroProxy] PlayMaker global 'Hero' not found");
                    warned = true;
                }

                return;
            }
        }

        var target = HeroSwitch.ActiveHeroGameObject;
        if (target == null) return;
        if (heroVar.Value != target) {
            heroVar.Value = target;
            Log.Info($"[HeroProxy] global 'Hero' -> '{target.name}'");
        }
    }

    internal static void Cleanup() {
        // restore the global to the Knight so HK is left coherent after unload
        if (heroVar != null && HkHero.UnsafeInstance != null)
            heroVar.Value = HkHero.UnsafeInstance.gameObject;
        heroVar = null;
        warned = false;
    }
}
