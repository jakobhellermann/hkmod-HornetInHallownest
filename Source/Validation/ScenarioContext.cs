using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HornetInHallownest.Validation;

// Handed to a scenario's Run: assertion helpers + the failure list. Assertions accumulate (a scenario keeps running
// after a failed assert so we report all failures, not just the first). The runner appends any captured Unity
// Exceptions/Errors to the same list before computing the verdict, so Passed reflects both explicit assertions and
// the in-process zero-error check.
public sealed class ScenarioContext {
    public List<string> Failures { get; } = new();

    public bool Passed => Failures.Count == 0;

    public void Fail(string msg) {
        Failures.Add(msg);
    }

    public void Assert(bool cond, string msg) {
        if (!cond) Failures.Add(msg);
    }

    public void AssertEqual<T>(T expected, T actual, string what) {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Failures.Add($"{what}: expected '{expected}', got '{actual}'");
    }

    public IEnumerator WaitFrames(int n) {
        for (var i = 0; i < n; i++) yield return null;
    }

    public IEnumerator WaitSeconds(float s) {
        yield return new WaitForSeconds(s);
    }
}
