using System;
using System.Collections.Generic;
using System.Reflection;

namespace Silksong;

// Shims for Silksong's IL-emit-based fast field accessors (Assembly-CSharp's `FieldAccessOptimizer<T,F>` and its
// `BoolFieldAccessOptimizer<T> : FieldAccessOptimizer<T,bool>`, absent in HK). PlayerData/HeroControllerStates use
// these for string-named field access from FSMs/cheats — not a hot path — so cached reflection is behaviourally
// equivalent. Kept in Adapt/ so the decompiled sources stay pristine.
public class FieldAccessOptimizer<TTarget, TField> {
    private static readonly Dictionary<string, FieldInfo?> cache = new();

    private static FieldInfo? Resolve(string name) {
        if (cache.TryGetValue(name, out var fi)) return fi;
        fi = typeof(TTarget).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        cache[name] = fi;
        return fi;
    }

    public TField GetField(TTarget target, string name) {
        var fi = Resolve(name);
        return fi != null ? (TField)fi.GetValue(target) : default!;
    }

    public void SetField(TTarget target, string name, TField value) => Resolve(name)?.SetValue(target, value);

    public bool FieldExists(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
}

public sealed class BoolFieldAccessOptimizer<TTarget> : FieldAccessOptimizer<TTarget, bool> { }
