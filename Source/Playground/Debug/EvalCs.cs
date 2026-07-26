using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HornetInHallownest.Util;
using UnityEngine;

namespace HornetInHallownest.Playground;

// POST /eval-cs: compile + run a C# snippet on UnityExplorer's Mono.CSharp evaluator (reflection, no compile-time dep).
// Body = C# source; end with a trailing expression (REPL-style, no `return`) to get it back as `result`. Debug-only.
internal static class EvalCs {
    // The assemblies the evaluator was built against; any mod's hot reload changes them.
    private static string? referencedAssemblies;

    // A hot reload loads a mod again as "<Name>-<ticks>", and only the newest copy has live state.
    private static List<Assembly> NewestAssemblies() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .GroupBy(a => SplitReloadTicks(a.GetName().Name).name)
            .Select(g => g.OrderBy(a => SplitReloadTicks(a.GetName().Name).ticks).Last())
            .ToList();

    private static (string name, long ticks) SplitReloadTicks(string name) {
        var dash = name.LastIndexOf('-');
        return dash > 0 && name.Length - dash > 15 && long.TryParse(name.Substring(dash + 1), out var ticks)
            ? (name.Substring(0, dash), ticks)
            : (name, 0);
    }

    private static void ReferenceAppdomainAssemblies(object evaluator, List<Assembly> assemblies) {
        var refMethod = evaluator.GetType().GetMethod("ReferenceAssembly", new[] { typeof(Assembly) });
        if (refMethod == null) {
            Log.Error("[eval-cs] Evaluator.ReferenceAssembly(Assembly) not found");
            return;
        }

        foreach (var asm in assemblies) {
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

    // Mono.CSharp's Evaluator.Compile prints "replacing fieldspec for <var>" to stdout (Player.log) for every top-level
    // variable of every snippet.
    private static object? CompileQuietly(MethodInfo compile, object evaluator, string source) {
        var stdout = Console.Out;
        var captured = new System.IO.StringWriter();
        Console.SetOut(captured);
        try {
            return compile.Invoke(evaluator, new object[] { source });
        } finally {
            Console.SetOut(stdout);
            foreach (var line in captured.ToString().Split('\n'))
                if (line.Length > 0 && !line.StartsWith("replacing fieldspec for ", StringComparison.Ordinal))
                    stdout.WriteLine(line);
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
        var assemblies = NewestAssemblies();
        var assembliesKey = string.Join(",", assemblies.Select(a => a.GetName().Name));
        var evaluator = referencedAssemblies == assembliesKey ? evaluatorProp?.GetValue(null) : null;
        if (evaluator == null) {
            referencedAssemblies = assembliesKey;
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
                ReferenceAppdomainAssemblies(evaluator, assemblies);
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
            if (CompileQuietly(compile, evaluator, source!) is not Delegate compiled) {
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
