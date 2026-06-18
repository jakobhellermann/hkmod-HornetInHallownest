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
    private const string BundlePath =
        "/home/jakob/.local/share/Steam/steamapps/common/Hollow Knight Silksong/Hollow Knight Silksong_Data/StreamingAssets/aa/StandaloneLinux64/globalsettings_assets_all.bundle";

    private static AssetBundle? bundle;

    internal static int Apply() {
        if (bundle == null) bundle = AssetBundle.LoadFromFile(BundlePath);
        if (bundle == null) { Log.Error("[GlobalSettings] LoadFromFile failed"); return 0; }

        var n = 0;
        foreach (var so in bundle.LoadAllAssets<ScriptableObject>()) {
            // A GlobalSettings SO's runtime type derives from GlobalSettingsBase<ThatType>, which holds the private
            // static _instance that the Get() accessor returns.
            var baseT = so.GetType().BaseType;
            var f = baseT?.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) continue;
            f.SetValue(null, so);
            Log.Info($"[GlobalSettings] {so.GetType().Name}._instance <- '{so.name}'");
            n++;
        }
        Log.Info($"[GlobalSettings] bootstrapped {n} GlobalSettings instances");
        return n;
    }

    internal static void Cleanup() {
        if (bundle != null) { bundle.Unload(true); bundle = null; }
    }
}
