extern alias Silksong;
using HornetPlayer.HornetInHallownest.Modules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// B - toggle infinite silk
// 8 - toggle collider height
internal static class DebugKeybinds {
    private static GameObject? go;

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.DebugKeybinds");
        go.AddComponent<DebugKeybindsBehaviour>();
        Object.DontDestroyOnLoad(go);
    }

    internal static void Cleanup() {
        if (go != null) Object.Destroy(go);
        go = null;
    }
}

internal sealed class DebugKeybindsBehaviour : MonoBehaviour {
    private bool infiniteSilk;

    private void Update() {
        if (Input.GetKeyDown(KeyCode.B)) {
            infiniteSilk = !infiniteSilk;
            Log.Debug($"[DebugKeybinds] infinite silk: {infiniteSilk}");
        }

        if (Input.GetKeyDown(KeyCode.Alpha8)) {
            var knightHeight = HornetSpawner.ToggleColliderHeight();
            Log.Debug($"[DebugKeybinds] collider height: {(knightHeight ? "Knight (1.28)" : "Hornet full (2.08)")}");
        }

        if (infiniteSilk) {
            var spd = Silksong::PlayerData.instance;
            if (spd != null && spd.silk < spd.silkMax) spd.silk = spd.silkMax;
        }
    }
}
