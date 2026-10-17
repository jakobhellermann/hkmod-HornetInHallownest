using System;
using System.IO;
using System.Linq;
using HornetInHallownest.Util;
using Newtonsoft.Json.Linq;

namespace HornetInHallownest.Core.SilksongSetup;

// Ensure that the game is ready for loading silksong addressables:
// - Prefixed `Silksong.*.dll`s need to be installed in the `Managed/` folder
// - (some of them) they need to be present in `ScriptingAssemblies.json`
// - Other required silksong DLLs need to be copied to `Managed/
// The setup will be on first install performed whenever the version behind the silksong path changes.
internal static class SilksongSetup {
#if HK1578
    private static string[] Deps => [
        ..SilksongAssemblies.Deps, ..SilksongAssemblies.Hk1578Deps,
        ..SilksongAssemblies.PresentIn(Paths.SilksongManagedDir, SilksongAssemblies.Hk1578PlatformDeps),
    ];
    private static string[] Installed => [..Deps, ..SilksongAssemblies.Prefixed, ..SilksongAssemblies.ShimFiles];
    // Everything a previous install may have left, whichever Silksong build it came from.
    private static string[] Removable => [..Installed, ..SilksongAssemblies.Hk1578PlatformDeps];
    private static string? ShimPath => Paths.ModFile($"managed/{SilksongAssemblies.Shim}.dll");
#else
    private static string[] Deps => SilksongAssemblies.Deps;
    private static string[] Installed => [..Deps, ..SilksongAssemblies.Prefixed];
    private static string[] Removable => Installed;
    private static string? ShimPath => null;
#endif

    // True if already installed, false if actions were taken and a restart is needed.
    internal static bool EnsureInstalled() {
        var changed = EnsureInstalledAssemblies(new InstalledFiles(Paths.HkManagedDir));
        changed |= RegisterForSerialization($"{Paths.HkDataDir}/ScriptingAssemblies.json");
        return !changed;
    }

    private static bool EnsureInstalledAssemblies(InstalledFiles install) {
        var fingerprint = FingerprintGameVersion();
        install.RemoveStale();
        if (install.IsCurrent(fingerprint)) return false;

        install.Reinstall(fingerprint);
        Log.Info($"[SilksongSetup] installed {Installed.Length} Silksong assemblies into Managed");
        return true;
    }

    // Keeping track of files installed in the `Managed` dir.
    private sealed class InstalledFiles(string managed) {
        private const string FingerprintFile = "Silksong.support.fingerprint";

        public bool IsCurrent(string fingerprint) =>
            ReadOrNull($"{managed}/{FingerprintFile}") == fingerprint && Installed.All(n => File.Exists($"{managed}/{n}.dll"));

        public void Reinstall(string fingerprint) {
            Clean();
            SilksongAssemblyBuilder.Build(Paths.SilksongManagedDir, managed, Deps, ShimPath, managed, Log.Debug);
            File.WriteAllText($"{managed}/{FingerprintFile}", fingerprint);
        }

        private void Clean() {
            foreach (var name in Removable) Remove($"{managed}/{name}.dll");
            foreach (var path in Directory.GetFiles(managed, "Silksong.*.dll")) Remove(path);
        }

        private void Remove(string path) {
            if (!File.Exists(path)) return;
            try {
                File.Delete(path);
            } catch (UnauthorizedAccessException) {
                // Windows doesn't let you delete a loaded DLL. Renaming works.
                var stale = $"{path}.{Guid.NewGuid():N}.stale";
                Log.Debug($"[SilksongSetup] Can't delete '{path}' since it is loaded, moving to '{stale}'");
                File.Move(path, stale);
            }
        }

        public void RemoveStale() {
            foreach (var stale in Directory.GetFiles(managed, "*.stale")) {
                try { File.Delete(stale); }
                catch (UnauthorizedAccessException) { Log.Debug($"[SilksongSetup] Failed to delete {Path.GetFileName(stale)}"); }
            }
        }

        private static string? ReadOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string FingerprintGameVersion() {
        var bootConfig = $"{Directory.GetParent(Paths.SilksongManagedDir)!.FullName}/boot.config";
        var fingerprint = $"{File.ReadAllText(bootConfig)}\nmod {typeof(SilksongSetup).Assembly.GetName().Version}";
#if HK1578
        fingerprint += $"\nshim {ShimHash()}";
#endif
        return fingerprint;
    }

#if HK1578
    // A changed shim needs a reinstall even when the mod version stays the same.
    private static string ShimHash() {
        using var sha = System.Security.Cryptography.SHA256.Create();
        foreach (var name in SilksongAssemblies.ShimFiles) {
            var bytes = File.ReadAllBytes(Paths.ModFile($"managed/{name}.dll"));
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return BitConverter.ToString(sha.Hash).Replace("-", "");
    }
#endif

    // Register prefixed assemblies for `[Serializable]` type resolution.
    private static bool RegisterForSerialization(string path) {
        var original = File.ReadAllText(path);
        var json = JObject.Parse(original);
        var names = (JArray)json["names"]!;
        var types = (JArray)json["types"]!; // parallel to names

        var hadOurs = names.Any(IsOurs);
        for (var i = names.Count - 1; i >= 0; i--)
            if (IsOurs(names[i])) { names.RemoveAt(i); types.RemoveAt(i); }
        foreach (var name in SilksongAssemblies.Prefixed) {
            names.Add($"{name}.dll");
            types.Add(16); // MonoBehaviour/serialized-type flag
        }

        var updated = json.ToString();
        if (updated == original) return false;

        // Snapshot the vanilla file the first time we touch it.
        if (!hadOurs) File.Copy(path, $"{path}.hornetbak", overwrite: true);
        File.WriteAllText(path, updated);
        Log.Info($"[SilksongSetup] registered {SilksongAssemblies.Prefixed.Length} assemblies in ScriptingAssemblies.json");
        return true;

        static bool IsOurs(JToken name) => ((string?)name)?.StartsWith($"{SilksongAssemblies.Prefix}.") == true;
    }
}
