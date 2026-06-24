using System;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// Silksong code compares against the "Recoiler" tag (CompareTag("Recoiler")) to decide nail-recoil behaviour, but HK's
// TagManager doesn't define "Recoiler" -> Unity's CompareTag throws/logs "Tag: Recoiler is not defined" and the check
// breaks. Short-circuit CompareTag("Recoiler") to true (skipping orig, which would throw on the undefined tag); every
// other tag falls through to the real CompareTag.
//
// TODO: "always true" is a blanket stub — it makes EVERY object read as a Recoiler. Narrow it to the objects that
// should actually recoil the nail (port/register the real Silksong "Recoiler" set, or add the tag to HK's TagManager).
internal static class CompareTagShim {
    private const string RecoilerTag = "Recoiler";
    private static Hook? goHook;
    private static Hook? compHook;

    internal static void Install() {
        var goMi = typeof(GameObject).GetMethod("CompareTag", new[] { typeof(string) });
        if (goMi != null)
            goHook = new Hook(goMi, (Func<Func<GameObject, string, bool>, GameObject, string, bool>)((orig, self, tag) =>
                tag == RecoilerTag ? HitRecoiler(self.name) : orig(self, tag)));
        else
            Log.Error("[CompareTagShim] GameObject.CompareTag(string) not found");

        var compMi = typeof(Component).GetMethod("CompareTag", new[] { typeof(string) });
        if (compMi != null)
            compHook = new Hook(compMi, (Func<Func<Component, string, bool>, Component, string, bool>)((orig, self, tag) =>
                tag == RecoilerTag ? HitRecoiler(self.name) : orig(self, tag)));
        else
            Log.Error("[CompareTagShim] Component.CompareTag(string) not found");
    }

    // Logs once per object name (CompareTag is hot in combat — avoid per-frame spam) so it's visible when it fires.
    private static bool HitRecoiler(string name) {
        Log.InfoOnce($"comparetag-recoiler:{name}",
            $"[CompareTagShim] CompareTag(\"Recoiler\") on '{name}' -> forced true (TODO: narrow to real recoilers)");
        return true;
    }

    internal static void Cleanup() {
        goHook?.Dispose();
        goHook = null;
        compHook?.Dispose();
        compHook = null;
    }
}
