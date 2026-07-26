extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Util;
using MonoMod.RuntimeDetour;

namespace HornetInHallownest.Playground;

// Logs who turns HK's input back on while Hornet's inventory holds it off (HK's ESC pause then fires behind it).
internal static class HkInputTracer {
    private static readonly List<Hook> hooks = [];

    internal static void Install() {
        foreach (var name in new[] { nameof(InputHandler.StartAcceptingInput), nameof(InputHandler.StartUIInput) })
            hooks.Add(new Hook(typeof(InputHandler).GetMethod(name)!,
                (Action<Action<InputHandler>, InputHandler>)((orig, self) => {
                    if (Silksong::PlayerData.HasInstance && Silksong::PlayerData.instance.isInventoryOpen)
                        Log.Info($"[HkInputTrace] {name} while Hornet's inventory is open:\n{Environment.StackTrace}");
                    orig(self);
                })));
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
    }
}
