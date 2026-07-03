using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace HornetPlayer.Playground;

// Register Silksong's Addressables catalog into HK's runtime so Silksong code (GameManager.EnsureGlobalPool ->
// LoadAssetAsync<GameObject>("GlobalPool"), and anything else later) loads normally — instead of failing with
// "RuntimeData is null" / "No Location found for Key=…" -> Instantiate(null). This is the proper fix, not a stub: HK
// ships NO addressables (no hollow_knight_Data/StreamingAssets/aa), so its Addressables runtime is empty and there's
// nothing to collide with — we own it entirely.
//
// How it resolves paths: Silksong's settings.json points the catalog at "{…Addressables.RuntimePath}/catalog.bin", and
// RuntimePath resolves at runtime to *HK's* (empty) StreamingAssets/aa. So every addressables internal id (settings,
// catalog, bundles) comes out under HK's aa. We install an InternalIdTransformFunc that rewrites that HK aa prefix to
// Silksong's real aa folder, then InitializeAsync() reads Silksong's settings.json + catalog.bin and the
// AssetBundleProvider loads the bundles from Silksong's install — all transparently.
internal static class AddressablesBootstrap {
    // The IL-prefixed MonoScripts bundle (same CAB-283454ff as Silksong's original *_monoscripts.bundle, but m_Script
    // entries repointed to the Silksong.* assemblies). ALL MonoScripts across all 1155 asset bundles are centralized in
    // that one CAB (verified by scan: zero asset bundles carry their own), so substituting this one bundle file makes
    // every m_Script PPtr — hero, GlobalPool, everything — bind to Silksong.* instead of the originals (which collide
    // with HK's identically-named Assembly-CSharp types). Ships next to the DLL (see Paths.ModFile).
    private const string MonoScriptsBundle = "monoscripts.silksong.bundle";

    // HK's addressables runtime path (= Addressables.RuntimePath), e.g. <hk>/hollow_knight_Data/StreamingAssets/aa.
    private static string HkAa => (Application.streamingAssetsPath + "/aa").Replace('\\', '/');

    // Hot-reload safe: the registered locator + the InternalIdTransformFunc live in Unity's ResourceManager, which
    // survives our DLL reload — a `static bool initialized` would reset per reload and re-register a DUPLICATE locator.
    // So the source of truth is the ResourceManager's locator list, not a static flag.
    private static bool CatalogRegistered(string catalogId) {
        foreach (var loc in Addressables.ResourceLocators)
            if (loc.LocatorId == catalogId)
                return true;
        return false;
    }

    internal static object Ensure() {
        string silksongAa;
        string catalogId;
        string monoScripts;
        try {
            silksongAa = Paths.SilksongAa();
            catalogId = silksongAa + "/catalog.bin";
            monoScripts = Paths.ModFile(MonoScriptsBundle);
        } catch (Exception e) {
            // Detection/asset failure: the mod cannot bring up the hero without Silksong's assets. Log clearly and bail
            // — Ensure's callers proceed no further, but the rest of the game keeps running.
            Log.Error($"[Addressables] HornetPlayer cannot start — {e.Message}");
            return new { ok = false, error = e.Message };
        }

        try {
            if (CatalogRegistered(catalogId)) return new { ok = true, note = "already registered" };

            var hkAa = HkAa;
            // Redirect any internal id resolved under HK's empty aa -> Silksong's aa. Applies to catalog.bin + every
            // *_assets_*.bundle (they go through ResourceManager.ProvideResource, which calls this). It does NOT apply to
            // the settings.json read during init (TextDataProvider reads it directly) — handled below by passing the
            // absolute Silksong settings path.
            Addressables.InternalIdTransformFunc = loc => {
                var id = loc.InternalId;
                if (id == null) return id;
                if (id.Contains(hkAa)) id = id.Replace(hkAa, silksongAa);
                // Substitute the remapped monoscripts bundle so m_Script PPtrs bind to Silksong.* (see MonoScriptsBundle).
                if (id.EndsWith("_monoscripts.bundle")) return monoScripts;
                return id;
            };

            // Facade InitializeAsync() hardcodes RuntimePath/settings.json (= HK's empty aa). Call the impl overload
            // InitializeAsync(string runtimeDataPath, string providerSuffix, bool autoReleaseHandle) with Silksong's
            // absolute settings.json (no token -> no transform needed for it). It's not on the facade -> reflection on
            // the Addressables.m_Addressables (AddressablesImpl) singleton.
            var implProp = typeof(Addressables).GetProperty("m_Addressables",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            var impl = implProp?.GetValue(null) ?? throw new Exception("Addressables.m_Addressables not found");
            var init = impl.GetType()
                           .GetMethod("InitializeAsync", [typeof(string), typeof(string), typeof(bool)])
                       ?? throw new Exception("AddressablesImpl.InitializeAsync(string,string,bool) not found");
            var handle = init.Invoke(impl, [silksongAa + "/settings.json", null, false]);

            // handle is AsyncOperationHandle<IResourceLocator> (generic struct) -> reflect WaitForCompletion + Status.
            // Init may report Failed because the settings.json catalog-location discovery comes up empty — that's fine;
            // we just need it to COMPLETE so the runtime is marked started and LoadContentCatalogAsync won't chain-wait
            // on the (HK-default) init. We then load Silksong's catalog.bin DIRECTLY, which builds the locator itself.
            var ht = handle.GetType();
            ht.GetMethod("WaitForCompletion")!.Invoke(handle, null);
            var initStatus = ht.GetProperty("Status")!.GetValue(handle)?.ToString();

            // Load Silksong's catalog directly (bypasses settings catalog-location discovery). Bundle ids inside resolve
            // via {RuntimePath} -> HK aa -> our InternalIdTransformFunc -> Silksong aa.
            var cat = Addressables.LoadContentCatalogAsync(catalogId, false);
            cat.WaitForCompletion();
            var ok = cat.Status == AsyncOperationStatus.Succeeded;
            var locatorKeys = ok && cat.Result != null ? cat.Result.LocatorId : null;
            // NOTE: settings-init is EXPECTED to report Failed (its catalog-location discovery comes up empty); the real
            // success signal is loadCatalog=Succeeded — that's the registered Silksong locator. Not an error.
            Log.Info(
                $"[Addressables] Silksong catalog registered: loadCatalog={cat.Status} (settings-init={initStatus}, expected-Failed; locator={locatorKeys})");
            return new { ok, init = initStatus, catalog = cat.Status.ToString(), locator = locatorKeys };
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            Log.Error($"[Addressables] init failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return new { ok = false, error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    // Test helper: actually load a key and report what came back. GET /addr-load?key=GlobalPool
    internal static object Load(string key) {
        try {
            Ensure(); // idempotent (CatalogRegistered guard)
            var h = Addressables.LoadAssetAsync<GameObject>(key);
            var obj = h.WaitForCompletion();
            var r = new {
                key, status = h.Status.ToString(), loaded = obj != null, name = obj != null ? obj.name : null
            };
            return r;
        } catch (Exception e) {
            return new { key, error = (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message };
        }
    }

    internal static void Cleanup() {
        // Process-lifetime addressables state; nothing safe to tear down per hot-reload. Leave the transform func +
        // initialized catalog in place (clearing them mid-session would break in-flight Silksong loads).
    }
}
