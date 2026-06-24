using System.Reflection;
using MonoMod.RuntimeDetour;

namespace HornetPlayer.Playground;

// HK FSMs drive the hero's animation via Tk2dPlayAnimation(clipName) on the PlayMaker global "Hero" — which we now point
// at Hornet (HeroProxy). The clip names are HK Knight clips ("Collect Normal", "Dreamer Land", "LookUp", …; see the
// census in playmakerfsm/examples/hero_usage.rs) that DON'T exist in Hornet's Silksong tk2d collection. tk2dSpriteAnimator
// .Play(string) Debug.LogErrors once PER CALL on a missing clip -> per-cutscene-trigger noise (and violates the
// zero-error policy). Replace that with a single log-once per clip name + skip, so we get a clean, dedup'd list of the
// HK clips that need a Hornet mapping (pull them out gradually). tk2d is the shared TeamCherry.TK2D assembly, so this
// also covers the Knight — but a missing clip on the Knight would be a real HK bug worth seeing, so the global hook is fine.
internal static class Tk2dClipShim {
    private static Hook? hook;

    internal static void Install() {
        var mi = typeof(tk2dSpriteAnimator).GetMethod("Play", BindingFlags.Public | BindingFlags.Instance,
            null, [typeof(string)], null);
        if (mi == null) {
            Log.Error("[Tk2dClipShim] tk2dSpriteAnimator.Play(string) not found");
            return;
        }

        hook = new Hook(mi, (Hooked)OnPlay);
        Log.Debug("[Tk2dClipShim] installed: tk2dSpriteAnimator.Play(string)");
    }

    private static void OnPlay(Orig orig, tk2dSpriteAnimator self, string name) {
        if (self != null && !string.IsNullOrEmpty(name) && self.GetClipByName(name) == null) {
            Log.InfoOnce($"clip|{self.gameObject.name}|{name}",
                $"[Tk2dClipShim] missing clip '{name}' on '{self.gameObject.name}' -> skipped (needs Hornet mapping)");
            return; // skip orig (it would Debug.LogError per call)
        }

        orig(self!, name);
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }

    private delegate void Orig(tk2dSpriteAnimator self, string name);

    private delegate void Hooked(Orig orig, tk2dSpriteAnimator self, string name);
}
