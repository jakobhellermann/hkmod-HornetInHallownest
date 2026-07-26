using System;
using System.Linq;
using System.Reflection;
using HornetInHallownest.Util;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace HornetInHallownest.Core;

// Register Silksong's Addressables catalog.
internal static class SilksongAddressables {
    // Contains MonoScript entries prefixed with `Silksong.*`
    private const string MonoScriptsBundleName = "monoscripts.silksong.bundle";

    internal static void EnsureMounted() {
        var aa = Paths.SilksongAddressables;
        var catalogId = $"{aa}/catalog.bin";
        if (IsRegistered(catalogId)) return; // survives hot reload

        InstallIdTransform(aa, Paths.ModFile(MonoScriptsBundleName));
        MarkRuntimeStarted($"{aa}/settings.json");
        var locator = LoadCatalog(catalogId);
        Log.Debug($"[Addressables] Silksong catalog mounted (locator={locator})");
    }

    private static bool IsRegistered(string catalogId) {
        return Addressables.ResourceLocators.Any(l => l.LocatorId == catalogId);
    }

    private static void InstallIdTransform(string silksongAa, string monoScriptsBundle) {
        var hkAa = $"{Application.streamingAssetsPath}/aa".Replace('\\', '/');
        Addressables.InternalIdTransformFunc = loc => RewriteId(loc.InternalId, hkAa, silksongAa, monoScriptsBundle);
    }

    private static string RewriteId(string id, string hkAa, string silksongAa, string monoScriptsBundle) {
        if (string.IsNullOrEmpty(id)) return id;
        if (id.EndsWith("_monoscripts.bundle", StringComparison.Ordinal)) return monoScriptsBundle;
        return id.Replace(hkAa, silksongAa);
    }

    // The public InitializeAsync facade hardcodes RuntimePath/settings.json, so we call the impl
    // overload with Silksong's absolute settings.json instead (reachable only by reflection).
    // It reports Failed (its catalog-location discovery comes up empty); we only need it to complete, so the
    // runtime is marked started and the following LoadContentCatalogAsync doesn't wait on HK's default init.
    private static void MarkRuntimeStarted(string settingsPath) {
        var impl = typeof(Addressables)
                       .GetProperty("m_Addressables",
                           BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                       ?.GetValue(null)
                   ?? throw new InvalidOperationException("Addressables.m_Addressables not found");
        var init = impl.GetType().GetMethod("InitializeAsync", [typeof(string), typeof(string), typeof(bool)])
                   ?? throw new InvalidOperationException(
                       "AddressablesImpl.InitializeAsync(string,string,bool) not found");
        var handle = init.Invoke(impl, [settingsPath, null, false])!;
        handle.GetType().GetMethod("WaitForCompletion")!.Invoke(handle, null);
    }

    private static string? LoadCatalog(string catalogId) {
        var op = Addressables.LoadContentCatalogAsync(catalogId, false);
        op.WaitForCompletion();
        if (op.Status != AsyncOperationStatus.Succeeded) {
            throw new InvalidOperationException($"catalog load {op.Status} for {catalogId}");
        }

        return op.Result?.LocatorId;
    }
}
