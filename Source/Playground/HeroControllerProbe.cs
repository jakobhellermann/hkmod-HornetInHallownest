using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Cecil = Mono.Cecil.Cil;

namespace HornetPlayer.Playground;

// DIAGNOSTIC (log-only): which HK HeroController methods are still called on the KNIGHT while Hornet is the active hero?
//
// The whole seam is "HK environment + Silksong hero". HK systems (enemies, hazards, interaction FSMs, camera) only know
// HK's `HeroController.instance` == the Knight, so while Hornet is active they keep talking to the inert Knight: enemies
// aggro/recoil toward the Knight's position, interaction gates read the Knight's (inert) state, etc. HeroSwitch already
// hand-redirects the three methods it found empirically (CanInteract/CanInput/GetState). This probe is the GENERAL
// version of that hunt: instrument EVERY public method on HK's HeroController and, while HornetActive, log the first
// distinct CALLER of each (so we see *who* grabs the Knight, not just *that* it happens). The Denylist is the
// "this one's fine / already handled" set.
//
// Mechanism: MonoMod ILHook that PREPENDS `ldstr label; call Probe.Hit(label)` to each method body (does NOT clear the
// body — orig still runs). The injected call is a single static-property compare while Knight-active (HornetActive ==
// false) -> ~free during normal play. While HornetActive it counts per-label and walks a stack trace only until each
// label has surfaced a few distinct callers (then it just counts) -> bounded cost even on hot methods like get_instance.
//
// NOTE: this catches METHOD calls only. Enemies that read `HeroController.instance.transform.position` are caught at the
// `get_instance` hook (the entry point); the subsequent `.transform`/`.position` are plain Component/Transform members
// and are NOT hooked. So a get_instance caller IS the smoking gun for "fly toward the Knight".
internal static class HeroControllerProbe {
    // How many distinct callers to surface per label before we stop walking the stack (just keep counting).
    private const int CallerCap = 8;
    private static readonly List<ILHook> hooks = new();
    private static readonly Dictionary<string, long> counts = new();
    private static readonly Dictionary<string, int> distinctCallers = new();
    private static readonly HashSet<string> loggedCallerKeys = new();

    internal static bool Enabled = false;

    // Methods that are fine / already handled / not interesting — don't log (still hooked, just early-out).
    private static readonly HashSet<string> Denylist = new() {
        // Already hand-redirected to RealHero in HeroSwitch:
        "CanInteract", "CanInput", "GetState"
    };

    internal static void Install() {
        if (hooks.Count > 0) return;

        var methods = typeof(HeroController).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var n = 0;
        foreach (var mi in methods) {
            if (mi.IsAbstract || mi.ContainsGenericParameters || mi.GetMethodBody() == null) continue;
            // Skip the operator/special junk; keep get_/set_ accessors (state reads/writes are interesting).
            var label = mi.Name + (mi.IsStatic ? " (static)" : "");
            try {
                hooks.Add(new ILHook(mi, il => Prepend(il, label)));
                n++;
            } catch (Exception e) {
                Log.Error($"[HeroControllerProbe] hook failed {label}: {e.Message}");
            }
        }

        Log.Debug($"[HeroControllerProbe] installed on {n} HK HeroController methods (log-only; while HornetActive)");
    }

    private static void Prepend(ILContext il, string label) {
        var c = new ILCursor(il);
        c.Goto(0);
        c.Emit(Cecil.OpCodes.Ldstr, label);
        c.Emit(Cecil.OpCodes.Call, typeof(HeroControllerProbe).GetMethod(nameof(Hit))!);
    }

    // Called at the top of every hooked HK HeroController method.
    public static void Hit(string label) {
        if (!Enabled || !HeroSwitch.HornetActive) return;
        counts.TryGetValue(label, out var c);
        counts[label] = c + 1;

        if (Denylist.Contains(label)) return;
        distinctCallers.TryGetValue(label, out var seen);
        if (seen >= CallerCap) return; // saturated: keep counting, stop walking the stack

        var caller = FirstExternalCaller(out var trace);
        var key = label + " <- " + caller;
        if (!loggedCallerKeys.Add(key)) return;
        distinctCallers[label] = seen + 1;
        Log.Info($"[HeroControllerProbe] KNIGHT.{label}  <-  {caller}\n{trace}");
    }

    // Walk past the probe + the hooked HeroController frame(s) (incl. MonoMod trampolines) to the first real caller.
    private static string FirstExternalCaller(out string trace) {
        var st = new StackTrace(1, false);
        var frames = st.GetFrames();
        var first = "?";
        var lines = new List<string>();
        if (frames != null)
            foreach (var f in frames) {
                var m = f.GetMethod();
                var dt = m?.DeclaringType;
                if (m == null) continue;
                var name = (dt?.FullName ?? "?") + "." + m.Name;
                // skip our own probe frames and HK HeroController's own internal frames
                if (dt == typeof(HeroControllerProbe) || dt == typeof(HeroController)) continue;
                if (first == "?") first = name;
                lines.Add("    at " + name);
                if (lines.Count >= 6) break;
            }

        trace = string.Join("\n", lines);
        return first;
    }

    // GET /hc-probe — dump the call counts + distinct-caller tallies, busiest first.
    internal static object Dump() {
        var rows = new List<object>();
        var keys = new List<string>(counts.Keys);
        keys.Sort((a, b) => counts[b].CompareTo(counts[a]));
        foreach (var k in keys) {
            distinctCallers.TryGetValue(k, out var dc);
            rows.Add(new { method = k, calls = counts[k], distinctCallers = dc, denied = Denylist.Contains(k) });
        }

        return new { enabled = Enabled, hooks = hooks.Count, hits = rows };
    }

    internal static object Reset() {
        counts.Clear();
        distinctCallers.Clear();
        loggedCallerKeys.Clear();
        return new { reset = true };
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        counts.Clear();
        distinctCallers.Clear();
        loggedCallerKeys.Clear();
    }
}
