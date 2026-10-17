using System.Collections;
using System.Collections.Generic;
using HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.Modules;

// Silksong's shaders are compiled for Unity 6. Loaded into HK 1.5.78 (Unity 2020.2) they don't render correctly:
// Unity draws them with the magenta error shader, and some even report isSupported = true while doing so (Silksong's
// "Sprites/Lit" on dust/sand/leaf particles). So don't trust isSupported: every shader that came out of a Silksong
// asset bundle is replaced on every material using it, with HK's own shader of the same name if HK has one, otherwise
// the closest generic shader (keeps texture + vertex colour, loses special effects). Masking/darkness shaders have no
// sensible stand-in and would draw as solid shapes, so their materials are made fully transparent instead.
//
// Works on Material assets (Resources.FindObjectsOfTypeAll), not renderers, so prefab materials are fixed before the
// effect is ever instantiated and pooled/spawned particles never flash pink. HK's materials are never touched: only
// shaders loaded from asset bundles (HK 1.5.78 itself loads none; they are all Silksong's) count as foreign.
public sealed class ShaderFallbackModule : ModuleBase {
    private static ShaderFallbackModule? instance;

    // HK's own shaders, snapshotted by the mod before any Silksong content (resources bundle, Addressables) loads. A
    // later shader sharing a name with one of these is a Silksong copy even if no bundle scan saw it load (e.g. the
    // repacked resources bundle's TextMeshPro shaders behind the HUD's geo counter).
    private static Dictionary<string, Shader>? hkShaderSnapshot;

    internal static void SnapshotHostShaders() {
        hkShaderSnapshot = new Dictionary<string, Shader>();
        foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
            if (s && s.isSupported && !hkShaderSnapshot.ContainsKey(s.name))
                hkShaderSnapshot[s.name] = s;
    }

    private static bool IsShadowingHkShader(Shader s) =>
        hkShaderSnapshot != null && hkShaderSnapshot.TryGetValue(s.name, out var hk) && hk && hk != s;

    private readonly HashSet<int> foreignShaders = [];
    private readonly HashSet<string> scannedBundles = [];
    private readonly HashSet<int> checkedMaterials = [];
    private readonly Dictionary<string, Shader?> replacementByName = [];
    private Dictionary<string, Shader>? hostShaders;
    private Coroutine? sweep;

    public override string Id => "shader-fallback";

    public override void Initialize() {
        instance = this;
        // Record each Silksong bundle's shaders the moment Addressables hands the bundle out: some (e.g. the HUD rig's)
        // are released again before Hornet spawns, and a bundle that's gone from GetAllLoadedAssetBundles would leave
        // its shaders looking like HK's own (they report isSupported, so nothing else would catch them).
        Detour(typeof(UnityEngine.ResourceManagement.ResourceProviders.AssetBundleResource), "GetAssetBundle",
            (System.Func<System.Func<UnityEngine.ResourceManagement.ResourceProviders.AssetBundleResource, AssetBundle?>,
                UnityEngine.ResourceManagement.ResourceProviders.AssetBundleResource, AssetBundle?>)OnGetAssetBundle);
        sweep = StartCoroutine(Sweep());
    }

    private AssetBundle? OnGetAssetBundle(
        System.Func<UnityEngine.ResourceManagement.ResourceProviders.AssetBundleResource, AssetBundle?> orig,
        UnityEngine.ResourceManagement.ResourceProviders.AssetBundleResource self) {
        var bundle = orig(self);
        if (bundle && ScanBundle(bundle)) {
            hostShaders = null;
            replacementByName.Clear();
            checkedMaterials.Clear(); // materials seen before their shader was known to be foreign need a re-check
        }
        return bundle;
    }

    protected override void OnDeinitialize() {
        if (sweep != null) Runtime.StopCoroutine(sweep);
        sweep = null;
        if (instance == this) instance = null;
    }

    private IEnumerator Sweep() {
        while (true) {
            yield return new WaitForSecondsRealtime(0.5f);
            if (!HornetSpawner.HornetRoot) continue; // nothing of Silksong's is rendered before she spawns
            FixAll();
        }
    }

    internal void FixAll() {
        if (ScanNewBundles()) {
            hostShaders = null; // new foreign shaders may shadow names; rebuild the host map lazily
            replacementByName.Clear();
        }

        foreach (var mat in Resources.FindObjectsOfTypeAll<Material>()) {
            if (!mat || !checkedMaterials.Add(mat.GetInstanceID())) continue;
            var shader = mat.shader;
            if (!shader) continue;
            if (!foreignShaders.Contains(shader.GetInstanceID()) && shader.isSupported) {
                if (!IsShadowingHkShader(shader)) continue;
                foreignShaders.Add(shader.GetInstanceID());
            }
            FixMaterial(mat, shader.name);
        }
    }

    private void FixMaterial(Material mat, string name) {
        if (IsMaskingShader(name) && !HostShader(name)) {
            // Darkness/blackout masks only make sense with their stencil/blend logic; with a plain sprite shader they'd
            // be solid shapes over the scene. Keep them, but invisible.
            var clear = Find("Sprites/Default");
            if (clear) {
                mat.shader = clear;
                mat.color = Color.clear;
            }
            LogDebugOnce($"shader:{name}", $"masking shader '{name}' (material '{mat.name}') -> transparent");
            return;
        }

        var replacement = Replacement(name);
        if (replacement) mat.shader = replacement;
        LogDebugOnce($"shader:{name}", $"Silksong shader '{name}' (material '{mat.name}') -> " +
                                       $"{(replacement ? "'" + replacement!.name + "'" : "no replacement")}");
    }

    // Record the shaders of asset bundles we haven't looked at yet. True when any new foreign shader was found.
    private bool ScanNewBundles() {
        var found = false;
        foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            if (bundle && ScanBundle(bundle))
                found = true;
        return found;
    }

    private bool ScanBundle(AssetBundle bundle) {
        if (!scannedBundles.Add(bundle.name) || bundle.isStreamedSceneAssetBundle) return false;
        var found = false;
        try {
            foreach (var sh in bundle.LoadAllAssets<Shader>())
                if (sh && foreignShaders.Add(sh.GetInstanceID()))
                    found = true;
        } catch (System.Exception e) {
            LogDebug($"scan '{bundle.name}': {e.Message}");
        }

        return found;
    }

    private Shader? Replacement(string name) {
        if (replacementByName.TryGetValue(name, out var cached)) return cached;

        var pick = HostShader(name);
        if (!pick && name.StartsWith("UI/", System.StringComparison.Ordinal))
            pick = Find("UI/Default");
        if (!pick && ContainsAny(name, "Additive", "Screen", "Light", "Glow", "Add ", "Particles"))
            pick = Find("Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default");
        if (!pick && name.StartsWith("tk2d/", System.StringComparison.Ordinal))
            pick = Find("tk2d/BlendVertexColor", "Sprites/Default");
        pick ??= Find("Sprites/Default");

        replacementByName[name] = pick;
        return pick;
    }

    // HK's own working shader for a Silksong shader name. Silksong suffixes some Addressables-shipped copies of shared
    // shaders (e.g. "tk2d/BlendVertexColor (addressable)").
    private Shader? HostShader(string name) {
        hostShaders ??= CollectHostShaders();
        if (hostShaders.TryGetValue(name, out var same)) return same;
        const string suffix = " (addressable)";
        if (name.EndsWith(suffix, System.StringComparison.Ordinal) &&
            hostShaders.TryGetValue(name.Substring(0, name.Length - suffix.Length), out var baseShader))
            return baseShader;
        return null;
    }

    private Shader? Find(params string[] names) {
        hostShaders ??= CollectHostShaders();
        foreach (var n in names) {
            if (hostShaders.TryGetValue(n, out var s)) return s;
            var found = Shader.Find(n);
            if (found && found.isSupported && !foreignShaders.Contains(found.GetInstanceID())) return found;
        }

        return null;
    }

    private Dictionary<string, Shader> CollectHostShaders() {
        var map = hkShaderSnapshot != null ? new Dictionary<string, Shader>(hkShaderSnapshot) : new Dictionary<string, Shader>();
        foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
            if (s && s.isSupported && !foreignShaders.Contains(s.GetInstanceID()) && !IsShadowingHkShader(s) &&
                !map.ContainsKey(s.name))
                map[s.name] = s;
        return map;
    }

    private static bool IsMaskingShader(string name) => ContainsAny(name, "Darkness", "Blackout", "Masked", "Mask");

    private static bool ContainsAny(string s, params string[] parts) {
        foreach (var p in parts)
            if (s.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    // Diagnostic (AutoTest "dump-shader-name <name>"): every Shader object with that name and every material using it.
    internal static void DumpShaderName(string name) {
        foreach (var sh in Resources.FindObjectsOfTypeAll<Shader>())
            if (sh && sh.name == name)
                Util.Log.Info($"[shader-name] '{name}' id={sh.GetInstanceID()} supported={sh.isSupported} " +
                              $"foreign={instance != null && instance.foreignShaders.Contains(sh.GetInstanceID())} " +
                              $"maxLOD={sh.maximumLOD} passes={sh.passCount}");
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            if (m && m.shader && m.shader.name == name)
                Util.Log.Info($"[shader-name]   material '{m.name}' -> shader id {m.shader.GetInstanceID()}");
    }

    // Diagnostic (AutoTest "dump-shaders"): every material on renderers under Hornet plus all particle renderers, with
    // its shader, whether it came from a Silksong bundle, and its support flag.
    internal static void DumpShaders() {
        var seen = new HashSet<int>();
        var root = HornetSpawner.HornetRoot;
        foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>()) {
            if (!r || !r.gameObject.scene.IsValid()) continue;
            var underHornet = root && r.transform.IsChildOf(root!.transform);
            if (!underHornet && r is not ParticleSystemRenderer) continue;
            foreach (var m in r.sharedMaterials) {
                if (!m || !seen.Add(m.GetInstanceID())) continue;
                var sh = m.shader;
                var foreign = sh && instance != null && instance.foreignShaders.Contains(sh.GetInstanceID());
                Util.Log.Info($"[shader-dump] {(underHornet ? "hornet" : "particle")} '{r.name}' mat '{m.name}' " +
                              $"shader '{(sh ? sh.name : "<null>")}' foreign={foreign} " +
                              $"supported={(sh && sh.isSupported)} active={r.gameObject.activeInHierarchy}");
            }
        }
    }
}
