extern alias Silksong;
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;
using HornetInHallownest.Core;
using HornetInHallownest.Util;

namespace HornetInHallownest.Bootstrap;

// Bring up Silksong's UIManager singleton so the HUD stops NullRef-ing on UIManager.instance. Load the `_UIManager`
// addressable prefab and set the private static _instance. Kept dormant (inactive holder) — serialized fields are live
// without running Awake/Start's heavier menu/canvas setup. Mirrors GameCamerasBootstrap.
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

    internal static void Ensure() {
        try {
            ui ??= FindExisting();
            if (ui != null) {
                RebindInstance(ui);
                return;
            }

            SilksongAddressables.EnsureMounted();
            var prefab = Addressables.LoadAssetAsync<GameObject>("_UIManager").WaitForCompletion();
            if (prefab == null) {
                Log.Error("[UIManager] _UIManager load returned null");
                return;
            }

            // Instantiate under an inactive holder so UIManager.Awake/Start don't run; we only need instance + fields.
            var holder = new GameObject("hp_ui_holder");
            holder.SetActive(false);
            var inst = Object.Instantiate(prefab, holder.transform);
            inst.name = Name;
            RebindInstance(inst);
            Object.DontDestroyOnLoad(holder);
            ui = inst;
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            Log.Error($"[UIManager] bootstrap failed: {ex}");
        }
    }

    private static void RebindInstance(GameObject inst) {
        var uim = inst.GetComponentInChildren<Silksong::UIManager>(true);
        if (uim != null) typeof(Silksong::UIManager).SetFieldValue("_instance", uim);
    }

    internal static void Cleanup() {
        if (ui != null) {
            Object.DestroyImmediate(ui.transform.root.gameObject);
            ui = null;
        }

        typeof(Silksong::UIManager).SetFieldValue("_instance", null);
    }
}
