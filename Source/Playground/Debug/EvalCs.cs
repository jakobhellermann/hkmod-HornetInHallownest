using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HornetPlayer.Playground;

// POST /eval-cs: compile + run a C# snippet on UnityExplorer's Mono.CSharp evaluator (reflection, no compile-time dep).
// Body = C# source; end with a trailing expression (REPL-style, no `return`) to get it back as `result`. Debug-only.
internal static class EvalCs {
    private static void ReferenceAppdomainAssemblies(object evaluator) {
        var refMethod = evaluator.GetType().GetMethod("ReferenceAssembly", new[] { typeof(Assembly) });
        if (refMethod == null) {
            Log.Error("[eval-cs] Evaluator.ReferenceAssembly(Assembly) not found");
            return;
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
            if (asm.IsDynamic) continue;
            try {
                refMethod.Invoke(evaluator, new object[] { asm });
            } catch {
                // some assemblies aren't importable (dynamic/no-location); skip them like UnityExplorer's own ctor does
            }
        }
    }

    private static void UnsubscribeAssemblyLoad(object evaluator) {
        try {
            var mi = evaluator.GetType().GetMethod("OnAssemblyLoad", BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi == null) return;
            var handler = (AssemblyLoadEventHandler)Delegate.CreateDelegate(typeof(AssemblyLoadEventHandler), evaluator, mi);
            AppDomain.CurrentDomain.AssemblyLoad -= handler;
        } catch (Exception e) {
            Log.Error($"[eval-cs] failed to detach ScriptEvaluator.OnAssemblyLoad: {e.Message}");
        }
    }

    internal static object Run(string? source) {
        if (string.IsNullOrWhiteSpace(source))
            return new { ok = false, error = "empty C# source (POST it in the request body)" };

        var ccType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityExplorer.CSConsole.ConsoleController"))
            .FirstOrDefault(t => t != null);
        if (ccType == null)
            return new { ok = false, error = "UnityExplorer.CSConsole.ConsoleController not found, is UnityExplorer loaded?" };

        var evaluatorProp = ccType.GetProperty("Evaluator", BindingFlags.Public | BindingFlags.Static);
        var evaluator = evaluatorProp?.GetValue(null);
        if (evaluator == null) {
            // UnityExplorer only creates the evaluator when its C# console panel is first opened (ConsoleController.Init).
            // ResetConsole(false) does just the headless part (new ScriptEvaluator + evaluatorOutput + default usings, no
            // UI wiring), so /eval-cs works without the console ever being opened.
            ccType.GetMethod("ResetConsole", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null)
                ?.Invoke(null, new object[] { false });
            evaluator = evaluatorProp?.GetValue(null);

            // ScriptEvaluator.Reference() dereferences UnityExplorer's ConfigManager.CSConsole_Assembly_Blacklist,
            // which our headless ResetConsole leaves null -> it NREs for every assembly. That breaks the evaluator two
            // ways: the ctor's ImportAppdomainAssemblies swallows the NRE and ends up referencing nothing (so even
            // `using UnityEngine;` won't compile), and the OnAssemblyLoad subscription re-throws it on every later
            // AssemblyLoad, aborting mod hot-reload. Fix both: reference the appdomain assemblies ourselves via the base
            // ReferenceAssembly (bypassing the null-blacklist check), and drop the OnAssemblyLoad subscription.
            if (evaluator != null) {
                UnsubscribeAssemblyLoad(evaluator);
                ReferenceAppdomainAssemblies(evaluator);
            }
        }

        if (evaluator == null) return new { ok = false, error = "UnityExplorer evaluator not initialized (ResetConsole failed, SRE unsupported?)" };

        var compile = evaluator.GetType().GetMethod("Compile", BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(string) }, null);
        if (compile == null) return new { ok = false, error = "Evaluator.Compile(string) not found" };

        var errorBuffer = ccType.GetField("evaluatorOutput", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as System.Text.StringBuilder;
        errorBuffer?.Clear();

        var output = new List<string>();
        void Capture(string condition, string stackTrace, LogType logType) {
            lock (output) output.Add(logType == LogType.Log ? condition : $"{logType}: {condition}");
        }
        List<string> Logs() { lock (output) return output.ToList(); }

        Application.logMessageReceivedThreaded += Capture;
        try {
            if (compile.Invoke(evaluator, new object[] { source! }) is not Delegate compiled) {
                var err = errorBuffer?.ToString().Trim();
                return new { ok = false, error = string.IsNullOrEmpty(err) ? "compilation failed" : err, output = Logs() };
            }

            var sentinel = new object();
            var args = new object?[] { sentinel };
            compiled.DynamicInvoke(args);
            if (ReferenceEquals(args[0], sentinel))
                return new { ok = true, result = (string?)null, output = Logs(), hint = "end with a trailing expression to get `result`" };
            return new { ok = true, result = args[0]?.ToString(), output = Logs() };
        } catch (Exception e) {
            return new { ok = false, error = (e.InnerException ?? e).ToString(), output = Logs() };
        } finally {
            Application.logMessageReceivedThreaded -= Capture;
        }
    }
}
