// Loads Unity 6 asset bundles on Unity 2020.2, which reads UnityFS only up to v7 and refuses bundles declaring a
// different engine version: Silksong's v8 bundles are read through the repacker as v7, with compat patches applied.
// ReSharper disable CheckNamespace
using System;
using System.Collections.Generic;
using System.IO;
using UnitySceneRepacker;

namespace UnityEngine.Compat;

internal static class UnityFsBundles {
    // Silksong's bundles declare engine version 0.0.0.
    private const string SilksongUnityVersion = "6000.0.50f1";

    // Streams have to stay open until their bundle is unloaded.
    private static readonly Dictionary<AssetBundleCreateRequest, Stream> streams = new Dictionary<AssetBundleCreateRequest, Stream>();

    internal static AssetBundleCreateRequest LoadFromFileAsync(string path, uint crc, ulong offset) {
        var version = UnityFsV8AsV7Stream.ReadVersion(path, (long)offset);
        if (version < 8) return AssetBundle.LoadFromFileAsync(path, crc, offset);
        if (offset != 0) throw new NotSupportedException($"UnityFS v{version} bundle at offset {offset} in {path}");

        var stream = BundleTranspile.Transpile(path, SilksongUnityVersion, Application.unityVersion);
        var request = AssetBundle.LoadFromStreamAsync(stream, crc);
        streams[request] = stream;
        return request;
    }

    // The game a bundle of its StreamingAssets belongs to.
    private static string GameDir(string bundlePath) {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(bundlePath)); dir != null; dir = dir.Parent)
            if (dir.Name.EndsWith("_Data", StringComparison.Ordinal)) return dir.Parent.FullName;
        throw new InvalidOperationException($"{bundlePath} isn't inside a game's data folder");
    }

    // The bundle's stream, to be disposed once the bundle is unloaded. Null for bundles loaded from a file.
    internal static Stream TakeStream(AssetBundle bundle) {
        foreach (var entry in streams)
            if (entry.Key.isDone && entry.Key.assetBundle == bundle) {
                streams.Remove(entry.Key);
                return entry.Value;
            }
        return null;
    }
}
