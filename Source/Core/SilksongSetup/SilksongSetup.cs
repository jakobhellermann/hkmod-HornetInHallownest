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
    // Unmodified assemblies HK lacks: copy as is
    private static readonly string[] deps = [
        "Unity.Addressables",
        "Unity.ResourceManager",
        "Unity.Profiling.Core",
        "Unity.Mathematics",
        "Unity.Burst",
        "Unity.Burst.Unsafe",               // Unity.Burst has a hard reference to it
        "Newtonsoft.Json.UnityConverters",
        "TeamCherry.Splines",
        "Coffee.SoftMaskForUGUI",
        // Reachable from the prefixed Silksong assemblies. Computed as a transitive closure over the assembly
        // scopes their type references actually name, NOT guessed: everything reachable that the host does not
        // already ship has to be on the probe path, or the first type that touches it fails to load.
        "TeamCherry.SharedUtils",           // TeamCherry.Splines -> (unprefixed) SharedUtils
        "TeamCherry.Cinematics",
        "TeamCherry.BuildBot",
        "Unity.InputSystem",
        "SharpDX",
        "SharpDX.DirectInput",
        "com.rlabrecque.steamworks.net",
        // Only present on hosts older than Silksong's Unity (see tools/setup-hk178.sh).
        "HornetInHallownest.UnityCompat"    // Unity 6 + netstandard2.1 API surface 2020.2 lacks
    ];

    private const string Prefix = "Silksong";

    // Silksong assemblies containing the types conflicting with HK: Copied as prefixed Silksong.*
    private static readonly string[] prefixInputs = [
        "Assembly-CSharp",
        "Assembly-CSharp-firstpass",
        "PlayMaker",
        "ConditionalExpression",
        "TeamCherry.NestedFadeGroup",
        "TeamCherry.Localization",
        "TeamCherry.SharedUtils"
    ];

    private static readonly string[] prefixed = [..prefixInputs.Select(n => AssemblyPrefixer.PrefixedName(Prefix, n))];

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
        Log.Info($"[SilksongSetup] installed {deps.Length} deps + {prefixed.Length} prefixed Silksong assemblies into Managed");
        return true;
    }

    // Keeping track of files installed in the `Managed` dir.
    private sealed class InstalledFiles(string managed) {
        private const string FingerprintFile = "Silksong.support.fingerprint";
        private static readonly string[] files = [..deps.Concat(prefixed).Select(n => $"{n}.dll")];

        public bool IsCurrent(string fingerprint) =>
            ReadOrNull($"{managed}/{FingerprintFile}") == fingerprint && files.All(f => File.Exists($"{managed}/{f}"));

        public void Reinstall(string fingerprint) {
            Clean();
            // Entries absent from the Silksong install (e.g. HornetInHallownest.UnityCompat, only added on hosts
            // older than Silksong's Unity) are skipped rather than failing the install.
            foreach (var name in deps) {
                var src = $"{Paths.SilksongManagedDir}/{name}.dll";
                if (File.Exists(src)) File.Copy(src, $"{managed}/{name}.dll");
            }
            AssemblyPrefixer.Prefix(Prefix, Paths.SilksongManagedDir,
                prefixInputs.Select(n => $"{Paths.SilksongManagedDir}/{n}.dll"), managed);
            File.WriteAllText($"{managed}/{FingerprintFile}", fingerprint);
        }

        private void Clean() {
            foreach (var name in deps) Remove($"{managed}/{name}.dll");
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
        return File.ReadAllText(bootConfig);
    }

    // Register prefixed assemblies for `[Serializable]` type resolution.
    private static bool RegisterForSerialization(string path) {
        var original = File.ReadAllText(path);
        var json = JObject.Parse(original);
        var names = (JArray)json["names"]!;
        var types = (JArray)json["types"]!; // parallel to names

        var hadOurs = names.Any(IsOurs);
        for (var i = names.Count - 1; i >= 0; i--)
            if (IsOurs(names[i])) { names.RemoveAt(i); types.RemoveAt(i); }
        foreach (var name in prefixed) {
            names.Add($"{name}.dll");
            types.Add(16); // MonoBehaviour/serialized-type flag
        }

        var updated = json.ToString();
        if (updated == original) return false;

        // Snapshot the vanilla file the first time we touch it.
        if (!hadOurs) File.Copy(path, $"{path}.hornetbak", overwrite: true);
        File.WriteAllText(path, updated);
        Log.Info($"[SilksongSetup] registered {prefixed.Length} assemblies in ScriptingAssemblies.json");
        return true;

        static bool IsOurs(JToken name) => ((string?)name)?.StartsWith($"{Prefix}.") == true;
    }
}
