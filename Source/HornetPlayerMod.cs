using System.Reflection;
using HornetPlayer.Playground;
using Modding;
using UnityEngine;

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
        DebugServer.MapPost("/play-clip", req => BundleSpike.PlayClip(req["name"]));
        DebugServer.MapPost("/spawn-real", _ => BundleSpike.SpawnReal());
        DebugServer.MapPost("/despawn-real", _ => BundleSpike.DespawnReal());
        DebugServer.MapPost("/diagnose-awake", _ => BundleSpike.DiagnoseAwake());
        DebugServer.MapPost("/reload-all-deps", req => BundleSpike.ReloadWithAllDeps(req["list"] ?? "/tmp/deps.txt"));
        DebugServer.Start(host, DebugServerPort);

        SilksongLoadSpike.Run();
        BundleSpike.Run();
    }

    public void Unload() {
        SilksongLoadSpike.Cleanup();
        BundleSpike.Cleanup();
        SilksongBootstrap.Cleanup();
        DebugServer.Stop();
        if (playgroundHost != null) Object.Destroy(playgroundHost);
        playgroundHost = null;
        LoadedInstance = null;
    }
}
