using System;
using System.IO;
using UnityEngine;

namespace HornetPlayer.Playground;

// Runtime path resolution for the mod's external files:
//   - ModDir / ModFile: where HornetPlayer.dll and the data files shipped next to it (the remapped monoscripts bundle) live.
//   - SilksongAa: Silksong's Addressables folder, auto-detected next to the HK install (same Steam library).
// Both throw with an actionable message rather than returning a silently-wrong path; callers turn that into a clear
// "mod won't work" log without taking down the rest of the game. (An in-game config path may replace the detection later.)
internal static class Paths {
    private static string? _modDir;

    // Directory containing HornetPlayer.dll (data files like the remapped monoscripts bundle ship next to it).
    // Set once from HornetPlayerMod.Initialize via the Modding API's Mod.ModDirectory — which the loader fills from the
    // actual load path, so it's correct even on a hot-reload (there Assembly.Location is empty because the assembly is
    // loaded from an in-memory byte[], and Path.GetDirectoryName("") would throw "Invalid path").
    internal static string ModDir {
        get => _modDir
               ?? throw new InvalidOperationException(
                   "Paths.ModDir not initialized — set it from HornetPlayerMod.Initialize (Mod.ModDirectory)");
        set => _modDir = value.Replace('\\', '/');
    }

    // A data file distributed next to the DLL (kept a loose file so it can be regenerated/swapped without rebuilding).
    internal static string ModFile(string name) {
        var p = $"{ModDir}/{name}";
        if (!File.Exists(p))
            throw new FileNotFoundException($"mod data file missing: {p} (must ship next to HornetPlayer.dll)", p);
        return p;
    }

    // The game's Managed/ root, where engine assemblies and our B2 prefixed DLLs (Silksong.*) are deployed. Derived from
    // a known engine assembly's location rather than a fixed path so it follows the install.
    internal static string ManagedDir =>
        Path.GetDirectoryName(typeof(GameObject).Assembly.Location)?.Replace('\\', '/')
        ?? throw new InvalidOperationException("could not resolve the Managed directory");

    // Silksong's per-platform Addressables build-target folder under aa/ (StandaloneOSX / StandaloneWindows64 /
    // StandaloneLinux64), matching the OS we're running on.
    private static string SilksongAaTarget =>
        Application.platform switch {
            RuntimePlatform.OSXPlayer => "StandaloneOSX",
            RuntimePlatform.WindowsPlayer => "StandaloneWindows64",
            _ => "StandaloneLinux64",
        };

    // A bundle inside Silksong's aa/<platform target>/ folder (e.g. globalsettings_assets_all.bundle).
    internal static string SilksongAaBundle(string name) => $"{SilksongAa()}/{SilksongAaTarget}/{name}";

    private const string SilksongDirName = "Hollow Knight Silksong";
    private static string? _silksongAa;

    // Silksong's StreamingAssets/aa folder, detected relative to HK's install: the two games are sibling directories in
    // the same Steam library, so we walk up from HK's data folder to the library root and over to Silksong's. Throws
    // (cached miss not stored) with a clear message if there's no Silksong install there.
    internal static string SilksongAa() {
        if (_silksongAa != null) return _silksongAa;
        var aa = DetectSilksongAa();
        if (!Directory.Exists(aa))
            throw new DirectoryNotFoundException(
                $"Silksong Addressables folder not found at '{aa}'. Expected a Silksong install next to Hollow Knight " +
                "in the same Steam library.");
        return _silksongAa = aa;
    }

    private static string DetectSilksongAa() {
        // The two games are sibling folders in the same Steam library ("…/common/Hollow Knight" and
        // "…/common/Hollow Knight Silksong"). Split HK's data path at its own game folder to get the library,
        // and mirror HK's layout for Silksong (.app bundle on macOS, "<name>_Data" elsewhere).
        var hkData = Application.dataPath.Replace('\\', '/');
        const string hkDir = "/Hollow Knight/";
        var i = hkData.IndexOf(hkDir, StringComparison.Ordinal);
        if (i < 0)
            throw new DirectoryNotFoundException(
                $"could not locate the Steam library from HK data path '{hkData}'");
        var library = hkData.Substring(0, i);
        var isMac = Application.platform == RuntimePlatform.OSXPlayer;
        var ssData = isMac
            ? $"{SilksongDirName}.app/Contents/Resources/Data"
            : $"{SilksongDirName}_Data";
        return $"{library}/{SilksongDirName}/{ssData}/StreamingAssets/aa";
    }
}
