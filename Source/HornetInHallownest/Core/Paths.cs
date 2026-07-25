using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HornetPlayer.HornetInHallownest.Core;

internal static class Paths {
    private const string SilksongName = "Hollow Knight Silksong";

    // Directory holding HornetPlayer.dll and the data files shipped beside it.
    internal static readonly string ModDir = ResolveModDir();

    private static string ResolveModDir() {
        // Under a hot-reload the assembly is loaded from an in-memory byte[] so Location is empty; fall back then.
        var location = Assembly.GetExecutingAssembly().Location;
        return string.IsNullOrEmpty(location)
            ? $"{Application.dataPath}/Managed/Mods/HornetPlayer"
            : Path.GetDirectoryName(location)!.Replace('\\', '/');
    }

    internal static string HkManagedDir => $"{Application.dataPath}/Managed";

    // Config override for the Silksong install, can point to data or installation folder
    internal static string? SilksongInstallOverride {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    internal static string SilksongAddressables => $"{SilksongDataDir}/StreamingAssets/aa";

    internal static string SilksongAddressablesBundle(string name) => $"{SilksongAddressables}/{AaTarget}/{name}";

    internal static string SilksongManagedDir => $"{SilksongDataDir}/Managed";


    internal static string ModFile(string name) {
        var path = $"{ModDir}/{name}";
        if (!File.Exists(path))
            throw new FileNotFoundException($"HornetPlayer is missing a required file ({name}).", path);
        return path;
    }

    // Silksong's data folder, resolved from the config override or auto-detected next to HK.
    private static string SilksongDataDir => field ??= ResolveSilksongDataDir();

    private static string ResolveSilksongDataDir() {
        var install = (SilksongInstallOverride ?? AutoDetectedInstall).Replace('\\', '/');
        var dataDir = install.EndsWith(DataFolder) ? install : $"{install}/{DataFolder}";
        if (Directory.Exists(dataDir)) return dataDir;

        throw new DirectoryNotFoundException(SilksongInstallOverride != null
            ? "Couldn't find Silksong at the configured SilksongPath. Point it at your Hollow Knight Silksong folder."
            : "Couldn't find Hollow Knight Silksong. Install it next to Hollow Knight, or set SilksongPath in "
              + "HornetPlayerMod.GlobalSettings.json to your Silksong install.");
    }

    // Steam installs the two games as siblings in the same library: ".../common/Hollow Knight" and its sibling here.
    private static string AutoDetectedInstall {
        get {
            var library = Directory.GetParent(Application.dataPath)?.Parent?.FullName;
            return $"{library}/{SilksongName}";
        }
    }

    private static string DataFolder => Application.platform == RuntimePlatform.OSXPlayer
        ? $"{SilksongName}.app/Contents/Resources/Data"
        : $"{SilksongName}_Data";

    private static string AaTarget => Application.platform switch {
        RuntimePlatform.WindowsPlayer => "StandaloneWindows64",
        RuntimePlatform.OSXPlayer => "StandaloneOSX",
        RuntimePlatform.LinuxPlayer => "StandaloneLinux64",
        var p => throw new PlatformNotSupportedException($"unsupported platform: {p}")
    };
}
