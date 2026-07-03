extern alias Silksong;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Bring up Silksong's UIManager singleton so the HUD's components stop NullRef-ing on UIManager.instance (the
// "Couldn't find a UIManager" burst when the in-game HUD activates). UIManager.instance does
// FindObjectOfType<Silksong.UIManager>() and, on a miss, logs that error + returns null — and HK's own `_UIManager` is
// a DIFFERENT type, so it never matches. We load Silksong's `_UIManager` addressable prefab (it carries UICanvas + the
// serialized refs the HUD reads) and set the private static _instance. Kept DORMANT (inactive holder) like the camera
// rig: serialized fields are live on an inactive GO, and we skip UIManager.Awake/Start's heavier menu/canvas setup
// until proven necessary. Mirrors GameCamerasBootstrap.
internal static class UIManagerBootstrap {
    private const string Name = "Silksong_UIManager";
    private static GameObject? ui;

    // Hot-reload safe: the DontDestroyOnLoad holder survives our DLL reload (our static does not) — reuse it.
    private static GameObject? FindExisting() {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name == Name && go.scene.IsValid())
                return go;
        return null;
    }

    internal static object Ensure() {
        try {
            ui ??= FindExisting();
            if (ui != null) {
                RebindInstance(ui);
                return new { ok = true, note = "reused", instanceSet = InstanceSet() };
            }

            SilksongCatalog.EnsureMounted();
            var prefab = Addressables.LoadAssetAsync<GameObject>("_UIManager").WaitForCompletion();
            if (prefab == null) return new { error = "_UIManager load returned null" };

            // Instantiate under an INACTIVE holder so UIManager.Awake/Start don't run; we only need instance + fields.
            var holder = new GameObject("hp_ui_holder");
            holder.SetActive(false);
            var inst = Object.Instantiate(prefab, holder.transform);
            inst.name = Name;
            RebindInstance(inst);
            Object.DontDestroyOnLoad(holder);
            ui = inst;
            return new { ok = true, instanceSet = InstanceSet() };
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            Log.Error($"[UIManager] bootstrap failed: {ex}");
            return new { error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    private static bool InstanceSet() {
        return typeof(Silksong::UIManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) != null;
    }

    private static void RebindInstance(GameObject inst) {
        var uim = inst.GetComponentInChildren<Silksong::UIManager>(true);
        if (uim != null)
            typeof(Silksong::UIManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, uim);
    }

    internal static void Cleanup() {
        if (ui != null) {
            Object.DestroyImmediate(ui.transform.root.gameObject);
            ui = null;
        }

        typeof(Silksong::UIManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
    }
}
