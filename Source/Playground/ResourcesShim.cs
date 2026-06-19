using System;
using System.Collections.Generic;
using System.Reflection;
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
    private const string BundlePath =
        "/home/jakob/dev/hk/mods/HornetPlayer/Source/lib/silksong-resources.bundle";

    private static Hook? hook;
    private static AssetBundle? bundle;
    private static readonly HashSet<string> loggedMiss = new();
    private static readonly HashSet<string> loggedServe = new();

    internal static void Install() {
        if (hook != null) return;
        bundle = AssetBundle.LoadFromFile(BundlePath);
        if (bundle == null) Log.Error($"[ResShim] LoadFromFile failed: {BundlePath}");
        else Log.Info($"[ResShim] loaded resources bundle ({bundle.GetAllAssetNames().Length} assets)");

        var mi = typeof(Resources).GetMethod(nameof(Resources.Load), new[] { typeof(string), typeof(Type) });
        if (mi == null) { Log.Error("[ResShim] Resources.Load(string,Type) not found"); return; }
        hook = new Hook(mi, (Func<Func<string, Type, Object>, string, Type, Object>)Detour);
        Log.Info("[ResShim] installed Resources.Load(string,Type) hook");
    }

    private static Object Detour(Func<string, Type, Object> orig, string path, Type type) {
        var res = orig(path, type);
        if (res != null || bundle == null || string.IsNullOrEmpty(path)) return res;

        // Serve from the Silksong resources bundle (lowercase key). LoadAsset binds MonoBehaviours to the Silksong.*
        // types via the bundle's embedded, remapped monoscripts + per-entry preload table.
        var key = path.ToLowerInvariant();
        Object served;
        try { served = type != null ? bundle.LoadAsset(key, type) : bundle.LoadAsset(key); }
        catch (Exception e) { Log.Error($"[ResShim] LoadAsset '{key}' threw: {e.Message}"); return res; }

        if (served != null) {
            if (loggedServe.Add(key))
                Log.Info($"[Resources.Load] SERVE '{path}' as {type?.Name} <- silksong-resources.bundle");
            return served;
        }
        if (loggedMiss.Add(key)) Log.Error($"[Resources.Load] MISS '{path}' as {type?.Name} (not in bundle)");
        return res;
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
        if (bundle != null) { bundle.Unload(true); bundle = null; }
        loggedMiss.Clear();
        loggedServe.Clear();
    }

    // Debug: reload silksong-resources.bundle from disk WITHOUT touching the hook, so we can iterate on the bundle
    // (rebuild via repack_resources) and re-test in-game without a hot-reload or game restart. Pair with DumpLocalization.
    internal static void Reload() {
        if (bundle != null) { bundle.Unload(true); bundle = null; }
        loggedMiss.Clear();
        loggedServe.Clear();
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
            return new { path, type = o.GetType().FullName, name = o.name, textLen = ta != null ? ta.bytes.Length : (int?)null };
        } catch (Exception e) { return new { path, error = e.Message }; }
    }

    // Debug: load the served Silksong LocalizationSettings and read sheetTitles directly — the ground-truth readout for
    // whether the baked typetree deserializes correctly ("General","Map Zones",…) or garbage. Bypasses the Language cctor.
    internal static object DumpLocalization() {
        var t = Type.GetType("TeamCherry.Localization.LocalizationSettings, Silksong.TeamCherryLocalization");
        if (t == null) return new { error = "type TeamCherry.Localization.LocalizationSettings (Silksong.TeamCherryLocalization) not found" };
        var so = Resources.Load("Languages/LocalizationSettings", t);
        if (so == null) return new { error = "Resources.Load returned null", bundleLoaded = bundle != null };
        var f = t.GetField("sheetTitles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var titles = f?.GetValue(so) as string[];
        return new { name = so.name, type = so.GetType().FullName, count = titles?.Length, sheetTitles = titles };
    }
}
