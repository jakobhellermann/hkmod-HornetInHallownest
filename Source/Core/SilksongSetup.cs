using System.IO;
using System.Linq;
using HornetInHallownest.Util;
using Newtonsoft.Json.Linq;

namespace HornetInHallownest.Core;

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
        "Newtonsoft.Json.UnityConverters",
        "TeamCherry.Splines",
        "Coffee.SoftMaskForUGUI"
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
            foreach (var name in deps)
                File.Copy($"{Paths.SilksongManagedDir}/{name}.dll", $"{managed}/{name}.dll");
            AssemblyPrefixer.Prefix(Prefix, Paths.SilksongManagedDir,
                prefixInputs.Select(n => $"{Paths.SilksongManagedDir}/{n}.dll"), managed);
            File.WriteAllText($"{managed}/{FingerprintFile}", fingerprint);
        }

        private void Clean() {
            foreach (var name in deps) File.Delete($"{managed}/{name}.dll");
            foreach (var path in Directory.GetFiles(managed, "Silksong.*.dll")) File.Delete(path);
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
