extern alias Silksong;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// Stop Hornet pogoing off HK objects that HK marks non-pogoable.
//
// Hornet's down-attack pogo gate is HeroDownAttack.IsNonBounce(obj), which returns true (no bounce) when obj has an
// active Silksong.NonBouncer (or BounceBalloon). HK objects that should NOT be pogoable (e.g. the stag Station Bell)
// carry HK's NonBouncer — a DIFFERENT type (HK's unprefixed Assembly-CSharp) — so `GetComponent<Silksong.NonBouncer>()`
// finds nothing and Hornet bounces off everything on the interactive/attack layers. (In HK the Knight respects the same
// NonBouncer and does NOT pogo these.)
//
// Fix: postfix IsNonBounce to ALSO honour HK's NonBouncer. Covers both pogo paths (ContinueBounceTrigger via
// OnTriggerEnter2D, and OnHitResponded) since both call IsNonBounce.
internal static class PogoNonBounceShim {
    private static Hook? hook;

    internal static void Install() {
        var mi = typeof(Silksong::HeroDownAttack).GetMethod("IsNonBounce",
            BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(GameObject)], null);
        if (mi == null) {
            Log.Error("[PogoNonBounceShim] HeroDownAttack.IsNonBounce not found");
            return;
        }

        hook = new Hook(mi, (Hooked)OnIsNonBounce);
        Log.Info("[PogoNonBounceShim] installed: HeroDownAttack.IsNonBounce");
    }

    private static bool OnIsNonBounce(Orig orig, GameObject obj) {
        if (orig(obj)) return true; // Silksong NonBouncer/BounceBalloon already said no-bounce
        if (obj == null) return false;
        var nb = obj.GetComponent<NonBouncer>(); // HK's NonBouncer (global)
        return nb != null && nb.active;
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }

    private delegate bool Orig(GameObject obj);

    private delegate bool Hooked(Orig orig, GameObject obj);
}
