extern alias Silksong;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Shared mechanism for the SURGICAL manager bring-ups (open item #6). Silksong's `_GameManager` prefab carries ~17
// ManagerSingletons (ToolItemManager, CollectableItemManager, QuestManager, …) ALL as components on its ROOT
// GameObject (verified: `rabex addressable _GameManager file tree --scripts` — they share GO #4003832963127629806).
// Because they share one GO we CANNOT activate just one (Unity fires Awake for every component on an active GO), and
// bringing up the whole prefab was TRIED + REJECTED (commit `um`: reaches into HK-owned scene/render, doesn't converge).
//
// So we never INSTANTIATE the prefab. We load the prefab ASSET, read the wanted manager's serialized [SerializeField]
// asset refs straight off its component (Instantiate would NOT clone ScriptableObject refs anyway — they point at the
// shared bundle assets), and copy them onto a FRESH GameObject carrying only that one manager. Activating that GO runs
// exactly ONE Awake — the manager's own — which registers its ManagerSingleton. Mirrors the "real Awake on its own GO"
// pattern (see GameCamerasBootstrap / [[prefer-real-over-reflection]]).
internal static class ManagerSingletonBootstrap {
    private static GameObject? prefab; // the loaded _GameManager prefab ASSET (donor of serialized field values)

    private static GameObject? Prefab() {
        if (prefab != null) return prefab;
        SilksongCatalog.EnsureMounted();
        // 533 deps mount the bundles carrying the managers' ScriptableObjects (tool/crest/collectable lists) — exactly
        // what we want resident. Cached by Addressables, so repeated calls across managers are cheap.
        prefab = Addressables.LoadAssetAsync<GameObject>("_GameManager").WaitForCompletion();
        return prefab;
    }

    // Hot-reload safe: brought-up manager GOs are DontDestroyOnLoad and survive our DLL reload (statics reset). Reuse.
    private static GameObject? FindExisting(string goName) {
        foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            if (g != null && g.name == goName && g.scene.IsValid())
                return g;
        return null;
    }

    // Bring up `managerType` (a Silksong ManagerSingleton<T>) onto its own GO named `goName`, copying the named
    // [SerializeField] values off the _GameManager prefab. Returns the live component (or null on failure).
    internal static Component? BringUp(Type managerType, string goName, params string[] serializedFields) {
        var existing = FindExisting(goName);
        if (existing != null) {
            var comp = existing.GetComponent(managerType);
            if (comp != null && SilentInstance(managerType) == null) SetSingleton(managerType, comp);
            return comp;
        }

        var pf = Prefab();
        if (pf == null) {
            Log.Error($"[Manager] _GameManager load returned null (for {managerType.Name})");
            return null;
        }

        var template = pf.GetComponent(managerType);
        if (template == null) {
            Log.Error($"[Manager] {managerType.Name} not found on _GameManager prefab");
            return null;
        }

        var go = new GameObject(goName);
        go.SetActive(false); // inactive -> AddComponent does NOT fire Awake yet; copy fields first
        var mgr = go.AddComponent(managerType);
        foreach (var name in serializedFields) {
            var f = GetField(managerType, name);
            if (f == null) {
                Log.Error($"[Manager] {managerType.Name}.{name} field not found");
                continue;
            }

            f.SetValue(mgr, f.GetValue(template));
        }

        Object.DontDestroyOnLoad(go);
        go.SetActive(true); // -> the manager's real Awake: base.Awake registers the ManagerSingleton, etc.

        if (SilentInstance(managerType) == null) SetSingleton(managerType, mgr); // belt-and-suspenders if Awake bailed
        Log.Info($"[Manager] {managerType.Name} up: instance={SilentInstance(managerType) != null}");
        return mgr;
    }

    // Walk up the type chain so a [SerializeField] declared on a base class still resolves.
    private static FieldInfo? GetField(Type t, string name) {
        for (var cur = t; cur != null; cur = cur.BaseType) {
            var f = cur.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return f;
        }

        return null;
    }

    private static Type ClosedManagerSingleton(Type managerType) {
        return typeof(Silksong::ManagerSingleton<>).MakeGenericType(managerType);
    }

    private static object? SilentInstance(Type managerType) {
        return ClosedManagerSingleton(managerType)
            .GetProperty("SilentInstance", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null);
    }

    private static void SetSingleton(Type managerType, Component mgr) {
        ClosedManagerSingleton(managerType).GetProperty("UnsafeInstance", BindingFlags.Public | BindingFlags.Static)
            ?.GetSetMethod(true)?.Invoke(null, [mgr]);
    }

    // DestroyImmediate the brought-up GO (its ManagerSingleton.OnDestroy nulls UnsafeInstance) so a mod toggle-off /
    // hot-reload leaves no leaked manager. Synchronous (not Object.Destroy) so it's gone before a reload's Initialize
    // runs BringUp again — matches SilksongBootstrap/GameCamerasBootstrap teardown.
    internal static void Destroy(string goName) {
        var g = FindExisting(goName);
        if (g != null) Object.DestroyImmediate(g);
    }

    internal static void Cleanup() {
        prefab = null;
        // Addressables owns the asset; just drop our ref
    }
}
