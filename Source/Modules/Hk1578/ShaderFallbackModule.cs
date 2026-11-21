#if HK1578
extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Modules.Hk1578;

// Silksong's Unity 6 shaders draw magenta on Unity 2020, some while reporting isSupported. On every material, a shader
// loaded from a Silksong bundle is replaced by HK's same-named shader or a generic stand-in; masking shaders have no
// stand-in and become transparent. Patches Material assets, so prefabs are fixed before they're ever instantiated.
public sealed class ShaderFallbackModule : ModuleBase {
    private readonly HashSet<int> foreignShaders = [];
    private readonly HashSet<string> scannedBundles = [];
    private readonly HashSet<int> checkedMaterials = [];
    private readonly HashSet<int> checkedObjects = [];
    private readonly Dictionary<string, Shader?> replacementByName = [];
    private Dictionary<string, Shader>? hostShaders;

    // HK's shaders by name, taken before any Silksong content loads. A same-named one loaded later is a Silksong copy,
    // even one embedded in a bundle without being one of its listed assets.
    private readonly Dictionary<string, Shader> hkShaders = [];

    // Every loaded material is (re)checked before the next frame renders.
    private bool rescan = true;

    public override string Id => "shader-fallback";

    public override void Initialize() {
        // Opened before this module: the resources bundle.
        foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles()) Scan(bundle);
        foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
            if (shader.isSupported && !foreignShaders.Contains(shader.GetInstanceID()) && !hkShaders.ContainsKey(shader.name))
                hkShaders[shader.name] = shader;

        // Addressables also opens dependency bundles, whose materials load without anything loaded from them directly.
        Detour(typeof(AssetBundleResource), nameof(AssetBundleResource.GetAssetBundle),
            (Func<Func<AssetBundleResource, AssetBundle?>, AssetBundleResource, AssetBundle?>)OnGetAssetBundle);
        // Bundles opened with LoadFromFile only show up here.
        Detour(typeof(AssetBundle), nameof(AssetBundle.LoadAsset),
            (Func<Func<AssetBundle, string, Type, Object>, AssetBundle, string, Type, Object>)((orig, self, name, type) =>
                Loaded(self, orig(self, name, type))), typeof(string), typeof(Type));
        Detour(typeof(AssetBundle), nameof(AssetBundle.LoadAssetWithSubAssets),
            (Func<Func<AssetBundle, string, Type, Object[]>, AssetBundle, string, Type, Object[]>)((orig, self, name, type) =>
                Loaded(self, orig(self, name, type))), typeof(string), typeof(Type));
        Detour(typeof(AssetBundle), nameof(AssetBundle.LoadAllAssets),
            (Func<Func<AssetBundle, Type, Object[]>, AssetBundle, Type, Object[]>)((orig, self, type) =>
                Loaded(self, orig(self, type))), typeof(Type));
        Detour(typeof(AssetBundle), nameof(AssetBundle.LoadAssetAsync),
            (Func<Func<AssetBundle, string, Type, AssetBundleRequest>, AssetBundle, string, Type, AssetBundleRequest>)(
                (orig, self, name, type) => LoadedAsync(self, orig(self, name, type))), typeof(string), typeof(Type));
        Detour(typeof(AssetBundle), nameof(AssetBundle.LoadAssetWithSubAssetsAsync),
            (Func<Func<AssetBundle, string, Type, AssetBundleRequest>, AssetBundle, string, Type, AssetBundleRequest>)(
                (orig, self, name, type) => LoadedAsync(self, orig(self, name, type))), typeof(string), typeof(Type));
        Detour(typeof(AssetBundle), nameof(AssetBundle.LoadAllAssetsAsync),
            (Func<Func<AssetBundle, Type, AssetBundleRequest>, AssetBundle, Type, AssetBundleRequest>)((orig, self, type) =>
                LoadedAsync(self, orig(self, type))), typeof(Type));
        // Prefabs a loaded asset only references (HeroController's effect prefabs) load their materials lazily, on the
        // first spawn.
        Detour(typeof(Silksong::ObjectPool), nameof(Silksong::ObjectPool.Spawn),
            (Func<Func<GameObject, Transform, Vector3, Quaternion, bool, GameObject>, GameObject, Transform, Vector3,
                Quaternion, bool, GameObject>)((orig, prefab, parent, position, rotation, steal) => {
                if (prefab) CheckGameObject(prefab);
                return orig(prefab, parent, position, rotation, steal);
            }), typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Quaternion), typeof(bool));

        Application.onBeforeRender += RescanIfNeeded;
    }

    protected override void OnDeinitialize() => Application.onBeforeRender -= RescanIfNeeded;

    private AssetBundle? OnGetAssetBundle(Func<AssetBundleResource, AssetBundle?> orig, AssetBundleResource self) {
        var bundle = orig(self);
        if (bundle) Scan(bundle!);
        return bundle;
    }

    private Object Loaded(AssetBundle bundle, Object result) {
        Scan(bundle);
        CheckObject(result);
        return result;
    }

    private Object[] Loaded(AssetBundle bundle, Object[] result) {
        Scan(bundle);
        foreach (var obj in result) CheckObject(obj);
        return result;
    }

    private AssetBundleRequest LoadedAsync(AssetBundle bundle, AssetBundleRequest request) {
        Scan(bundle);
        request.completed += _ => {
            foreach (var obj in request.allAssets) CheckObject(obj);
        };
        return request;
    }

    private void Scan(AssetBundle bundle) {
        if (!scannedBundles.Add(bundle.name) || bundle.isStreamedSceneAssetBundle) return;
        var found = false;
        foreach (var shader in bundle.LoadAllAssets<Shader>())
            if (shader && foreignShaders.Add(shader.GetInstanceID()))
                found = true;
        if (!found) return;

        hostShaders = null;
        replacementByName.Clear();
        checkedMaterials.Clear(); // materials seen before their shader was known to be foreign need a re-check
        rescan = true;
    }

    private void RescanIfNeeded() {
        if (!rescan) return;
        rescan = false;
        foreach (var mat in Resources.FindObjectsOfTypeAll<Material>()) Check(mat);
    }

    private void CheckObject(Object obj) {
        if (obj is Material mat) Check(mat);
        else if (obj is GameObject go) CheckGameObject(go);
    }

    // Reading the references loads them: what Unity would otherwise load lazily, while already rendering.
    private void CheckGameObject(GameObject go) {
        if (!checkedObjects.Add(go.GetInstanceID())) return;
        foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in renderer.sharedMaterials)
                Check(mat);
        // A clip from another sprite collection switches the renderer to that collection's material.
        foreach (var animator in go.GetComponentsInChildren<tk2dSpriteAnimator>(true)) {
            var library = animator.Library;
            if (!library || !checkedObjects.Add(library.GetInstanceID())) continue;
            foreach (var clip in library.clips)
                foreach (var frame in clip.frames)
                    if (frame.spriteCollection && checkedObjects.Add(frame.spriteCollection.GetInstanceID()))
                        foreach (var mat in frame.spriteCollection.materials)
                            Check(mat);
        }
    }

    private void Check(Material? mat) {
        if (!mat || !checkedMaterials.Add(mat!.GetInstanceID())) return;
        var shader = mat.shader;
        if (shader && (IsSilksongCopy(shader) || !shader.isSupported)) FixMaterial(mat, shader.name);
    }

    private bool IsSilksongCopy(Shader shader) =>
        foreignShaders.Contains(shader.GetInstanceID()) ||
        hkShaders.TryGetValue(shader.name, out var hk) && !ReferenceEquals(hk, shader);

    private void FixMaterial(Material mat, string name) {
        var host = HostShader(name);
        if (host) {
            mat.shader = host;
            return;
        }

        var masking = IsMaskingShader(name) || IsDarknessCutout(mat);
        var replacement = masking ? Find("Sprites/Default") : GenericReplacement(name);
        if (!replacement) {
            LogErrorOnce($"shader:{name}", $"no replacement for Silksong shader '{name}' (material '{mat.name}')");
            return;
        }

        mat.shader = replacement;
        if (masking) {
            // Masks only make sense with their stencil/blend logic; with a plain sprite shader they'd be solid shapes.
            mat.color = Color.clear;
            LogInfoOnce($"shader:{name}", $"masking shader '{name}' (material '{mat.name}') has no HK counterpart -> " +
                                          "transparent");
        } else {
            LogInfoOnce($"shader:{name}", $"Silksong shader '{name}' (material '{mat.name}') has no HK counterpart " +
                                          $"-> generic '{replacement!.name}'");
        }
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
        var map = new Dictionary<string, Shader>(hkShaders);
        foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
            if (s && s.isSupported && !IsSilksongCopy(s) && !map.ContainsKey(s.name))
                map[s.name] = s;
        return map;
    }

    private static bool IsMaskingShader(string name) => ContainsAny(name, "Darkness", "Blackout", "Masked", "Mask");

    // The DarknessCameraEffect is only implemented in HK 1.5.12620, not 1.5.78
    private static bool IsDarknessCutout(Material mat) => ContainsAny(mat.name, "Darkness-Cutout");

    private static bool ContainsAny(string s, params string[] parts) {
        foreach (var p in parts)
            if (s.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
}
#endif
