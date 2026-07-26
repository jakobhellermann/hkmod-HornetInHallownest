using System.Reflection;
using HornetInHallownest.HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.Playground;

// HeroController + FSMs deref GlobalSettings singletons (Gameplay/UI/Effects/Camera/…). Each GlobalSettingsBase<T>.Get()
// loads via an addressables key HK's catalog lacks, so it falls back to an empty SO -> NullRefs (e.g. GetMaxFallVelocity).
// The real SOs live in one bundle (not a Hero_Hornet dep), so we load it explicitly and assign each _instance directly.
internal static class GlobalSettingsBootstrap {
    private static AssetBundle? bundle;

    // True only when we loaded the bundle via LoadFromFile (so Cleanup may Unload it). False when we reused a bundle
    // Addressables already mounted — Addressables owns it; unloading it would break the live catalog.
    private static bool ownsBundle;
    private static string BundlePath => Paths.SilksongAddressablesBundle("globalsettings_assets_all.bundle");

    internal static int Apply() {
        if (bundle == null) {
            // The globalsettings bundle is usually already mounted by Addressables (in _GameManager's dep closure); a
            // second LoadFromFile of the same files throws. Reuse it — matched by content (Addressables names by hash).
            var scanned = 0;
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles()) {
                if (b == null) continue;
                scanned++;
                foreach (var an in b.GetAllAssetNames()) {
                    var l = an.ToLowerInvariant();
                    if (l.Contains("global") && l.Contains("settings")) {
                        bundle = b;
                        break;
                    }
                }

                if (bundle != null) break;
            }

            if (bundle == null) {
                bundle = AssetBundle.LoadFromFile(BundlePath);
                ownsBundle = true;
                Log.Debug($"[GlobalSettings] LoadFromFile (no mounted bundle among {scanned} loaded)");
            }
            else {
                Log.Debug($"[GlobalSettings] reusing mounted bundle '{bundle.name}' (scanned {scanned} loaded)");
            }
        }

        if (bundle == null) {
            Log.Error("[GlobalSettings] bundle not available");
            return 0;
        }

        var n = 0;
        // Do not LoadAllAssets: this catch-all bundle's other SOs run side-effecting OnEnables (e.g. MainQuest ->
        // Localization cctor throws). Load only the "Global … Settings" assets by name.
        foreach (var name in bundle.GetAllAssetNames()) {
            var lower = name.ToLowerInvariant();
            if (!(lower.Contains("global") && lower.Contains("settings"))) continue;
            var so = bundle.LoadAsset<ScriptableObject>(name);
            if (so == null) continue;
            // A GlobalSettings SO's runtime type derives from GlobalSettingsBase<ThatType>, which holds the private
            // static _instance that the Get() accessor returns.
            var baseT = so.GetType().BaseType;
            var f = baseT?.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) continue;
            f.SetValue(null, so);
            // critical: Get() gates on _foundInstance; without setting it, our _instance is ignored on the first Get()
            // and an empty SO is cached instead. Set it so Get() returns our populated SO.
            baseT?.GetField("_foundInstance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, true);
            Log.Debug($"[GlobalSettings] {so.GetType().Name}._instance <- '{so.name}'");
            n++;
        }

        Log.Debug($"[GlobalSettings] bootstrapped {n} GlobalSettings instances");
        return n;
    }

    internal static void Cleanup() {
        // Only unload a bundle we loaded. If we reused the Addressables-mounted one, leave it to Addressables.
        if (bundle != null && ownsBundle) bundle.Unload(true);
        bundle = null;
        ownsBundle = false;
    }
}
