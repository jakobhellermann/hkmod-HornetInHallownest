using System;
using HornetInHallownest.Playground;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.HornetInHallownest.Core;

// Serves Resource.Load assets from the bundled `silksong-resources.bundle`, which contains scripts remapped to `Silksong.*`.
internal static class SilksongResources {
    private static Hook? hook;
    private static AssetBundle? bundle;
    private static string ResourcesBundlePath => Paths.ModFile("silksong-resources.bundle");

    internal static void Install() {
        if (hook != null) return;

        bundle = AssetBundle.LoadFromFile(ResourcesBundlePath);
        if (bundle == null) {
            Log.Error($"[SilksongResources] LoadFromFile failed: {ResourcesBundlePath}");
            return;
        }

        Log.Debug($"[SilksongResources] loaded resources bundle ({bundle.GetAllAssetNames().Length} assets)");

        var target = typeof(Resources).GetMethod(nameof(Resources.Load), [typeof(string), typeof(Type)])!;
        hook = new Hook(target, (Func<Func<string, Type?, Object>, string, Type, Object?>)Detour);
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
        if(bundle) bundle.Unload(true);
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
