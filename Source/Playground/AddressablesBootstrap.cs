using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

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
    private const string SilksongAa =
        "/home/jakob/.local/share/Steam/steamapps/common/Hollow Knight Silksong/Hollow Knight Silksong_Data/StreamingAssets/aa";

    private static bool initialized;

    // HK's addressables runtime path (= Addressables.RuntimePath), e.g. <hk>/hollow_knight_Data/StreamingAssets/aa.
    private static string HkAa => (Application.streamingAssetsPath + "/aa").Replace('\\', '/');

    internal static object Ensure() {
        try {
            if (initialized) return new { ok = true, note = "already initialized" };

            var hkAa = HkAa;
            // Redirect any internal id resolved under HK's empty aa -> Silksong's aa. Applies to catalog.bin + every
            // *_assets_*.bundle (they go through ResourceManager.ProvideResource, which calls this). It does NOT apply to
            // the settings.json read during init (TextDataProvider reads it directly) — handled below by passing the
            // absolute Silksong settings path.
            Addressables.InternalIdTransformFunc = loc => {
                var id = loc.InternalId;
                return id != null && id.Contains(hkAa) ? id.Replace(hkAa, SilksongAa) : id;
            };

            // Facade InitializeAsync() hardcodes RuntimePath/settings.json (= HK's empty aa). Call the impl overload
            // InitializeAsync(string runtimeDataPath, string providerSuffix, bool autoReleaseHandle) with Silksong's
            // absolute settings.json (no token -> no transform needed for it). It's not on the facade -> reflection on
            // the Addressables.m_Addressables (AddressablesImpl) singleton.
            var implProp = typeof(Addressables).GetProperty("m_Addressables", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            var impl = implProp?.GetValue(null) ?? throw new Exception("Addressables.m_Addressables not found");
            var init = impl.GetType().GetMethod("InitializeAsync", new[] { typeof(string), typeof(string), typeof(bool) })
                       ?? throw new Exception("AddressablesImpl.InitializeAsync(string,string,bool) not found");
            var handle = init.Invoke(impl, new object[] { SilksongAa + "/settings.json", null, false });

            // handle is AsyncOperationHandle<IResourceLocator> (generic struct) -> reflect WaitForCompletion + Status.
            // Init may report Failed because the settings.json catalog-location discovery comes up empty — that's fine;
            // we just need it to COMPLETE so the runtime is marked started and LoadContentCatalogAsync won't chain-wait
            // on the (HK-default) init. We then load Silksong's catalog.bin DIRECTLY, which builds the locator itself.
            var ht = handle.GetType();
            ht.GetMethod("WaitForCompletion")!.Invoke(handle, null);
            var initStatus = ht.GetProperty("Status")!.GetValue(handle)?.ToString();

            // Load Silksong's catalog directly (bypasses settings catalog-location discovery). Bundle ids inside resolve
            // via {RuntimePath} -> HK aa -> our InternalIdTransformFunc -> Silksong aa.
            var cat = Addressables.LoadContentCatalogAsync(SilksongAa + "/catalog.bin", autoReleaseHandle: false);
            cat.WaitForCompletion();
            initialized = cat.Status == AsyncOperationStatus.Succeeded;
            var locatorKeys = cat.Status == AsyncOperationStatus.Succeeded && cat.Result != null ? cat.Result.LocatorId : null;
            // NOTE: settings-init is EXPECTED to report Failed (its catalog-location discovery comes up empty); the real
            // success signal is loadCatalog=Succeeded — that's the registered Silksong locator. Not an error.
            Log.Info($"[Addressables] Silksong catalog registered: loadCatalog={cat.Status} (settings-init={initStatus}, expected-Failed; locator={locatorKeys})");
            return new { ok = initialized, init = initStatus, catalog = cat.Status.ToString(), locator = locatorKeys };
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            Log.Error($"[Addressables] init failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return new { ok = false, error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    // Test helper: actually load a key and report what came back. GET /addr-load?key=GlobalPool
    internal static object Load(string key) {
        try {
            if (!initialized) Ensure();
            var h = Addressables.LoadAssetAsync<GameObject>(key);
            var obj = h.WaitForCompletion();
            var r = new { key, status = h.Status.ToString(), loaded = obj != null, name = obj != null ? obj.name : null };
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
