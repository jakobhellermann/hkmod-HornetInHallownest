using System;
using System.Collections.Generic;
using System.Linq;
using Log =
    HornetPlayer.Playground.Log; // TEMP: Log is shared infra slated to move into Core; not a Playground-diagnostics dep.

namespace HornetPlayer.HornetInHallownest.Core;

// Ordered list of lifecycle modules 
public sealed class ModuleHost {
    private readonly HashSet<string> active = [];
    private readonly List<ModuleBase> modules = [];

    public IReadOnlyList<ModuleBase> Modules => modules;

    public ModuleHost Add(ModuleBase m) {
        if (modules.Any(x => x.Id == m.Id)) throw new ArgumentException($"duplicate module Id '{m.Id}'");
        modules.Add(m);
        return this;
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

    public ModuleBase? Get(string id) {
        return modules.FirstOrDefault(m => m.Id == id);
    }

    public bool IsActive(string id) {
        return active.Contains(id);
    }

    public bool Disable(string id) {
        var m = Get(id);
        if (m == null || !active.Remove(id)) return false;
        try {
            m.Deinitialize();
        } catch (Exception e) {
            Log.Error($"[ModuleHost] disable '{id}': {e}");
        }

        return true;
    }

    public bool Enable(string id) {
        var m = Get(id);
        if (m == null || active.Contains(id)) return false;
        try {
            m.Initialize();
            active.Add(id);
        } catch (Exception e) {
            Log.Error($"[ModuleHost] enable '{id}': {e}");
            return false;
        }

        return true;
    }
}
