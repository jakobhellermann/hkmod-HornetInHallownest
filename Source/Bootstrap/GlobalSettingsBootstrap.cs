using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;

namespace HornetInHallownest.Bootstrap;

// GlobalSettingsBase<T>.Get() falls back to an empty settings object, so the real ones are assigned from their bundle.
internal static class GlobalSettingsBootstrap {
    private static AssetBundle? bundle;

    // False when Addressables mounted it, which owns it then.
    private static bool ownsBundle;
    private static string BundlePath => Paths.SilksongAddressablesBundle("globalsettings_assets_all.bundle");

    internal static void Apply() {
        if (bundle == null) {
            // Usually already mounted by Addressables, under a hash name, and a second LoadFromFile throws.
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles()) {
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
            }
        }

        // The bundle's other assets have side-effecting OnEnables (MainQuest throws in the Localization cctor).
        foreach (var name in bundle.GetAllAssetNames()) {
            var lower = name.ToLowerInvariant();
            if (!(lower.Contains("global") && lower.Contains("settings"))) continue;
            var so = bundle.LoadAsset<ScriptableObject>(name);
            if (so == null) continue;
            var baseT = so.GetType().BaseType;
            var f = baseT?.GetFieldInfo("_instance", ReflectionExtension.StaticAnyVisibility, logFailure: false);
            if (f == null) continue;
            f.SetValue(null, so);
            // Get() ignores _instance until it is set.
            baseT?.SetFieldValue("_foundInstance", true);
        }
    }

    internal static void Cleanup() {
        if (bundle != null && ownsBundle) bundle.Unload(true);
        bundle = null;
        ownsBundle = false;
    }
}
