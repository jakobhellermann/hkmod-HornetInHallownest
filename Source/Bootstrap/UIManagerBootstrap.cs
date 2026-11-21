extern alias Silksong;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;
using HornetInHallownest.Core;

namespace HornetInHallownest.Bootstrap;

// The HUD reads UIManager.instance; its menus stay inactive, so their Awake/Start never run.
internal static class UIManagerBootstrap {
    private const string Name = "Silksong_UIManager";
    private static GameObject? ui;

    // The DontDestroyOnLoad holder survives a hot-reload, our static doesn't.
    private static GameObject? FindExisting() {
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name == Name && go.scene.IsValid())
                return go;
        return null;
    }

    internal static void Ensure() {
        ui ??= FindExisting();
        if (ui != null) {
            RebindInstance(ui);
            return;
        }

        SilksongAddressables.EnsureMounted();
        var prefab = Addressables.LoadAssetAsync<GameObject>("_UIManager").WaitForCompletion();

        var holder = new GameObject("hp_ui_holder");
        holder.SetActive(false);
        var inst = Object.Instantiate(prefab, holder.transform);
        inst.name = Name;
        RebindInstance(inst);
        Object.DontDestroyOnLoad(holder);
        ui = inst;
    }

    private static void RebindInstance(GameObject inst) {
        Silksong::UIManager._instance = inst.GetComponentInChildren<Silksong::UIManager>(true);
    }

    internal static void Cleanup() {
        if (ui != null) {
            Object.DestroyImmediate(ui.transform.root.gameObject);
            ui = null;
        }

        Silksong::UIManager._instance = null;
    }
}
