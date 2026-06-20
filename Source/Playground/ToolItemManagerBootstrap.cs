extern alias Silksong;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// SURGICAL bring-up of Silksong's `ToolItemManager` (open item #6) — the singleton the tools/crests/nail-art systems
// deref. It carries two serialized assets: `toolItems` (ToolItemList) + `crestList` (ToolCrestList). Bringing up the
// whole `_GameManager` prefab to get it was TRIED + REJECTED (commit `um`): activating it reaches into HK-owned scene/
// render + hero crest-state/FSM-init and does not converge. So we do the opposite of broad: we never INSTANTIATE the
// prefab (no Awake on its 18 managers). We load the prefab ASSET, read the two SerializeField asset refs straight off
// its ToolItemManager component, and copy them onto a FRESH GameObject carrying only a ToolItemManager. Activating that
// GO runs exactly ONE Awake — ToolItemManager's own (event subs to our bootstrap GameManager.instance, harmless) —
// which registers the ManagerSingleton. Mirrors GameCamerasBootstrap's "real Awake on its own GO" pattern.
internal static class ToolItemManagerBootstrap {
    private const string GoName = "Silksong_ToolItemManager";
    private static GameObject? go;

    private static FieldInfo ToolItemsField => typeof(Silksong::ToolItemManager)
        .GetField("toolItems", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static FieldInfo CrestListField => typeof(Silksong::ToolItemManager)
        .GetField("crestList", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // Hot-reload safe: the GO is DontDestroyOnLoad and survives our DLL reload (the `go` static does not). Reuse it.
    private static GameObject? FindExisting() {
        foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            if (g != null && g.name == GoName && g.scene.IsValid()) return g;
        return null;
    }

    internal static object Ensure() {
        try {
            go ??= FindExisting();
            if (go != null) {
                // Re-register the Silksong-assembly static in case it dangled across the reload.
                var existing = go.GetComponent<Silksong::ToolItemManager>();
                if (existing != null && Silksong::ManagerSingleton<Silksong::ToolItemManager>.UnsafeInstance == null)
                    SetSingleton(existing);
                return new { ok = true, note = "reused existing", instanceSet = Silksong::ToolItemManager.SilentInstance != null };
            }

            AddressablesBootstrap.Ensure();
            // Load the prefab ASSET (not instantiated -> no Awakes). 533 deps mount the bundles that carry the tool/crest
            // ScriptableObjects, which is exactly what we want resident. GetComponentInChildren(true) finds the
            // ToolItemManager wherever it sits on the prefab (root or child).
            var prefab = Addressables.LoadAssetAsync<GameObject>("_GameManager").WaitForCompletion();
            if (prefab == null) return new { error = "_GameManager load returned null" };
            var template = prefab.GetComponentInChildren<Silksong::ToolItemManager>(true);
            if (template == null) return new { error = "ToolItemManager not found on _GameManager prefab" };

            // Read the serialized asset refs off the prefab's component. Instantiate does NOT clone ScriptableObject
            // refs (they point at the shared bundle assets), so reading straight from the prefab asset is equivalent
            // and avoids cloning the GameObject entirely.
            var toolItems = ToolItemsField.GetValue(template);
            var crestList = CrestListField.GetValue(template);

            go = new GameObject(GoName);
            go.SetActive(false); // inactive -> AddComponent does NOT fire Awake yet; set fields first
            var mgr = go.AddComponent<Silksong::ToolItemManager>();
            ToolItemsField.SetValue(mgr, toolItems);
            CrestListField.SetValue(mgr, crestList);
            Object.DontDestroyOnLoad(go);
            go.SetActive(true); // -> ToolItemManager.Awake: base.Awake registers the singleton, cursedCrest lookup, etc.

            // base.Awake should have set it; belt-and-suspenders in case Awake bailed.
            if (Silksong::ManagerSingleton<Silksong::ToolItemManager>.UnsafeInstance == null) SetSingleton(mgr);

            int tools = CountList(toolItems), crests = CountList(crestList);
            var instanceSet = Silksong::ToolItemManager.SilentInstance != null;
            Log.Info($"[ToolItemManager] up: instance={instanceSet}, toolItems={tools}, crests={crests}");
            return new { ok = true, instanceSet, toolItems = tools, crests };
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            Log.Error($"[ToolItemManager] bootstrap failed: {ex}");
            return new { error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    // Diagnostic: confirm the singleton resolves and the serialized lists are populated, and probe GetCrestByName
    // (which relies on the SO's OnEnable-built name dictionary — verifies that fired on bundle load).
    internal static object Diag() {
        var mgr = Silksong::ToolItemManager.SilentInstance;
        if (mgr == null) return new { error = "ToolItemManager singleton null (Ensure not run / spawn first)" };
        var crests = Silksong::ToolItemManager.GetAllCrests();
        var tools = Silksong::ToolItemManager.GetUnlockedTools().ToList();
        var crestId = Silksong::PlayerData.instance?.CurrentCrestID;
        var byName = string.IsNullOrEmpty(crestId) ? null : Silksong::ToolItemManager.GetCrestByName(crestId);
        return new {
            instanceSet = true,
            crestCount = crests.Count,
            crestNames = crests.Select(c => c.name).ToArray(),
            unlockedToolCount = tools.Count,
            currentCrestID = crestId,
            getCrestByNameResolves = byName != null,
        };
    }

    private static void SetSingleton(Silksong::ToolItemManager mgr) =>
        typeof(Silksong::ManagerSingleton<Silksong::ToolItemManager>)
            .GetProperty("UnsafeInstance", BindingFlags.Public | BindingFlags.Static)
            ?.GetSetMethod(true)?.Invoke(null, new object[] { mgr });

    private static int CountList(object? list) =>
        list is System.Collections.IEnumerable e ? e.Cast<object>().Count() : -1;

    internal static void Cleanup() {
        go ??= FindExisting();
        if (go != null) { Object.DestroyImmediate(go); go = null; } // OnDestroy clears UnsafeInstance
    }
}
