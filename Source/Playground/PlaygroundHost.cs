using System;
using HornetPlayer.DevServer;
using UnityEngine;

namespace HornetPlayer.Playground;

// MonoBehaviour that lives on a DontDestroyOnLoad GameObject. It pumps the debug server's request queue each frame on
// the Unity main thread and is the coroutine host for async (multi-frame) route handlers.
public class PlaygroundHost : MonoBehaviour {
    private void Update() {
        try {
            DebugServer.Update();
        } catch (Exception e) {
            Log.Error(e);
        }
    }
}
