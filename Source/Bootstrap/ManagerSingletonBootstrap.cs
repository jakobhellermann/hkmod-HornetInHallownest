extern alias Silksong;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;
using HornetInHallownest.Core;
using HornetInHallownest.Util;

namespace HornetInHallownest.Playground;

// Shared mechanism for the surgical manager bring-ups. Silksong's `_GameManager` prefab carries ~17 ManagerSingletons
// on one root GO, so we can't activate just one (Unity Awakes every component), and the whole prefab was tried +
// rejected (reaches into HK-owned scene/render). Instead: load the prefab asset, copy the wanted manager's serialized
// [SerializeField] asset refs onto a fresh single-manager GO, activate it -> only its own Awake runs (registers the
// ManagerSingleton).
internal static class ManagerSingletonBootstrap {
    private static GameObject? prefab; // the loaded _GameManager prefab asset (donor of serialized field values)

    private static GameObject? Prefab() {
        if (prefab != null) return prefab;
        SilksongAddressables.EnsureMounted();
        // Deps mount the bundles carrying the managers' ScriptableObjects; Addressables caches, so repeated calls cheap.
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
        go.SetActive(false); // inactive -> AddComponent does not fire Awake yet; copy fields first
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
        Log.Debug($"[Manager] {managerType.Name} up: instance={SilentInstance(managerType) != null}");
        return mgr;
    }

    // Register a bare singleton without copying serialized data (unlike BringUp) or running Awake: just enough so
    // ManagerSingleton<T>.Instance stops FindAnyObjectByType-scanning the whole scene on every access while it is null.
    internal static Component RegisterBare(Type managerType, string goName) {
        var existing = FindExisting(goName)?.GetComponent(managerType);
        if (existing != null) {
            if (SilentInstance(managerType) == null) SetSingleton(managerType, existing);
            return existing;
        }

        var go = new GameObject(goName);
        Object.DontDestroyOnLoad(go);
        go.SetActive(false);
        var mgr = go.AddComponent(managerType);
        SetSingleton(managerType, mgr);
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

    // DestroyImmediate (not Object.Destroy) so the GO is gone before a hot-reload's Initialize runs BringUp again.
    internal static void Destroy(string goName) {
        var g = FindExisting(goName);
        if (g != null) Object.DestroyImmediate(g);
    }

    internal static void Cleanup() {
        prefab = null;
        // Addressables owns the asset; just drop our ref
    }
}
