using System.Reflection;
using UnityEngine;

namespace HornetPlayer.Playground;

// HeroController + FSMs deref GlobalSettings singletons (Gameplay/UI/Effects/Camera/…). Each is a
// GlobalSettingsBase<T> whose Get() does Addressables.LoadAssetAsync("GlobalSettings/<name>.asset") — a Silksong
// addressables key HK's catalog doesn't have (and we load bundles manually, not via Addressables), so Get() falls
// back to an EMPTY ScriptableObject.CreateInstance<T>() with null tool/config fields -> NullRefs (e.g.
// GetMaxFallVelocity -> Gameplay.WeightedAnkletTool.Status). All real, populated SOs live in ONE bundle that is NOT
// a Hero_Hornet dependency (GlobalSettings load via Addressables at boot), so we load it explicitly. Their tool/crest
// PPtrs resolve against the hero dep closure that's already resident. Fix: assign each GlobalSettingsBase<T>._instance
// directly, bypassing Addressables — one generic pass covers all ~8 settings types.
internal static class GlobalSettingsBootstrap {
    private static string BundlePath => Paths.SilksongAaBundle("globalsettings_assets_all.bundle");

    private static AssetBundle? bundle;

    // True only when WE loaded the bundle via LoadFromFile (so Cleanup may Unload it). False when we reused a bundle
    // Addressables already mounted — Addressables owns it; unloading it would break the live catalog.
    private static bool ownsBundle;

    internal static int Apply() {
        if (bundle == null) {
            // ToolItemManagerBootstrap loads `_GameManager` via Addressables (runs just before us in SpawnReal), and
            // globalsettings_assets_all IS in its dep closure -> the Silksong catalog already mounted this bundle. A
            // second LoadFromFile of the same files throws "another AssetBundle with the same files is already loaded".
            // Reuse the mounted one (and leave ownership with Addressables). Addressables names its bundles by HASH,
            // not "globalsettings", so match by CONTENT (an asset path with global+settings), not by bundle.name.
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
                Log.Info($"[GlobalSettings] LoadFromFile (no mounted bundle among {scanned} loaded)");
            }
            else {
                Log.Info($"[GlobalSettings] reusing mounted bundle '{bundle.name}' (scanned {scanned} loaded)");
            }
        }

        if (bundle == null) {
            Log.Error("[GlobalSettings] bundle not available");
            return 0;
        }

        var n = 0;
        // Do NOT LoadAllAssets: this is a catch-all bundle (quests, particles, …). Loading every ScriptableObject runs
        // their OnEnable — MainQuest.OnEnable -> QuestType.Create -> LocalisedString -> Localization cctor (throws), and
        // we don't want those side-effects anyway. Load ONLY the "Global … Settings" assets by name.
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
            // CRITICAL: Get() gates on _foundInstance — `if (!_foundInstance) { ...Addressables (missing) -> empty
            // CreateInstance<T>()...; _foundInstance = true; } return _instance;`. Without setting _foundInstance our
            // _instance is ignored on the first Get(), which then caches an EMPTY SO (null tool refs -> NullRefs in
            // GetTotalFrostSpeed/GetMaxFallVelocity). Set it so Get() returns our real, populated SO.
            baseT?.GetField("_foundInstance", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, true);
            Log.Info($"[GlobalSettings] {so.GetType().Name}._instance <- '{so.name}'");
            n++;
        }

        Log.Info($"[GlobalSettings] bootstrapped {n} GlobalSettings instances");
        return n;
    }

    internal static void Cleanup() {
        // Only unload a bundle WE loaded. If we reused the Addressables-mounted one, leave it to Addressables.
        if (bundle != null && ownsBundle) bundle.Unload(true);
        bundle = null;
        ownsBundle = false;
    }
}
