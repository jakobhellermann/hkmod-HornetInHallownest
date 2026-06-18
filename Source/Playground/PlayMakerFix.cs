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
            PurgeSilksongFromHk();
        } catch (Exception e) {
            Log.Error($"[PlayMakerFix] Apply failed: {e}");
        }
    }

    // Seeding a separate, process-lifetime static cache; nothing to undo on unload.
    internal static void Cleanup() { }

    private static void SeedSilksongPlayMaker() {
        // FullName -> Type for every type a Silksong FSM might reference by name (actions, enums, FsmObject component
        // types), from both prefixed Silksong assemblies.
        var map = new Dictionary<string, Type>();
        AddTypes(map, typeof(Silksong::HeroController).Assembly);                     // Silksong.AssemblyCSharp
        AddTypes(map, typeof(SilksongPM::HutongGames.PlayMaker.ActionData).Assembly); // Silksong.PlayMaker

        var seededRefl = SeedInto(SilksongStatic(typeof(SilksongPM::HutongGames.PlayMaker.ReflectionUtils), "typeLookup"), map);
        var seededAction = SeedInto(SilksongStatic(typeof(SilksongPM::HutongGames.PlayMaker.ActionData), "ActionTypeLookup"), map);
        Log.Info($"[PlayMakerFix] seeded Silksong.PlayMaker (isolated): typeLookup+={seededRefl}, ActionTypeLookup+={seededAction} from {map.Count} types");
    }

    private static void AddTypes(Dictionary<string, Type> map, Assembly asm) {
        Type?[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types; }
        foreach (var t in types)
            if (t?.FullName != null) map[t.FullName] = t;
    }

    private static IDictionary? SilksongStatic(Type owner, string field) =>
        (IDictionary?)owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

    private static int SeedInto(IDictionary? dict, Dictionary<string, Type> map) {
        if (dict == null) return -1;
        foreach (var kv in map) dict[kv.Key] = kv.Value;
        return map.Count;
    }

    // Defensive: an earlier build (global seed / per-FSM hook era) may have cached Silksong action types in HK's
    // SHARED PlayMaker caches, which survive hot-reload (PlayMaker.dll isn't reloaded). Remove any Silksong-typed
    // entries so HK re-resolves to its own. With the prefix in place this is normally a no-op.
    private static void PurgeSilksongFromHk() {
        var silksongAsms = new HashSet<Assembly> {
            typeof(Silksong::HeroController).Assembly,
            typeof(SilksongPM::HutongGames.PlayMaker.ActionData).Assembly,
        };
        PurgeFrom(typeof(HutongGames.PlayMaker.ActionData), "ActionTypeLookup", silksongAsms);
        PurgeFrom(typeof(HutongGames.PlayMaker.ReflectionUtils), "typeLookup", silksongAsms);
    }

    private static void PurgeFrom(Type owner, string field, HashSet<Assembly> silksongAsms) {
        var dict = (IDictionary?)owner.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (dict == null) return;
        var stale = new List<object>();
        foreach (DictionaryEntry e in dict)
            if (e.Value is Type t && silksongAsms.Contains(t.Assembly)) stale.Add(e.Key);
        foreach (var k in stale) dict.Remove(k);
        if (stale.Count > 0) Log.Info($"[PlayMakerFix] purged {stale.Count} stale Silksong entries from HK {owner.Name}.{field}");
    }
}
