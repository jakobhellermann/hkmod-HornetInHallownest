using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;

namespace HornetPlayer.Playground;

// HK FSMs drive the hero's animation via Tk2dPlayAnimation(clipName) on the PlayMaker global "Hero" — which we now point
// at Hornet (HeroProxy). The clip names are HK Knight clips ("Collect Normal", "Dreamer Land", "LookUp", …; see the
// census in playmakerfsm/examples/hero_usage.rs) that DON'T exist in Hornet's Silksong tk2d collection. tk2dSpriteAnimator
// .Play(string) Debug.LogErrors once PER CALL on a missing clip -> per-cutscene-trigger noise (and violates the
// zero-error policy). For an unmapped clip we log-once + skip (a clean, dedup'd list of HK clips still needing a Hornet
// mapping). tk2d is the shared TeamCherry.TK2D assembly, so this also covers the Knight — but a missing clip on the
// Knight would be a real HK bug worth seeing, so the global hook is fine.
//
// ClipMap: HK clip name -> Hornet clip name. CRITICAL beyond visuals — skipping a clip that a Tk2dPlayAnimationWithEvents
// action waits on (animationCompleteEvent) HANGS the FSM forever (no clip -> no AnimationCompleted -> the gated event,
// e.g. FINISHED, never fires). The HK ability/item pickup ("Shiny Control"/"Inspect") plays the "Collect SD *" sequence
// on the hero and gates on its completion; with the clip skipped, the pickup never returns control (soft-lock). Mapping
// to a REAL Hornet clip makes AnimationCompleted fire -> the gated event fires -> control returns. Hornet's own item
// collect is "Collect Normal 1/2/3"; "Collect Normal 3" reads as a clean collect pose, so the whole HK collect sequence
// maps onto it.
internal static class Tk2dClipShim {
    private static Hook? hook;

    private static readonly Dictionary<string, string> ClipMap = new() {
        ["Collect SD 1"] = "Collect Normal 3",
        ["Collect SD 1 Back"] = "Collect Normal 3",
        ["Collect SD 2"] = "Collect Normal 3",
        ["Collect SD 3"] = "Collect Normal 3",
        ["Collect SD 4"] = "Collect Normal 3",
        // Mask-shard completion (4th piece): "Heart Container UI" plays "Collect Heart Piece End" on the hero gated on
        // its completion (animationCompleteEvent=FINISHED). Hornet lacks that clip -> no AnimationCompleted -> FINISHED
        // never fires -> stuck in the receive pose with control relinquished. Map to the same clean collect pose.
        ["Collect Heart Piece End"] = "Collect Normal 3",
        // Mantis Lords (and other bosses) challenge-accept: HK plays "Challenge Start" gated on its completion. Map to
        // Hornet's "Taunt" — thematically a challenge taunt, and wrapMode Once (so AnimationCompleted fires and the FSM
        // proceeds; mapping to a LoopSection clip would never complete -> permanent soft-lock).
        // TODO: the preferred pose is "Challenge Strong" (the dramatic away-from-camera challenge stance) — but it's
        // LoopSection, so it never fires AnimationCompleted and would soft-lock with the simple remap. To use it, add a
        // general WithEvents gate fix: hook Tk2dPlayAnimationWithEvents and, when a remapped clip loops, fire the
        // action's animationCompleteEvent after one clip duration so the pose shows AND the FSM advances. Until then,
        // "Taunt" (Once) is the stand-in.
        ["Challenge Start"] = "Taunt"
    };

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
            // Remap a known HK clip to a Hornet clip (plays for real -> AnimationCompleted fires -> any WithEvents gate
            // resolves). Only if the mapped clip actually exists on this animator; else fall through to skip.
            if (ClipMap.TryGetValue(name, out var mapped) && self.GetClipByName(mapped) != null) {
                Log.InfoOnce($"clipmap|{self.gameObject.name}|{name}",
                    $"[Tk2dClipShim] remapped HK clip '{name}' -> Hornet '{mapped}' on '{self.gameObject.name}'");
                orig(self, mapped);
                return;
            }

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
