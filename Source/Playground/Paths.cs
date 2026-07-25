using System;
using System.IO;
using UnityEngine;

namespace HornetPlayer.Playground;

internal static class Paths {
    private const string SilksongName = "Hollow Knight Silksong";

    // Directory holding HornetPlayer.dll and the data files shipped beside it.
    private static readonly string modDir = HornetPlayerMod.LoadedInstance?.ModDirectory
                                            ?? throw new InvalidOperationException("ModDirectory not set");

    // HK's Managed folder: engine assemblies plus our prefixed Silksong.* DLLs.
    internal static string HkManagedDir => $"{Application.dataPath}/Managed";

    // Config override for the Silksong install; may point at the install folder or its "<name>_Data" folder.
    // Null or blank auto-detects Silksong next to Hollow Knight.
    internal static string? SilksongInstall { get; set; }

    // Silksong's Addressables folder: its data directory plus the fixed StreamingAssets/aa suffix.
    internal static string SilksongAa => $"{SilksongDataDir}/StreamingAssets/aa";

    internal static string ModFile(string name) {
        var path = $"{modDir}/{name}";
        if (!File.Exists(path))
            throw new FileNotFoundException($"HornetPlayer is missing a required file ({name}). Reinstall the mod.", path);
        return path;
    }

    // A bundle inside Silksong's aa/<platform target>/ folder (e.g. globalsettings_assets_all.bundle).
    internal static string SilksongAaBundle(string name) => $"{SilksongAa}/{AaTarget}/{name}";

    // Silksong's data folder (holds StreamingAssets), resolved once from the config override or auto-detected next to HK.
    private static string SilksongDataDir => field ??= ResolveSilksongDataDir();

    private static string ResolveSilksongDataDir() {
        var install = (ConfiguredInstall ?? AutoDetectedInstall).Replace('\\', '/');
        // The config override may point at the install folder or the data folder directly; accept both.
        var withData = $"{install}/{DataFolder}";
        if (Directory.Exists($"{withData}/StreamingAssets")) return withData;
        if (Directory.Exists($"{install}/StreamingAssets")) return install;

        throw new DirectoryNotFoundException(ConfiguredInstall != null
            ? "Couldn't find Silksong at the configured SilksongPath. Point it at your Hollow Knight Silksong folder."
            : "Couldn't find Hollow Knight Silksong. Install it next to Hollow Knight, or set SilksongPath in "
              + "HornetPlayerMod.GlobalSettings.json to your Silksong folder.");
    }

    private static string? ConfiguredInstall => string.IsNullOrWhiteSpace(SilksongInstall) ? null : SilksongInstall;

    // Steam installs the two games as siblings in the same library: ".../common/Hollow Knight" and its sibling here.
    private static string AutoDetectedInstall {
        get {
            var library = Directory.GetParent(Application.dataPath)?.Parent?.FullName;
            return $"{library}/{SilksongName}";
        }
    }

    // Silksong's data folder: an ".app" bundle on macOS, "<name>_Data" elsewhere.
    private static string DataFolder => Application.platform == RuntimePlatform.OSXPlayer
        ? $"{SilksongName}.app/Contents/Resources/Data"
        : $"{SilksongName}_Data";

    private static string AaTarget => Application.platform switch {
        RuntimePlatform.OSXPlayer => "StandaloneOSX",
        RuntimePlatform.WindowsPlayer => "StandaloneWindows64",
        _ => "StandaloneLinux64"
    };
}
