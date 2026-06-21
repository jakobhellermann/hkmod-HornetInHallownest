using System;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// Cross-game GetComponent(string) name-collision tolerance.
//
// Unity's GameObject.GetComponent(string) resolves a bare class name to a SINGLE System.Type via its type registry.
// When HK and the prefixed Silksong assembly both define a type with that name (e.g. "HeroController" exists in both
// HK's Assembly-CSharp and Silksong.AssemblyCSharp), the name resolves to HK's (the primary game assembly) — so the
// lookup on a Silksong object returns null even though the same-named Silksong component IS present (verified:
// hornet.GetComponent("HeroController")=NULL while GetComponent<Silksong.HeroController>()=OK; knight.GetComponent(
// "HeroController") returns the Assembly-CSharp type). This silently breaks PlayMaker actions that resolve a behaviour
// by STRING name — CallMethodProper etc.: e.g. the Bind FSM's "Can Bind?" gate does GetComponent("HeroController") to
// call CanBind, gets null, never calls it -> "Return Bool" stays false -> the gate cancels bind/heal. (Typed
// GetComponent<T> is unaffected; only the string overload collides.)
//
// Fix: on a NULL result, fall back to scanning the object's own components for one whose type Name/FullName matches the
// requested name. Only activates on null (Unity's normal hit is untouched), and GetComponent(string) is cold (~0/s in
// gameplay, ~340 calls total at boot), so the cost is nil. Process-wide but harmless: HK calls that legitimately return
// null just do one extra component scan that also finds nothing.
internal static class GetComponentShim {
    private static Hook? hook;

    internal static void Install() {
        if (hook != null) return;
        var mi = typeof(GameObject).GetMethod("GetComponent", new[] { typeof(string) });
        if (mi == null) {
            Log.Error("[GetComponentShim] GameObject.GetComponent(string) not found");
            return;
        }

        hook = new Hook(mi,
            (Func<Func<GameObject, string, Component>, GameObject, string, Component?>)((orig, self, name) => {
                var c = orig(self, name);
                if (c != null || string.IsNullOrEmpty(name)) return c;
                foreach (var comp in self.GetComponents<Component>()) {
                    if (comp == null) continue; // a missing-script component
                    var ty = comp.GetType();
                    if (ty.Name == name || ty.FullName == name) return comp;
                }

                return null;
            }));
        Log.Debug("[GetComponentShim] installed: GameObject.GetComponent(string) name-match fallback on null");
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }
}
