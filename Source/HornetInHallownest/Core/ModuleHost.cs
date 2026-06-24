using System;
using System.Collections.Generic;
using System.Linq;
using Log =
    HornetPlayer.Playground.Log; // TEMP: Log is shared infra slated to move into Core; not a Playground-diagnostics dep.

namespace HornetPlayer.HornetInHallownest.Core;

// The single ordered list of lifecycle modules — one source of truth for order. InitializeAll() runs forward,
// DeinitializeAll() runs reverse, which replaces the old hand-maintained mirror teardown list in HornetPlayerMod.
// Modules are addressable by Id so the validation runner can Disable/Enable exactly one in a live instance.
//
// Each module's Initialize/Deinitialize is wrapped in try/catch: one module failing to come up (or tear down) must
// not abort the rest — we log it and continue, matching the resilience of the old flat list.
public sealed class ModuleHost {
    private readonly HashSet<string> active = new();
    private readonly List<IModule> modules = new();

    public IReadOnlyList<IModule> Modules => modules;

    public ModuleHost Add(IModule m) {
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

    public void Tick() {
        foreach (var m in modules) {
            if (m is not ITickable t || !active.Contains(m.Id)) continue;
            try {
                t.Tick();
            } catch (Exception e) {
                Log.Error($"[ModuleHost] tick '{m.Id}': {e}");
            }
        }
    }

    public IModule? Get(string id) {
        return modules.FirstOrDefault(m => m.Id == id);
    }

    public bool IsActive(string id) {
        return active.Contains(id);
    }

    // Runtime toggle for the validation runner. Disable tears a single module down ("does a minimal variant / more
    // bring-up carry without it?"); Enable brings it back. Both no-op (return false) if the Id is unknown or already
    // in the requested state, so the runner can report exactly what it toggled.
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
