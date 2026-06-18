extern alias Silksong;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;

namespace HornetPlayer.Playground;

// PlayMaker resolves FSM action types by full name via ReflectionUtils.GetGlobalType. Two problems for us (we use
// HK's PlayMaker — PlayMakerFSM's MonoScript isn't remapped):
//  1. Its fallback assembly scan is cached ONCE (assemblyNames/loadedAssemblies) before our runtime-loaded
//     Silksong.AssemblyCSharp is in the AppDomain -> Silksong-only actions never found -> "Could Not Create Action".
//  2. GetGlobalType tries `Type.GetType(name + ",Assembly-CSharp")` FIRST, so any action name that exists in BOTH
//     HK and Silksong (e.g. SetPolygonCollider) resolves to HK's version (wrong field layout -> NullRef in OnEnter).
// Fix: seed PlayMaker's `typeLookup` (checked FIRST in GetGlobalType) with Silksong.AssemblyCSharp's action types,
// so every game action resolves to OUR version. Also reset the stale assembly cache for non-action GetGlobalType
// lookups (FsmObject/enum types, etc.).
internal static class PlayMakerFix {
    internal static void Apply() {
        ResetTypeCache();
        SeedSilksongActionTypes();
    }

    private static void ResetTypeCache() {
        try {
            var t = typeof(ReflectionUtils);
            var f = BindingFlags.NonPublic | BindingFlags.Static;
            t.GetField("assemblyNames", f)?.SetValue(null, null);
            t.GetField("loadedAssemblies", f)?.SetValue(null, null);
            Log.Info("[PlayMakerFix] ReflectionUtils assembly cache reset");
        } catch (Exception e) {
            Log.Error($"[PlayMakerFix] cache reset failed: {e}");
        }
    }

    private static void SeedSilksongActionTypes() {
        try {
            // Build name -> Silksong type map for the game's PlayMaker action/util types.
            var asm = typeof(Silksong::HeroController).Assembly; // Silksong.AssemblyCSharp
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            // SCOPED: only seed Silksong-EXCLUSIVE action names. PlayMaker (typeLookup/ActionTypeLookup) is shared
            // with HK; seeding a name HK ALSO has would make HK's own FSMs (scene fade/darkness on entry+respawn)
            // resolve OUR version -> they break (screen stays dark). So skip any name HK's Assembly-CSharp defines —
            // those keep resolving to HK's. Hornet's Silksong-only actions still resolve to ours.
            var map = new System.Collections.Generic.Dictionary<string, Type>();
            var collisions = new System.Collections.Generic.List<string>();
            foreach (var t in types) {
                var ns = t?.Namespace;
                if (ns != "HutongGames.PlayMaker.Actions" && ns != "HutongGames.PlayMaker") continue;
                if (Type.GetType(t!.FullName + ", Assembly-CSharp") != null) { collisions.Add(t.FullName!); continue; }
                map[t.FullName!] = t;
            }
            // Un-clobber any colliding names a previous (unscoped) seed wrote, so HK's FSMs re-resolve to HK's.
            RemoveFrom(typeof(ReflectionUtils), "typeLookup", collisions);
            RemoveFrom(typeof(ActionData), "ActionTypeLookup", collisions);
            Log.Info($"[PlayMakerFix] {collisions.Count} colliding action names left to HK (scoped seed)");

            // Seed BOTH caches: ReflectionUtils.typeLookup (used by GetGlobalType) AND ActionData.ActionTypeLookup
            // (GetActionType's OWN cache, checked first — HK's menu FSMs populate it with HK's colliding versions
            // before our seed, so seeding only ReflectionUtils wasn't enough). Both keyed by full action name; our
            // Silksong types override HK's, so colliding actions (e.g. SetPolygonCollider) resolve to OUR layout.
            var seededRefl = SeedInto(typeof(ReflectionUtils), "typeLookup", map);
            var seededAction = SeedInto(typeof(ActionData), "ActionTypeLookup", map);
            Log.Info($"[PlayMakerFix] seeded {map.Count} Silksong action types (typeLookup={seededRefl}, ActionTypeLookup={seededAction})");
        } catch (Exception e) {
            Log.Error($"[PlayMakerFix] seed failed: {e}");
        }
    }

    private static void RemoveFrom(Type owner, string field, System.Collections.Generic.List<string> keys) {
        var dict = (IDictionary?)owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (dict == null) return;
        foreach (var k in keys) if (dict.Contains(k)) dict.Remove(k);
    }

    private static bool SeedInto(Type owner, string field, System.Collections.Generic.Dictionary<string, Type> map) {
        var dict = (IDictionary?)owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (dict == null) { Log.Error($"[PlayMakerFix] {owner.Name}.{field} not found"); return false; }
        foreach (var kv in map) dict[kv.Key] = kv.Value;
        return true;
    }
}
