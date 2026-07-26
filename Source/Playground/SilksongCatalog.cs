using System;
using System.Linq;
using System.Reflection;
using HornetInHallownest.HornetInHallownest.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace HornetInHallownest.Playground;

// Mounts Silksong's Addressables catalog into HK's runtime so Silksong code (GameManager.EnsureGlobalPool ->
// LoadAssetAsync("GlobalPool"), the hero, …) loads normally. HK ships no addressables, so its runtime is empty and
// we own it. Silksong's catalog resolves every internal id under {RuntimePath} = HK's empty aa; we rewrite that prefix
// to Silksong's real aa so the bundles load from its install.
internal static class SilksongCatalog {
    private const string MonoScriptsBundleName = "monoscripts.silksong.bundle";

    // Idempotent, throws on unrecoverable failure. The source of truth for "already mounted" is the ResourceManager's
    // locator list (survives our DLL hot-reload — a static flag would reset and re-register a duplicate).
    internal static void EnsureMounted() {
        var aa = Paths.SilksongAddressables;
        var catalogId = $"{aa}/catalog.bin";
        if (IsRegistered(catalogId)) return;

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

    // HK's empty aa -> Silksong's real aa, and every *_monoscripts.bundle -> the IL-prefixed one (so m_Script PPtrs
    // bind to Silksong.* instead of the originals that collide with HK's Assembly-CSharp). Pure for testability.
    private static string RewriteId(string id, string hkAa, string silksongAa, string monoScriptsBundle) {
        if (string.IsNullOrEmpty(id)) return id;
        if (id.EndsWith("_monoscripts.bundle", StringComparison.Ordinal)) return monoScriptsBundle;
        return id.Contains(hkAa) ? id.Replace(hkAa, silksongAa) : id;
    }

    // The public InitializeAsync facade hardcodes RuntimePath/settings.json (= HK's empty aa). We call the impl overload
    // with Silksong's absolute settings.json instead — only reachable via reflection on the AddressablesImpl singleton.
    // The op is EXPECTED to report Failed (its catalog-location discovery comes up empty); we only need it to COMPLETE
    // so the runtime is marked started and the following LoadContentCatalogAsync doesn't chain-wait on HK's default init.
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
        if (op.Status != AsyncOperationStatus.Succeeded)
            throw new InvalidOperationException($"catalog load {op.Status} for {catalogId}");
        return op.Result?.LocatorId;
    }

    // Process-lifetime state; nothing safe to tear down per hot-reload (clearing the transform / locator mid-session
    // would break in-flight Silksong loads).
    internal static void Cleanup() {
    }
}
