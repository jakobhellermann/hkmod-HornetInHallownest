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
    // The IL-prefixed MonoScripts bundle (same CAB-283454ff as Silksong's original *_monoscripts.bundle, but m_Script
    // entries repointed to the Silksong.* assemblies). ALL MonoScripts across all 1155 asset bundles are centralized in
    // that one CAB (verified by scan: zero asset bundles carry their own), so substituting this one bundle file makes
    // every m_Script PPtr — hero, GlobalPool, everything — bind to Silksong.* instead of the originals (which collide
    // with HK's identically-named Assembly-CSharp types).
    private const string RemappedMonoScripts =
        "/home/jakob/dev/hk/mods/HornetPlayer/Source/lib/monoscripts.silksong.bundle";

    // HK's addressables runtime path (= Addressables.RuntimePath), e.g. <hk>/hollow_knight_Data/StreamingAssets/aa.
    private static string HkAa => (Application.streamingAssetsPath + "/aa").Replace('\\', '/');
    private static string CatalogId => SilksongAa + "/catalog.bin";

    // Hot-reload safe: the registered locator + the InternalIdTransformFunc live in Unity's ResourceManager, which
    // survives our DLL reload — a `static bool initialized` would reset per reload and re-register a DUPLICATE locator.
    // So the source of truth is the ResourceManager's locator list, not a static flag.
    private static bool CatalogRegistered() {
        foreach (var loc in Addressables.ResourceLocators)
            if (loc.LocatorId == CatalogId) return true;
        return false;
    }

    internal static object Ensure() {
        try {
            if (CatalogRegistered()) return new { ok = true, note = "already registered" };

            var hkAa = HkAa;
            // Redirect any internal id resolved under HK's empty aa -> Silksong's aa. Applies to catalog.bin + every
            // *_assets_*.bundle (they go through ResourceManager.ProvideResource, which calls this). It does NOT apply to
            // the settings.json read during init (TextDataProvider reads it directly) — handled below by passing the
            // absolute Silksong settings path.
            Addressables.InternalIdTransformFunc = loc => {
                var id = loc.InternalId;
                if (id == null) return id;
                if (id.Contains(hkAa)) id = id.Replace(hkAa, SilksongAa);
                // Substitute the remapped monoscripts bundle so m_Script PPtrs bind to Silksong.* (see RemappedMonoScripts).
                if (id.EndsWith("_monoscripts.bundle")) return RemappedMonoScripts;
                return id;
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
            var cat = Addressables.LoadContentCatalogAsync(CatalogId, autoReleaseHandle: false);
            cat.WaitForCompletion();
            var ok = cat.Status == AsyncOperationStatus.Succeeded;
            var locatorKeys = ok && cat.Result != null ? cat.Result.LocatorId : null;
            // NOTE: settings-init is EXPECTED to report Failed (its catalog-location discovery comes up empty); the real
            // success signal is loadCatalog=Succeeded — that's the registered Silksong locator. Not an error.
            Log.Info($"[Addressables] Silksong catalog registered: loadCatalog={cat.Status} (settings-init={initStatus}, expected-Failed; locator={locatorKeys})");
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
            var r = new { key, status = h.Status.ToString(), loaded = obj != null, name = obj != null ? obj.name : null };
            return r;
        } catch (Exception e) {
            return new { key, error = (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message };
        }
    }

    // Viability test for "load the hero via Addressables" (option A): load Hero_Hornet through the registered catalog
    // and report how its ROOT components bound — Silksong.AssemblyCSharp (good), HK's Assembly-CSharp (false-bind), or
    // null (missing script). This is the make-or-break: if the monoscripts redirect works, every component resolves to
    // Silksong.*. Does NOT instantiate (inspecting the prefab's components is enough to read the binding). GET /addr-load-hero
    internal static object LoadHero() {
        try {
            Ensure();
            var h = Addressables.LoadAssetAsync<GameObject>("Hero_Hornet");
            var prefab = h.WaitForCompletion();
            if (prefab == null) return new { error = "Hero_Hornet load returned null", status = h.Status.ToString() };

            var comps = prefab.GetComponents<Component>();
            var byAsm = new System.Collections.Generic.Dictionary<string, int>();
            var rootComponents = new System.Collections.Generic.List<string>();
            var missing = 0;
            foreach (var c in comps) {
                if (c == null) { missing++; rootComponents.Add("<missing script>"); continue; }
                var asm = c.GetType().Assembly.GetName().Name;
                byAsm[asm] = byAsm.TryGetValue(asm, out var n) ? n + 1 : 1;
                rootComponents.Add($"{c.GetType().FullName} [{asm}]");
            }
            return new {
                loaded = true, name = prefab.name, status = h.Status.ToString(),
                rootTotal = comps.Length, missingScripts = missing,
                byAssembly = byAsm,
                rootComponents,
            };
        } catch (Exception e) {
            var ex = e.InnerException ?? e;
            return new { error = ex.GetType().Name + ": " + ex.Message };
        }
    }

    internal static void Cleanup() {
        // Process-lifetime addressables state; nothing safe to tear down per hot-reload. Leave the transform func +
        // initialized catalog in place (clearing them mid-session would break in-flight Silksong loads).
    }
}
