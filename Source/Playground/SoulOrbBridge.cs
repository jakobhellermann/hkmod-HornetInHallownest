using System;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace HornetPlayer.Playground;

// HK soul (SoulOrb + HeroController.AddMPCharge) -> Hornet silk. Why no hero-retarget channel catches this: SoulOrb is
// a plain MonoBehaviour (not an FSM), and it reads HeroController.instance (the KNIGHT) DIRECTLY — for its homing
// `target` (Start), for the grant (AddMPCharge), and for the get-flash. HeroProxy (PlayMaker global "Hero"),
// GameObjectFindShim ("Player" tag) and EnemyTargetBridge (GetHero action) all only steer FSM/tag/Find lookups, so a
// direct HeroController.instance read in arbitrary C# is invisible to them. Result: a soul totem's orbs fly to the
// inert off-screen Knight and dump soul into HK's PlayerData; Hornet (no soul, only silk) gets nothing.
//
// Two hooks, only while Hornet is the active hero:
//   1. SoulOrb.OnEnable + Start -> repoint the private `target` to Hornet so the orbs home to her. Both are needed:
//      SoulOrb is POOLED and pre-warmed, so Start (which sets target = HeroController.instance) runs ONCE at pool
//      warmup — before Hornet exists, so it caches the Knight and never re-runs. A fling only fires OnEnable, so the
//      retarget must live there. Start is still hooked for the rare never-warmed orb (Awake->OnEnable->Start order
//      would let Start's Knight overwrite our OnEnable retarget on that first activation).
//   2. HeroController.AddMPCharge -> add silk to Hornet (1 per orb) and skip the Knight's soul add. This is the general
//      soul->silk seam: any HK soul source aimed at the hero (totems, vessels) converts to silk.
// (The get-flash still SpriteFlashes the Knight — cosmetic, off-screen, left alone.)
internal static class SoulOrbBridge {
    private static Hook? startHook;
    private static Hook? enableHook;
    private static Hook? mpHook;
    private static FieldInfo? targetField;

    private static void RetargetToHornet(SoulOrb self) {
        if (targetField != null && HeroSwitch.HornetActive && BundleSpike.RealHero is { } hero)
            targetField.SetValue(self, hero.transform);
    }

    internal static void Install() {
        if (startHook != null || mpHook != null || enableHook != null) return;

        var start = typeof(SoulOrb).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
        var onEnable = typeof(SoulOrb).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
        targetField = typeof(SoulOrb).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);
        if (start == null || onEnable == null || targetField == null) {
            Log.Error("[SoulOrbBridge] SoulOrb.Start / OnEnable / target field not found");
        }
        else {
            startHook = new Hook(start, (Action<Action<SoulOrb>, SoulOrb>)((orig, self) => {
                orig(self);
                RetargetToHornet(self);
            }));
            enableHook = new Hook(onEnable, (Action<Action<SoulOrb>, SoulOrb>)((orig, self) => {
                orig(self);
                RetargetToHornet(self);
            }));
        }

        var mp = typeof(HeroController).GetMethod("AddMPCharge", new[] { typeof(int) });
        if (mp == null)
            Log.Error("[SoulOrbBridge] HeroController.AddMPCharge(int) not found");
        else
            mpHook = new Hook(mp, (Action<Action<HeroController, int>, HeroController, int>)((orig, self, amount) => {
                if (HeroSwitch.HornetActive && BundleSpike.RealHero is { } hero) {
                    hero.AddSilk(1, true); // 1 silk per orb; skip the Knight's soul add
                    return;
                }

                orig(self, amount);
            }));

        Log.Debug("[SoulOrbBridge] installed: SoulOrb.Start retarget + AddMPCharge -> Hornet silk");
    }

    internal static void Cleanup() {
        startHook?.Dispose();
        startHook = null;
        enableHook?.Dispose();
        enableHook = null;
        mpHook?.Dispose();
        mpHook = null;
    }
}
