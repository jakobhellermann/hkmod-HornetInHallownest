#if HK1578
using System.Collections;
using System.Collections.Generic;
using HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.Modules.Hk1578;

// Silksong's Unity 6 shaders draw magenta on Unity 2020, some while reporting isSupported. On every material, a shader
// loaded from a Silksong bundle is replaced by HK's same-named shader or a generic stand-in; masking shaders have no
// stand-in and become transparent. Patches Material assets, so prefabs are fixed before they're ever instantiated.
public sealed class ShaderFallbackModule : ModuleBase {
    private static ShaderFallbackModule? instance;

    // HK's shaders before any Silksong content loads. A later shader with one of these names is a Silksong copy, even
    // one no bundle scan saw (e.g. the resources bundle's TextMeshPro shaders).
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
    private static readonly WaitForSecondsRealtime sweepInterval = new(0.5f);

    public override string Id => "shader-fallback";

    public override void Initialize() {
        instance = this;
        // Scan bundles as Addressables hands them out: some are released before Hornet spawns, and their shaders would
        // otherwise pass as HK's own.
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
            yield return sweepInterval;
            if (!HornetSpawner.HornetRoot) continue; // nothing of Silksong's is rendered before she spawns
            FixAll();
        }
    }

    private void FixAll() {
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
        var host = HostShader(name);
        if (host) {
            mat.shader = host;
            return;
        }

        var replacement = IsMaskingShader(name) ? Find("Sprites/Default") : GenericReplacement(name);
        if (!replacement) {
            LogErrorOnce($"shader:{name}", $"no replacement for Silksong shader '{name}' (material '{mat.name}')");
            return;
        }

        mat.shader = replacement;
        if (IsMaskingShader(name)) {
            // Masks only make sense with their stencil/blend logic; with a plain sprite shader they'd be solid shapes.
            mat.color = Color.clear;
            LogInfoOnce($"shader:{name}", $"masking shader '{name}' (material '{mat.name}') has no HK counterpart -> " +
                                          "transparent");
        } else {
            LogInfoOnce($"shader:{name}", $"Silksong shader '{name}' (material '{mat.name}') has no HK counterpart " +
                                          $"-> generic '{replacement!.name}'");
        }
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
        foreach (var sh in bundle.LoadAllAssets<Shader>())
            if (sh && foreignShaders.Add(sh.GetInstanceID()))
                found = true;
        return found;
    }

    private Shader? GenericReplacement(string name) {
        if (replacementByName.TryGetValue(name, out var cached)) return cached;

        Shader? pick = null;
        if (name.StartsWith("UI/", System.StringComparison.Ordinal))
            pick = Find("UI/Default");
        if (!pick && ContainsAny(name, "Additive", "Screen", "Light", "Glow", "Add ", "Particles"))
            pick = Find("Legacy Shaders/Particles/Additive", "Particles/Additive", "Sprites/Default");
        if (!pick && name.StartsWith("tk2d/", System.StringComparison.Ordinal))
            pick = Find("tk2d/BlendVertexColor", "Sprites/Default");
        if (!pick) pick = Find("Sprites/Default");

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
}
#endif
