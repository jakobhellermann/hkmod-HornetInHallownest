extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Linq;
using Log =
    HornetInHallownest.Util.Log; // TEMP: Log is shared infra slated to move into Core; not a Playground-diagnostics dep.

namespace HornetInHallownest.Core;

// Ordered list of lifecycle modules 
public sealed class ModuleHost {
    private readonly HashSet<string> active = [];
    private readonly List<ModuleBase> modules = [];

    public void Add(ModuleBase m) {
        if (modules.Any(x => x.Id == m.Id)) throw new ArgumentException($"duplicate module Id '{m.Id}'");
        modules.Add(m);
    }

    public void InitializeAll() {
        foreach (var m in modules)
            try {
                m.Initialize();
                active.Add(m.Id);
            } catch (Exception e) {
                Log.Error($"[ModuleHost] init '{m.Id}': {e}");
            }
    }

    public void HornetActiveUpdate(Silksong::HeroController hero) {
        var paused = ModuleBase.Paused;
        foreach (var m in modules) {
            if (!active.Contains(m.Id)) continue;
            if (paused && !m.RunWhilePaused) continue;
            try {
                m.HornetActiveUpdate(hero);
            } catch (Exception e) {
                Log.Error($"[ModuleHost] update '{m.Id}': {e}");
            }
        }
    }

    public void HornetToggled(bool hornetActive) {
        foreach (var m in modules) {
            if (!active.Contains(m.Id)) continue;
            try {
                m.HornetToggled(hornetActive);
            } catch (Exception e) {
                Log.Error($"[ModuleHost] toggle '{m.Id}': {e}");
            }
        }
    }

    public void DeinitializeAll() {
        for (var i = modules.Count - 1; i >= 0; i--) {
            var m = modules[i];
            if (!active.Remove(m.Id)) continue;
            try {
                m.Deinitialize();
            } catch (Exception e) {
                Log.Error($"[ModuleHost] deinit '{m.Id}': {e}");
            }
        }
    }
}
