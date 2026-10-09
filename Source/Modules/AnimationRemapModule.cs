using System;
using System.Collections;
using System.Collections.Generic;
using HornetInHallownest.Core;

namespace HornetInHallownest.Modules;

// HK FSMs animate the hero via tk2dSpriteAnimator.Play(clipName) with HK Knight clip names.
// Map them to equivalent hornet clips.
// Required for FSMs waiting for certain animation events.
public sealed class AnimationRemapModule : ModuleBase {
    private static readonly Dictionary<string, string> clipMap = new() {
        ["Collect SD 1"] = "Collect Normal 1",
        ["Collect SD 1 Back"] = "Collect Normal 3",
        ["Collect SD 2"] = "Collect Normal 3",
        ["Collect SD 3"] = "Collect Normal 3",
        ["Collect SD 4"] = "Collect Normal 1",
        ["Collect Heart Piece End"] = "Collect Normal 3",
        ["Challenge Start"] = "Challenge Strong",
        ["Challenge End"] = "ChallengeStrongToIdle",
        ["Collect Magical Fall"] = "Fall",
        ["Dreamer Land"] = "Idle Hurt",
        ["Collect Magical 1"] = "Collect Memory Orb",
        ["Collect Shadow"] = "Collect Memory Orb",
        ["DJ Get Land"] = "Collect Normal 1",
        ["ToProne"] = "Abyss Kneel",
        ["Collect Acid"] = "Collect Normal 1",
        ["Collect StandToIdle"] = "Collect Stand 3",
        ["Look At King"] = "LookUp"
    };

    // These clips are looping, but the orig HK clip play expects an animation finished event.
    // Send it manually when the animation first loops.
    private static readonly HashSet<string> loopingClips = ["Challenge Strong"];

    private static readonly HashSet<string> loopStartClips = ["Collect Shadow"];

    private static readonly Dictionary<(string Playing, string Next), string> followUpMap = new() {
        [("Collect Memory Orb", "GetUpToIdle")] = "Land"
    };

    public override string Id => "animation-remap";

    public override void Initialize() {
        Detour(typeof(tk2dSpriteAnimator), "Play", OnPlay, typeof(string));
    }

    private void OnPlay(Action<tk2dSpriteAnimator, string> orig, tk2dSpriteAnimator self, string name) {
        if (self && self.CurrentClip is { } playing &&
            followUpMap.TryGetValue((playing.name, name), out var followUp) &&
            self.GetClipByName(followUp) is not null) {
            orig(self, followUp);
            return;
        }

        if (self && !string.IsNullOrEmpty(name) && self.GetClipByName(name) is null) {
            if (clipMap.TryGetValue(name, out var mapped) && self.GetClipByName(mapped) is { } clip) {
                LogDebugOnce($"clipmap|{self.gameObject.name}|{name}",
                    $"remapped HK clip '{name}' -> Hornet '{mapped}' on '{self.gameObject.name}'");
                if (loopStartClips.Contains(name)) self.PlayFromFrame(clip, clip.loopStart);
                else orig(self, mapped);

                if (loopingClips.Contains(mapped)) StartCoroutine(SendAnimationCompletionEvent(self, clip));

                return;
            }

            LogDebugOnce($"clip|{self.gameObject.name}|{name}",
                $"missing clip '{name}' on '{self.gameObject.name}' -> skipped (needs Hornet mapping)");
            return;
        }

        orig(self!, name);
    }

    private static IEnumerator SendAnimationCompletionEvent(tk2dSpriteAnimator anim, tk2dSpriteAnimationClip clip) {
        var last = (clip.frames?.Length ?? 1) - 1;
        while (anim && anim.Playing && anim.CurrentClip == clip && anim.CurrentFrame < last) yield return null;
        if (!anim) yield break;
        anim.AnimationEventTriggered?.Invoke(anim, clip, 0);
        yield return null;
        if (anim) anim.AnimationCompleted?.Invoke(anim, clip);
    }
}
