using System;
using System.Reflection;
using HornetPlayer.HornetInHallownest;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Resources.Load shim. Silksong code loads assets via Resources.Load("languages/…", "playmakerglobals", …) that live in
// Silksong's resources.assets — absent in HK, so they return null and surface as NullRefs / cctor throws far from the
// call site (notably TeamCherry.Localization.Language: Settings == null -> NRE in the type initializer, then every later
// LocalisedString access re-throws). Resources.Load only reads the built-in resources system, never AssetBundles — so we
// hook it: on a MISS, serve the asset from silksong-resources.bundle, which is Silksong's whole ResourceManager container
// repacked so its MonoBehaviours bind to the IL-prefixed Silksong.* assemblies (see rabex-env/examples/repack_resources.rs).
// Container keys are LOWERCASE Resources paths (Unity's own convention), so we lowercase the request before lookup. We
// only serve when HK's own Resources.Load misses; anything we still can't serve is logged once for visibility.
internal static class ResourcesShim {
    private static string BundlePath => Paths.ModFile("silksong-resources.bundle");

    private static Hook? hook;
    private static AssetBundle? bundle;

    internal static void Install() {
        if (hook != null) return;
        bundle = AssetBundle.LoadFromFile(BundlePath);
        if (bundle == null) Log.Error($"[ResShim] LoadFromFile failed: {BundlePath}");
        else Log.Info($"[ResShim] loaded resources bundle ({bundle.GetAllAssetNames().Length} assets)");

        var mi = typeof(Resources).GetMethod(nameof(Resources.Load), [typeof(string), typeof(Type)]);
        if (mi == null) {
            Log.Error("[ResShim] Resources.Load(string,Type) not found");
            return;
        }

        hook = new Hook(mi, (Func<Func<string, Type?, Object>, string, Type, Object?>)Detour);
        Log.Debug("[ResShim] installed Resources.Load(string,Type) hook");
    }

    private static Object? Detour(Func<string, Type?, Object> orig, string path, Type? type) {
        if (bundle == null || string.IsNullOrEmpty(path)) return orig(path, type);

        if (SilksongContext.Active) {
            // Silksong is loading its own assets: prefer the bundle over HK's same-path original. Scoped by time (not
            // caller — unreliable via the MonoMod trampoline); HK's loads happen outside the window. See SilksongContext.
            var served = ServeFromBundle(path, type);
            if (served != null) return served;

            if (path.StartsWith("Languages/"))
                // startup noise of silksong 
                return null;

            Log.Error($"[Resources.Load] MISS '{path}' as {type?.Name} could not be served from silksong Resources");
            var res = orig(path, type);
            if (res == null)
                Log.ErrorOnce($"resmiss|{path.ToLowerInvariant()}",
                    $"[Resources.Load] MISS '{path}' as {type?.Name} could not be served from hollowknight Resources either");
            return res;
        }

        // Default: HK's original wins; only fall back to the bundle on a miss.
        var orig0 = orig(path, type);
        if (orig0 != null) return orig0;

        var bundleAsset = ServeFromBundle(path, type);
        if (bundleAsset != null) return bundleAsset;
        if (bundleAsset == null)
            Log.ErrorOnce($"resmiss|{path.ToLowerInvariant()}",
                $"[Resources.Load] NULL '{path}' as {type?.Name} was loaded as null from hk context but is present in hkss. Are you missing SilksongContext?");

        return orig0;
    }

    // Load `path` from the Silksong resources bundle (lowercase key, Unity's container convention). LoadAsset binds
    // MonoBehaviours to the Silksong.* types via the bundle's embedded, remapped monoscripts + per-entry preload table.
    // Returns null on a bundle miss; logs each distinct SERVE once. MISS logging is the caller's job (it depends on
    // whether the original also missed).
    private static Object? ServeFromBundle(string path, Type? type) {
        var key = path.ToLowerInvariant();
        Object served;
        try {
            served = type != null ? bundle!.LoadAsset(key, type) : bundle!.LoadAsset(key);
        } catch (Exception e) {
            Log.Error($"[ResShim] LoadAsset '{key}' threw: {e.Message}");
            return null;
        }

        if (served != null)
            Log.InfoOnce($"resserve|{key}",
                $"[Resources.Load] SERVE '{path}' as {type?.Name} <- silksong-resources.bundle");
        return served;
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
        if (bundle != null) {
            bundle.Unload(true);
            bundle = null;
        }
    }

    // Debug: reload silksong-resources.bundle from disk WITHOUT touching the hook, so we can iterate on the bundle
    // (rebuild via repack_resources) and re-test in-game without a hot-reload or game restart. Pair with DumpLocalization.
    internal static void Reload() {
        if (bundle != null) {
            bundle.Unload(true);
            bundle = null;
        }

        bundle = AssetBundle.LoadFromFile(BundlePath);
        Log.Info(bundle != null
            ? $"[ResShim] reloaded bundle ({bundle.GetAllAssetNames().Length} assets)"
            : $"[ResShim] reload FAILED: {BundlePath}");
    }

    // Debug: load any Resources path through the shim and report what came back (or the exception). Used to isolate
    // whether a repacked asset deserializes — e.g. a plain TextAsset (no MonoScript/typetree) vs a MonoBehaviour.
    internal static object LoadRes(string path) {
        try {
            var o = Resources.Load(path);
            if (o == null) return new { path, result = "null" };
            var ta = o as TextAsset;
            return new {
                path, type = o.GetType().FullName, o.name, textLen = ta != null ? ta.bytes.Length : (int?)null
            };
        } catch (Exception e) {
            return new { path, error = e.Message };
        }
    }

    // Debug: load the served Silksong LocalizationSettings and read sheetTitles directly — the ground-truth readout for
    // whether the baked typetree deserializes correctly ("General","Map Zones",…) or garbage. Bypasses the Language cctor.
    internal static object DumpLocalization() {
        var t = Type.GetType("TeamCherry.Localization.LocalizationSettings, Silksong.TeamCherryLocalization");
        if (t == null)
            return new {
                error = "type TeamCherry.Localization.LocalizationSettings (Silksong.TeamCherryLocalization) not found"
            };
        // ReSharper disable once Unity.UnknownResource
        var so = Resources.Load("Languages/LocalizationSettings", t);
        if (so == null) return new { error = "Resources.Load returned null", bundleLoaded = bundle != null };
        var f = t.GetField("sheetTitles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var titles = f?.GetValue(so) as string[];
        return new { so.name, type = so.GetType().FullName, count = titles?.Length, sheetTitles = titles };
    }
}
