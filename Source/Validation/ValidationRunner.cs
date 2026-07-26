using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.DevServer;
using HornetInHallownest.Util;
using UnityEngine;

namespace HornetInHallownest.Validation;

// In-mod validation engine: the fast loop for the validation-gated migration. Runs registered scenarios against the
// live game (one instance, no clean-reload):
//
//   POST /validate?scenario=<name>&disable=<moduleId,...>
//
// `disable` deactivates modules by Id (via ModuleHost) for the scenario's duration, then restores them. This is how
// we validate "is this shim still needed?" / "does more bring-up carry without it?" without a restart. During Run we
// subscribe to Application.logMessageReceived and treat any Exception/Error as a failure: the in-process zero-error
// verdict (catches engine-level NullRefs etc. that never reach our Log sinks), no Player.log parsing.
public sealed class ValidationRunner {
    private readonly Dictionary<string, IScenario> scenarios = new(StringComparer.OrdinalIgnoreCase);

    public ValidationRunner Register(IScenario s) {
        scenarios[s.Name] = s;
        return this;
    }

    public object List() {
        return new {
            scenarios = scenarios.Keys.OrderBy(k => k).ToArray()
        };
    }

    public IEnumerator RunRoute(DevRequest req, Action<object?> respond) {
        var name = req["scenario"];
        if (string.IsNullOrEmpty(name) || !scenarios.TryGetValue(name, out var scenario)) {
            respond(new { error = $"unknown scenario '{name}'", available = scenarios.Keys.OrderBy(k => k).ToArray() });
            yield break;
        }

        var ctx = new ScenarioContext();
        var engineErrors = new List<string>();
        Application.LogCallback watcher = (msg, _, type) => {
            if (type is LogType.Exception or LogType.Error) engineErrors.Add($"{type}: {msg}");
        };

        // The mod's own Log.Error goes to the modding API (ModLog), not Application.logMessageReceived, so tap the Log
        // sink too, else scenarios miss every shim/bridge error (e.g. ResourcesShim "missing SilksongContext").
        var modErrors = new List<string>();
        var origSink = Log.SinkError;
        Log.SinkError = m => {
            modErrors.Add(m);
            origSink(m);
        };

        Application.logMessageReceived += watcher;
        try {
            yield return null; // let a disable settle one frame before acting
            yield return scenario.Run(ctx);
        } finally {
            Application.logMessageReceived -= watcher;
            Log.SinkError = origSink;
        }

        foreach (var e in engineErrors) ctx.Fail(e);
        foreach (var e in modErrors) ctx.Fail($"Log.Error: {e}");
        Log.Info($"[Validation] {name} -> {(ctx.Passed ? "PASS" : "FAIL")}");
        respond(new {
            scenario = name,
            passed = ctx.Passed,
            failures = ctx.Failures.ToArray()
        });
    }
}
