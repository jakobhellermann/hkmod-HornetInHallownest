using System;
using System.Collections.Generic;
using HornetPlayer.HornetInHallownest.Core;

namespace HornetPlayer.HornetInHallownest.Modules;

// HK FSMs animate the hero via tk2dSpriteAnimator.Play(clipName) with HK Knight clip names.
// Map them to equivalent hornet clips. 
// Required for FSMs waiting for certain animation events.
public sealed class AnimationRemapModule : ModuleBase {
    private static readonly Dictionary<string, string> clipMap = new() {
        ["Collect SD 1"] = "Collect Normal 3",
        ["Collect SD 1 Back"] = "Collect Normal 3",
        ["Collect SD 2"] = "Collect Normal 3",
        ["Collect SD 3"] = "Collect Normal 3",
        ["Collect SD 4"] = "Collect Normal 3",
        ["Collect Heart Piece End"] = "Collect Normal 3",
        ["Challenge Start"] = "Taunt" // required AnimationCompleted (e.g. for mantis lord accept), so no LoopSection wrap mode like "Challenge Strong"
    };

    public override string Id => "animation-remap";

    public override void Initialize() {
        Detour(typeof(tk2dSpriteAnimator), "Play", OnPlay, typeof(string));
    }

    private void OnPlay(Action<tk2dSpriteAnimator, string> orig, tk2dSpriteAnimator self, string name) {
        if (self && !string.IsNullOrEmpty(name) && self.GetClipByName(name) == null) {
            if (clipMap.TryGetValue(name, out var mapped) && self.GetClipByName(mapped) != null) {
                LogDebugOnce($"clipmap|{self.gameObject.name}|{name}",
                    $"remapped HK clip '{name}' -> Hornet '{mapped}' on '{self.gameObject.name}'");
                orig(self, mapped);
                return;
            }

            LogDebugOnce($"clip|{self.gameObject.name}|{name}",
                $"missing clip '{name}' on '{self.gameObject.name}' -> skipped (needs Hornet mapping)");
            return; 
        }

        orig(self!, name);
    }
}
