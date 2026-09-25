using System;
using HornetInHallownest.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Core;

// Serves Resource.Load assets from the bundled `silksong-resources.bundle`, which contains scripts remapped to `Silksong.*`.
internal static class SilksongResources {
    private static Api? installed;
    private static AssetBundle? bundle;
    private static string ResourcesBundlePath => Paths.ModFile("silksong-resources.bundle");

    // Unity's own interception point for Resources.Load, rather than a native detour on Resources.Load itself.
    // Resources.Load(string, Type) is a 9-instruction forwarder to ResourcesAPI.ActiveAPI.Load; MonoMod cannot
    // reliably patch something that small (on HK 1.5.78 the detour dies inside DetourNativeX86Platform.Apply with
    // a NullReferenceException, i.e. no native entry to write to). overrideAPI is a public, supported setter and
    // exists identically on both Unity generations, so this is the better mechanism on either host.
    private sealed class Api : ResourcesAPI {
        protected override Object Load(string path, Type systemTypeInstance) => Detour(Base, path, systemTypeInstance);

        // `base.Load` cannot be converted to a delegate directly, so route through an instance method.
        private Object Base(string path, Type? systemTypeInstance) => base.Load(path, systemTypeInstance);
    }

    internal static void Install() {
        if (installed != null) return;

        bundle = AssetBundle.LoadFromFile(ResourcesBundlePath);
        if (bundle == null) {
            Log.Error($"[SilksongResources] LoadFromFile failed: {ResourcesBundlePath}");
            return;
        }

        Log.Debug($"[SilksongResources] loaded resources bundle ({bundle.GetAllAssetNames().Length} assets)");

        installed = new Api();
        ResourcesAPI.overrideAPI = installed;
    }

    internal static void Cleanup() {
        if (installed != null && ReferenceEquals(ResourcesAPI.overrideAPI, installed)) ResourcesAPI.overrideAPI = null;
        installed = null;
        if (bundle) bundle.Unload(true);
        bundle = null;
    }

    private static Object? Detour(Func<string, Type?, Object> orig, string path, Type? type) {
        if (!bundle || string.IsNullOrEmpty(path)) return orig(path, type);
        return SilksongContext.Active ? LoadForSilksong(orig, path, type) : LoadHkFirst(orig, path, type);
    }

    // Silksong code loading its own assets
    private static Object? LoadForSilksong(Func<string, Type?, Object> orig, string path, Type? type) {
        var served = ServeFromBundle(path, type);
        if (served != null) return served;
        if (IsLanguages(path)) return null; // These are null in silksong as well

        var hk = orig(path, type);
        return hk != null ? hk : LogMiss(path, type);
    }

    private static Object? LoadHkFirst(Func<string, Type?, Object> orig, string path, Type? type) {
        var hk = orig(path, type);
        if (hk != null) return hk;

        var served = ServeFromBundle(path, type);
        if (served != null) return served;

        return IsLanguages(path) ? null : LogMiss(path, type);
    }

    private static Object? ServeFromBundle(string path, Type? type) {
        // Container keys are lowercase Resources paths
        var key = path.ToLowerInvariant();
        Object? served;
        try {
            served = type != null ? bundle!.LoadAsset(key, type) : bundle!.LoadAsset(key);
            // Typed LoadAsset misses when the caller asks for a base type but the bundle holds the derived concrete
            // asset (e.g. LocalizationProjectSettingsBase vs the derived LocalizationProjectSettings). Retry untyped.
            if (served == null && type != null) {
                var any = bundle.LoadAsset(key);
                if (any != null && type.IsInstanceOfType(any)) served = any;
            }
        } catch (Exception e) {
            Log.Error($"[SilksongResources] LoadAsset '{key}' threw: {e.Message}");
            return null;
        }

        if (served != null) {
            Log.DebugOnce($"resserve|{key}",
                $"[Resources.Load] SERVE '{path}' as {type?.Name} (ctx={SilksongContext.Active}) <- silksong-resources.bundle");
        }

        return served;
    }

    private static bool IsLanguages(string path) => path.StartsWith("Languages/", StringComparison.Ordinal);

    private static Object? LogMiss(string path, Type? type) {
        Log.ErrorOnce($"resmiss|{path.ToLowerInvariant()}",
            $"[Resources.Load] '{path}' as {type?.Name} missed HK Resources and the Silksong bundle");
        return null;
    }
}
