extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HornetInHallownest.Util;

namespace HornetInHallownest.Core;

// Hornet's FSMs run on the isolated, prefixed Silksong.PlayMaker runtime, whose ReflectionUtils.GetGlobalType hardcodes
// `,Assembly-CSharp` (a string literal the assembly-rename can't touch) and so would resolve Silksong FSM action types
// to HK's. Seed its type caches (consulted before that literal) with every Silksong type; the caches are private to
// Silksong.PlayMaker, so HK's own resolution is untouched.
internal static class SilksongPlayMaker {
    // Assemblies containing PlayMaker actions.
    private static readonly string[] extraActionAssemblies = [
        "Silksong.TeamCherry.NestedFadeGroup",
        "Silksong.TeamCherry.Localization",
        "Silksong.ConditionalExpression",
    ];

    internal static void Apply() {
        try {
            var types = CollectSilksongTypes();
            SeedCache(typeof(SilksongPM::HutongGames.PlayMaker.ReflectionUtils), "typeLookup", types);
            SeedCache(typeof(SilksongPM::HutongGames.PlayMaker.ActionData), "ActionTypeLookup", types);
        } catch (Exception e) {
            Log.Error($"[SilksongPlayMaker] {e}");
        }
    }

    // Seeds a process-lifetime static cache; nothing to undo.
    internal static void Cleanup() { }

    private static Dictionary<string, Type> CollectSilksongTypes() {
        var byName = new Dictionary<string, Type>();
        AddTypes(byName, typeof(Silksong::HeroController).Assembly);
        AddTypes(byName, typeof(SilksongPM::HutongGames.PlayMaker.ActionData).Assembly);
        foreach (var name in extraActionAssemblies)
            try {
                AddTypes(byName, Assembly.LoadFrom($"{Paths.HkManagedDir}/{name}.dll"));
            } catch (Exception e) {
                Log.Error($"[SilksongPlayMaker] load {name}: {e.Message}");
            }

        return byName;
    }

    private static void AddTypes(Dictionary<string, Type> byName, Assembly asm) {
        Type?[] types;
        try {
            types = asm.GetTypes();
        } catch (ReflectionTypeLoadException e) {
            types = e.Types;
        }

        foreach (var t in types)
            if (t?.FullName != null)
                byName[t.FullName] = t;
    }

    private static void SeedCache(Type owner, string field, Dictionary<string, Type> types) {
        if (owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) is not IDictionary cache) {
            Log.Error($"[SilksongPlayMaker] {owner.Name}.{field} not found");
            return;
        }

        foreach (var kv in types)
            cache[kv.Key] = kv.Value;
    }
}
