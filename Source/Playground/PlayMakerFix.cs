extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace HornetPlayer.Playground;

// HK and Silksong used to share ONE PlayMaker.dll -> one global action-type lookup serving both games -> name
// collisions broke each other's FSMs (HK benches/stag/scene-transitions vs Hornet's). Now PlayMaker is PREFIXED to
// Silksong.PlayMaker (SilksongPrefixer) and the bundle's PlayMakerFSM MonoScripts bind to it (remap_monoscripts), so
// Hornet's FSMs run on a SEPARATE PlayMaker runtime with its OWN static type caches.
//
// Remaining gap: Silksong.PlayMaker.ReflectionUtils.GetGlobalType still hardcodes `,Assembly-CSharp` (a string literal
// the assembly-rename can't touch), so by default it would resolve Silksong FSM action/types to HK's Assembly-CSharp.
// Fix: seed Silksong.PlayMaker's typeLookup (checked before that literal) with every type from Silksong.AssemblyCSharp
// + Silksong.PlayMaker. These caches are PRIVATE to Silksong.PlayMaker, so seeding them is invisible to HK -> HK's
// PlayMaker resolves exactly as vanilla. No runtime hooks, no per-FSM ownership checks.
internal static class PlayMakerFix {
    internal static void Apply() {
        try {
            SeedSilksongPlayMaker();
        } catch (Exception e) {
            Log.Error($"[PlayMakerFix] Apply failed: {e}");
        }
    }

    // Seeding a separate, process-lifetime static cache; nothing to undo on unload.
    internal static void Cleanup() {
    }

    private static void SeedSilksongPlayMaker() {
        // FullName -> Type for every type a Silksong FSM might reference by name (actions, enums, FsmObject component
        // types), from both prefixed Silksong assemblies.
        var map = new Dictionary<string, Type>();
        AddTypes(map, typeof(Silksong::HeroController).Assembly); // Silksong.AssemblyCSharp
        AddTypes(map, typeof(SilksongPM::HutongGames.PlayMaker.ActionData).Assembly); // Silksong.PlayMaker

        // PlayMaker actions also live in 3 small shared assemblies we prefixed (FadeNestedFadeGroup, GetLocalisedString,
        // ConditionalExpression). They aren't referenced by the mod, so load them from lib — else they resolve to the
        // original-PlayMaker variant via GetGlobalType's loaded-assemblies fallback (type mismatch -> can't create).
        foreach (var name in new[] {
                     "Silksong.TeamCherryNestedFadeGroup", "Silksong.TeamCherryLocalization",
                     "Silksong.ConditionalExpression"
                 })
            try {
                AddTypes(map, Assembly.LoadFrom($"{Paths.ManagedDir}/{name}.dll"));
            } catch (Exception e) {
                Log.Error($"[PlayMakerFix] load {name}: {e.Message}");
            }

        var seededRefl =
            SeedInto(SilksongStatic(typeof(SilksongPM::HutongGames.PlayMaker.ReflectionUtils), "typeLookup"), map);
        var seededAction =
            SeedInto(SilksongStatic(typeof(SilksongPM::HutongGames.PlayMaker.ActionData), "ActionTypeLookup"), map);
        Log.Info(
            $"[PlayMakerFix] seeded Silksong.PlayMaker (isolated): typeLookup+={seededRefl}, ActionTypeLookup+={seededAction} from {map.Count} types");
    }

    private static void AddTypes(Dictionary<string, Type> map, Assembly asm) {
        Type?[] types;
        try {
            types = asm.GetTypes();
        } catch (ReflectionTypeLoadException e) {
            types = e.Types;
        }

        foreach (var t in types)
            if (t?.FullName != null)
                map[t.FullName] = t;
    }

    private static IDictionary? SilksongStatic(Type owner, string field) {
        return (IDictionary?)owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
    }

    private static int SeedInto(IDictionary? dict, Dictionary<string, Type> map) {
        if (dict == null) return -1;
        foreach (var kv in map) dict[kv.Key] = kv.Value;
        return map.Count;
    }
}
