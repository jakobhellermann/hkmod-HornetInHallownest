using System.Reflection;
using HornetPlayer.Playground;
using Modding;
using UnityEngine;

using HornetPlayer.DevServer;
namespace HornetPlayer;

public class HornetPlayerMod : Mod, ITogglableMod {
    // Distinct from Silksong's DevUtils server (8200) so both games can be debugged at once.
    private const int DebugServerPort = 8201;

    public static HornetPlayerMod? LoadedInstance { get; private set; }

    private GameObject? playgroundHost;

    public override string GetVersion() {
        return Assembly.GetExecutingAssembly().GetName().Version.ToString();
    }

    public override void Initialize() {
        if (LoadedInstance != null) return;
        LoadedInstance = this;

        Playground.Log.Sink = msg => Log(msg);

        playgroundHost = new GameObject("HornetPlayer.Playground");
        Object.DontDestroyOnLoad(playgroundHost);
        var host = playgroundHost.AddComponent<PlaygroundHost>();

        PlaygroundRoutes.Register();
        DebugServer.MapPost("/respawn-hornet", _ => {
            BundleSpike.Cleanup();
            BundleSpike.Run();
            return new { ok = true };
        });
        DebugServer.MapPost("/spawn-real", _ => BundleSpike.SpawnReal());
        DebugServer.MapPost("/despawn-real", _ => BundleSpike.DespawnReal());
        DebugServer.MapPost("/diagnose-awake", _ => BundleSpike.DiagnoseAwake());
        DebugServer.MapPost("/reload-all-deps", req => BundleSpike.ReloadWithAllDeps(req["list"] ?? "/tmp/deps.txt"));
        DebugServer.MapPost("/scan-serializable", _ => BundleSpike.ScanSerializable());
        DebugServer.MapGet("/hero-state", _ => BundleSpike.HeroState());
        DebugServer.MapGet("/diag-input", _ => BundleSpike.DiagInput());
        DebugServer.MapGet("/fsm-state", _ => BundleSpike.FsmState());
        DebugServer.MapPost("/load-gamecameras", _ => BundleSpike.LoadGameCameras());
        DebugServer.MapGet("/fsm-dump", req => BundleSpike.FsmDump(req["name"] ?? "Sprint"));
        DebugServer.MapPost("/load-save", req => {
            var slot = int.TryParse(req["slot"], out var s) ? s : 0;
            GameManager.instance.LoadGameFromUI(slot); // HK's GameManager: full UI load (transition + scene)
            return new { ok = true, slot };
        });
        DebugServer.MapPost("/press", req => {
            var a = (req["a"] ?? "right").ToLowerInvariant(); // left/right/up/down/jump/attack/dash
            if (!int.TryParse(req["frames"], out var f) || f <= 0) f = 60;
            InputBridge.Press(a, f); // debug-drive an InControl action for f frames (no physical key needed)
            return new { action = a, frames = f };
        });
        DebugServer.Start(host, DebugServerPort);

        SilksongLoadSpike.Run();   // touches Silksong types -> assembly is now in the AppDomain
        PlayMakerFix.Apply();
        Stub.Install();
        InputBridge.Install();
        HornetEnvironmentAdapter.Install();
        BundleSpike.Run();
    }

    public void Unload() {
        SilksongLoadSpike.Cleanup();
        BundleSpike.Cleanup();
        SilksongBootstrap.Cleanup();
        GlobalSettingsBootstrap.Cleanup();
        PlayMakerFix.Cleanup();
        Stub.Cleanup();
        InputBridge.Cleanup();
        HornetEnvironmentAdapter.Cleanup();
        DebugServer.Stop();
        if (playgroundHost != null) Object.Destroy(playgroundHost);
        playgroundHost = null;
        LoadedInstance = null;
    }
}
