extern alias Silksong;
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;
using HornetInHallownest.Core;
using HornetInHallownest.Util;

namespace HornetInHallownest.Bootstrap;

// Silksong's `_GameManager` prefab holds all managers on one GO, and activating it takes over HK's scene and rendering.
// So each manager gets its own GO with the serialized fields copied off the prefab.
internal static class ManagerSingletonBootstrap {
    private static GameObject? prefab;

    private static GameObject Prefab() {
        if (prefab != null) return prefab;
        SilksongAddressables.EnsureMounted();
        prefab = Addressables.LoadAssetAsync<GameObject>("_GameManager").WaitForCompletion();
        return prefab;
    }

    // The DontDestroyOnLoad manager GOs survive a hot-reload, our statics don't.
    private static GameObject? FindExisting(string goName) {
        foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            if (g != null && g.name == goName && g.scene.IsValid())
                return g;
        return null;
    }

    // Its Awake registers the ManagerSingleton.
    internal static Component BringUp(Type managerType, string goName, params string[] serializedFields) {
        if (FindExisting(goName) is { } existing) {
            var comp = existing.GetComponent(managerType);
            SetSingleton(managerType, comp);
            return comp;
        }

        var template = Prefab().GetComponent(managerType);

        var go = new GameObject(goName);
        go.SetActive(false);
        var mgr = go.AddComponent(managerType);
        foreach (var name in serializedFields) {
            var f = managerType.GetFieldInfo(name, ReflectionExtension.InstanceAnyVisibility)!;
            f.SetValue(mgr, f.GetValue(template));
        }

        Object.DontDestroyOnLoad(go);
        go.SetActive(true);
        return mgr;
    }

    // Without an instance, ManagerSingleton<T>.Instance scans the scene with FindAnyObjectByType on every access.
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

    private static Type ClosedManagerSingleton(Type managerType) {
        return typeof(Silksong::ManagerSingleton<>).MakeGenericType(managerType);
    }

    private static object? SilentInstance(Type managerType) {
        return ClosedManagerSingleton(managerType).GetPropertyValue<object>("SilentInstance");
    }

    private static void SetSingleton(Type managerType, Component mgr) {
        ClosedManagerSingleton(managerType).SetPropertyValue("UnsafeInstance", mgr);
    }

    // Immediate, or a hot-reload's BringUp would reuse the doomed GO.
    internal static void Destroy(string goName) {
        var g = FindExisting(goName);
        if (g != null) Object.DestroyImmediate(g);
    }

    internal static void Cleanup() {
        prefab = null;
    }
}
