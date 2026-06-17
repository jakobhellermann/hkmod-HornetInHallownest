using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Feasibility spike: can HK's Unity (6000.0.61) load a Silksong-authored AssetBundle (6000.0.50)?
// Everything loaded here is tracked and torn down in Cleanup(), called from HornetPlayerMod.Unload — so a
// `dotnet build` hot-reload (Unload → Initialize) doesn't leak a still-loaded bundle (which would make the next
// LoadFromFile fail with "another AssetBundle with the same files is already loaded").
internal static class BundleSpike {
    private const string Aa =
        "/home/jakob/.local/share/Steam/steamapps/common/Hollow Knight Silksong/Hollow Knight Silksong_Data/StreamingAssets/aa/StandaloneLinux64/";

    // Dependency bundles must be resident BEFORE the prefab is loaded so its cross-bundle PPtrs resolve. The
    // MonoScripts bundle is the key one: the tk2d MonoBehaviours' m_Script points into it, and those MonoScripts
    // carry (assembly="TeamCherry.TK2D", class="tk2dSprite") which binds to HK's identical assembly.
    private static readonly string[] DepBundles = {
        Aa + "94696d22b6ed0a74097d1bd58feb4dce_monoscripts.bundle", // tk2d MonoScripts
        Aa + "herocollections_assets_shared.bundle",                // tk2dSpriteCollectionData + materials
        Aa + "herodynamic_assets_all.bundle",                       // tk2dSpriteAnimation (the clip library)
    };
    private const string PrefabBundlePath = Aa + "heroloading_assets_all.bundle";

    private static readonly List<AssetBundle> bundles = new();
    private static AssetBundle? bundle; // the prefab bundle
    private static readonly List<GameObject> spawned = new();
    private static tk2dSpriteAnimator? puppetAnim;

    // Play a clip on the spawned puppet by (partial, case-insensitive) name — used to prove provenance by triggering
    // Silksong-exclusive moves (Needolin, Bind Silk, …) and for general animation debugging.
    internal static object PlayClip(string? name) {
        if (puppetAnim == null) return new { error = "no puppet spawned" };
        var clips = puppetAnim.Library?.clips;
        if (clips == null) return new { error = "no clip library" };
        if (string.IsNullOrEmpty(name))
            return new { clips = clips.Select(c => c.name).ToArray() };
        var clip = clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains(name!.ToLowerInvariant()));
        if (clip == null) return new { error = $"no clip matching '{name}'" };
        puppetAnim.Play(clip);
        return new { ok = true, playing = clip.name };
    }

    internal static void Run() {
        if (bundles.Count > 0) {
            Log.Info("[BundleSpike] already loaded, skipping");
            return;
        }

        foreach (var dep in DepBundles) {
            var b = AssetBundle.LoadFromFile(dep);
            Log.Info($"[BundleSpike] dep {(b == null ? "FAILED" : "ok")}: {dep}");
            if (b != null) bundles.Add(b);
        }

        Log.Info($"[BundleSpike] LoadFromFile: {PrefabBundlePath}");
        bundle = AssetBundle.LoadFromFile(PrefabBundlePath);
        if (bundle == null) {
            Log.Error("[BundleSpike] LoadFromFile returned null — bundle format likely incompatible");
            return;
        }
        bundles.Add(bundle);

        var names = bundle.GetAllAssetNames();
        Log.Info($"[BundleSpike] loaded OK — {names.Length} assets. Sample:");
        foreach (var n in names.Take(20)) Log.Info($"[BundleSpike]   {n}");

        // Try to load (not instantiate) the Hero_Hornet prefab asset. Missing dependencies (505 of them, in other
        // bundles) will resolve to null PPtrs, but the load itself tells us whether cross-game deserialization works.
        var heroName = names.FirstOrDefault(n => n.ToLowerInvariant().Contains("hero_hornet"));
        if (heroName == null) {
            Log.Info("[BundleSpike] no asset name contains 'hero_hornet'");
            return;
        }

        Log.Info($"[BundleSpike] LoadAsset<GameObject>({heroName})");
        var prefab = bundle.LoadAsset<GameObject>(heroName);
        if (prefab == null) {
            Log.Error("[BundleSpike] LoadAsset returned null");
            return;
        }

        SpawnPuppet(prefab);
    }

    // Component type names kept on the puppet root; everything else (gameplay scripts that would false-bind to HK's
    // same-named classes, FSMs, audio, colliders, attach points) is stripped before the object is ever activated.
    private static readonly string[] KeepComponents =
        { "tk2dSprite", "tk2dSpriteAnimator", "MeshRenderer", "MeshFilter" };

    private static void SpawnPuppet(GameObject prefab) {
        // Instantiate under an INACTIVE parent so no Awake/OnEnable runs until we've stripped the dangerous scripts
        // (HK's real HeroController.Awake would otherwise run on Silksong data → crash).
        var staging = new GameObject("hp_staging");
        staging.SetActive(false);
        var inst = Object.Instantiate(prefab, staging.transform);
        inst.name = "Hornet_Visual";

        // First visual: just the body on the root. Drop the whole effect/slash/audio subtree.
        for (var i = inst.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(inst.transform.GetChild(i).gameObject);

        var stripped = 0;
        foreach (var c in inst.GetComponents<Component>()) {
            if (c == null || c is Transform) continue;
            var name = c.GetType().Name;
            if (KeepComponents.Contains(name)) continue;
            try {
                Object.DestroyImmediate(c);
                stripped++;
            } catch (Exception e) {
                Log.Error($"[BundleSpike] strip {name}: {e.Message}");
            }
        }
        Log.Info($"[BundleSpike] stripped {stripped} components; root now: {string.Join(", ", inst.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name))}");

        // Active follower; reparenting the instance onto it activates the instance (only tk2d Awake runs now).
        var follower = new GameObject("HornetPuppet");
        Object.DontDestroyOnLoad(follower);
        follower.AddComponent<HornetPuppet>();
        inst.transform.SetParent(follower.transform, false);
        inst.transform.localPosition = Vector3.zero; // prefab root carries a baked-in offset; sit it on the follower
        Object.DestroyImmediate(staging);
        spawned.Add(follower);

        var anim = inst.GetComponent<tk2dSpriteAnimator>();
        puppetAnim = anim;
        if (anim == null) {
            Log.Error("[BundleSpike] no tk2dSpriteAnimator on root after strip");
            return;
        }

        var clips = anim.Library != null ? anim.Library.clips : null;
        if (clips == null || clips.Length == 0) {
            Log.Error("[BundleSpike] tk2dSpriteAnimator has no clips/library");
            return;
        }
        Log.Info($"[BundleSpike] {clips.Length} clips. First 30: {string.Join(", ", clips.Take(30).Select(c => c.name))}");

        var idle = clips.FirstOrDefault(c => c.name.ToLowerInvariant().Contains("idle")) ?? clips[0];
        anim.Play(idle);
        Log.Info($"[BundleSpike] playing clip '{idle.name}' — Hornet_Visual spawned");

        // The bundle's shaders don't survive the cross-game load (material shader = Hidden/InternalErrorShader →
        // pink). HK ships "tk2d/BlendVertexColor", so re-point every collection material at it.
        RemapShaders(inst);
    }

    private static void RemapShaders(GameObject inst) {
        var tk2d = Shader.Find("tk2d/BlendVertexColor");
        if (tk2d == null) {
            Log.Error("[BundleSpike] Shader.Find('tk2d/BlendVertexColor') failed");
            return;
        }

        var fixedCount = 0;
        void Fix(Material[]? mats) {
            if (mats == null) return;
            foreach (var m in mats)
                if (m != null && (m.shader == null || !m.shader.isSupported)) {
                    m.shader = tk2d;
                    fixedCount++;
                }
        }

        var sprite = inst.GetComponent<tk2dSprite>();
        var coll = sprite != null ? sprite.Collection : null;
        if (coll != null) {
            Fix(coll.materials);
            Fix(coll.materialInsts);
        }
        Fix(inst.GetComponent<Renderer>()?.sharedMaterials);
        Log.Info($"[BundleSpike] remapped {fixedCount} materials to tk2d/BlendVertexColor");
    }

    internal static void Cleanup() {
        foreach (var go in spawned)
            if (go != null) Object.Destroy(go);
        spawned.Clear();

        foreach (var b in bundles)
            if (b != null) b.Unload(true);
        var n = bundles.Count;
        bundles.Clear();
        bundle = null;
        if (n > 0) Log.Info($"[BundleSpike] {n} bundles unloaded");
    }
}
