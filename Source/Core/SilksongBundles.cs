using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HornetInHallownest.Core.SilksongSetup;
using HornetInHallownest.Util;
using UnitySceneRepacker;
using UnityEngine;

namespace HornetInHallownest.Core;

// Bundles of Silksong files whose scripts bind to the prefixed `Silksong.*` assemblies, built from the Silksong install.
internal static class SilksongBundles {
    private static string CacheDir => $"{Paths.ModDir}/cache";
    private static string FingerprintFile => $"{CacheDir}/fingerprint";

    // Replaces Silksong's `*_monoscripts.bundle`, which every addressables bundle takes its MonoScripts from.
    internal static string MonoScripts => $"{CacheDir}/monoscripts.silksong.bundle";

    // Silksong's `resources.assets`, by `Resources.Load` path.
    internal static string Resources => $"{CacheDir}/silksong-resources.bundle";

    internal static void EnsureBuilt(string setupFingerprint) {
        var fingerprint = $"{setupFingerprint}\nrepacker {GameRepacker.FormatVersion}";
        if (ReadOrNull(FingerprintFile) == fingerprint) return;

        var time = Stopwatch.StartNew();
        Directory.CreateDirectory(CacheDir);
        using var silksong = new GameRepacker(Directory.GetParent(Paths.SilksongManagedDir)!.FullName);
        var options = new BuildBundleOptions {
            RewriteRules = RewriteRules(),
            TargetVersion = Application.unityVersion,
            Compression = Compression.None,
        };
        File.WriteAllBytes(MonoScripts, silksong.BuildAddressablesBundle(MonoScriptsSource(), options));

        options.ListResources = true;
        options.InlineExternal = ["MonoScript"];
        File.WriteAllBytes(Resources, silksong.BuildFileBundle("resources.assets", options));

        File.WriteAllText(FingerprintFile, fingerprint);
        Log.Info($"[SilksongBundles] built the Silksong bundles in {time.ElapsedMilliseconds} ms");
    }

    private static List<RewriteRule> RewriteRules() {
        var rules = SilksongAssemblies.PrefixInputs
            .Select(name => RewriteRule.RemapAssembly(name, AssemblyPrefixer.PrefixedName(SilksongAssemblies.Prefix, name)))
            .ToList();
#if HK1578
        // HK 1.5.78 compiles tk2d into its Assembly-CSharp
        rules.Add(RewriteRule.RemapAssembly("TeamCherry.TK2D", "Assembly-CSharp"));
#endif
        return rules;
    }

    private static string MonoScriptsSource() {
        var aaBuild = Path.GetDirectoryName(Paths.SilksongAddressablesBundle("_"))!;
        return Path.GetFileName(Directory.GetFiles(aaBuild, "*_monoscripts.bundle").Single());
    }

    private static string? ReadOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}
