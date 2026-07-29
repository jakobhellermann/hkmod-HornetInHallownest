using System.IO;
using System.Linq;
using HornetInHallownest.Util;
using Newtonsoft.Json.Linq;

namespace HornetInHallownest.Core;

// Puts the game install into the state the mod needs and that only takes effect at Unity startup: the Silksong support
// assemblies on Mono's default probe path (Managed root), and our prefixed assemblies registered for Unity's
// serialization type resolution (ScriptingAssemblies.json). Neither can be done from a running mod (the probe path and
// that file are read at engine init), so this only prepares them; a changed state means a restart is required. Runs
// every load, is idempotent, and self-heals after a Steam file-verify wipes the changes.
internal static class SilksongSetup {
    // Unmodified Unity-package assemblies HK lacks. They must sit on Mono's probe path so metadata type-ref tokens
    // (e.g. a game type's field typed from Unity.Addressables) resolve; an AssemblyResolve handler can't satisfy those.
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

    // Our IL-prefixed assemblies. Must be in Managed root and registered (type flag 16) so Unity resolves their nested
    // [Serializable]/MonoBehaviour types when deserializing Silksong assets (else those fields come out null).
    private static readonly string[] prefixed = [
        "Silksong.AssemblyCSharp",
        "Silksong.AssemblyCSharpfirstpass",
        "Silksong.PlayMaker",
        "Silksong.TeamCherryNestedFadeGroup",
        "Silksong.TeamCherryLocalization",
        "Silksong.TeamCherrySharedUtils",
        "Silksong.ConditionalExpression"
    ];

    // True if already installed, false if actions were taken and a restart is needed.
    internal static bool EnsureInstalled() {
        var managed = Paths.HkManagedDir;
        var changed = false;

        foreach (var name in deps)
            changed |= CopyIfMissing($"{Paths.SilksongManagedDir}/{name}.dll", $"{managed}/{name}.dll");

        // Shipped as {name}.dll.managed so the mod loader ignores them in Mods/; installed as plain .dll into Managed.
        foreach (var name in prefixed)
            changed |= CopyIfMissing($"{Paths.ModDir}/{name}.dll.managed", $"{managed}/{name}.dll");

        changed |= RegisterForSerialization($"{Paths.HkDataDir}/ScriptingAssemblies.json");
        return !changed;
    }

    private static bool CopyIfMissing(string src, string dst) {
        if (File.Exists(dst) || !File.Exists(src)) return false;
        File.Copy(src, dst);
        Log.Info($"[SilksongSetup] installed {Path.GetFileName(dst)} into Managed");
        return true;
    }

    private static bool RegisterForSerialization(string path) {
        if (!File.Exists(path)) return false;
        var json = JObject.Parse(File.ReadAllText(path));
        if (json["names"] is not JArray names || json["types"] is not JArray types) return false;

        var missing = prefixed.Where(name => names.All(n => (string?)n != $"{name}.dll")).ToList();
        if (missing.Count == 0) return false;

        // Snapshot the restore point while the file is still pristine (none of our entries in it yet).
        var backup = $"{path}.hornetbak";
        if (missing.Count == prefixed.Length) File.Copy(path, backup, overwrite: true);

        foreach (var name in missing) {
            names.Add($"{name}.dll");
            types.Add(16); // MonoBehaviour/serialized-type flag
        }

        File.WriteAllText(path, json.ToString());
        Log.Info($"[SilksongSetup] registered {missing.Count} assemblies in ScriptingAssemblies.json");
        return true;
    }
}
