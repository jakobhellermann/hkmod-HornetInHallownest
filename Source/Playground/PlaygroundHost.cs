using System;
using HornetPlayer.DevServer;
using UnityEngine;

namespace HornetPlayer.Playground;

// MonoBehaviour that lives on a DontDestroyOnLoad GameObject. It pumps the debug server's request queue each frame on
// the Unity main thread and is the coroutine host for async (multi-frame) route handlers. OnTick is a generic
// per-frame hook (the mod points it at ModuleHost.Tick) so the host stays decoupled from the module system.
public class PlaygroundHost : MonoBehaviour {
    public Action? OnTick;

    private void Update() {
        try {
            DebugServer.Update();
        } catch (Exception e) {
            Log.Error(e);
        }

        try {
            OnTick?.Invoke();
        } catch (Exception e) {
            Log.Error(e);
        }
    }
}
