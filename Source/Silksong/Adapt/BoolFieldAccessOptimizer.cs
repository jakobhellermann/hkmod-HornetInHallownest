using System;
using System.Collections.Generic;
using System.Reflection;

namespace Silksong;

// Shim for Silksong's IL-emit-based fast bool field accessor (Assembly-CSharp defines
// `BoolFieldAccessOptimizer<T> : FieldAccessOptimizer<T, bool>`, which HK doesn't have). HeroControllerStates only
// uses it for string-named state access from FSMs (GetState/SetState/HasState) — not the locomotion hot path — so a
// plain cached-reflection implementation is behaviourally equivalent, just slower on those calls.
//
// This lives in a separate Adapt/ file on purpose: the decompiled Decompiled/*.cs stay byte-for-byte what ilspycmd
// emits (modulo the namespace wrap), so re-extracting on a new game version produces a clean diff. Adaptations go
// here, never by editing the decompiled sources.
public sealed class BoolFieldAccessOptimizer<TTarget> {
    private static readonly Dictionary<string, FieldInfo?> cache = new();

    private static FieldInfo? Resolve(string name) {
        if (cache.TryGetValue(name, out var fi)) return fi;
        fi = typeof(TTarget).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        cache[name] = fi;
        return fi;
    }

    public bool GetField(TTarget target, string name) {
        var fi = Resolve(name);
        return fi != null && (bool)fi.GetValue(target);
    }

    public void SetField(TTarget target, string name, bool value) {
        Resolve(name)?.SetValue(target, value);
    }

    public bool FieldExists(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
}
