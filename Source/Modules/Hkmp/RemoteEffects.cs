#if HK1578
extern alias Silksong;
using HornetInHallownest.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Modules.Hkmp;

// Visual-only copies of Hornet's effects for one remote player, cloned from the Hero_Hornet prefab.
internal sealed class RemoteEffects {
    private readonly GameObject root;
    private readonly GameObject worldRoot;
    private readonly EffectObject[] clones;
    private readonly Transform?[] attachedParents;

    internal RemoteEffects(Transform playerObject, Transform prefab) {
        // Inactive while cloning: the copies' gameplay components are stripped before any of them wake up.
        root = new GameObject("Hornet Effects");
        root.SetActive(false);
        root.transform.SetParent(playerObject, false);
        // Detached effects (the thrown harpoon needle) keep their world orientation; under the PlayerObject, which HKMP
        // flips with scale.x = -1 when facing left, they would render mirrored.
        worldRoot = new GameObject("Hornet Effects (world)");
        Object.DontDestroyOnLoad(worldRoot);

        var sources = HornetEffects.Resolve(prefab);
        attachedParents = new Transform?[sources.Length];
        for (var i = 0; i < sources.Length; i++) {
            if (!HornetEffects.IsTopLevel(i)) continue;
            // Recreate the path prefix ("Attacks" for "Attacks/Default") so Resolve finds the copies like the originals.
            var segments = HornetEffects.Entries[i].Split('/');
            var parent = root.transform;
            for (var k = 0; k < segments.Length - 1; k++) {
                var holder = parent.Find(segments[k]);
                if (!holder) {
                    holder = new GameObject(segments[k]).transform;
                    holder.SetParent(parent, false);
                }

                parent = holder!;
            }

            var source = sources[i].Transform;
            var clone = Object.Instantiate(source.gameObject, parent, false);
            clone.name = source.name;
            StripToVisuals(clone);
            attachedParents[i] = parent;
        }

        clones = HornetEffects.Resolve(root.transform);
        foreach (var clone in clones) clone.Transform.gameObject.SetActive(false);

        root.SetActive(true);
    }

    internal tk2dSpriteAnimator? Animator(int entry) => clones[entry].Animator;

    internal void Apply(int entry, EffectState state) {
        var clone = clones[entry];
        if (HornetEffects.IsTopLevel(entry)) {
            var parent = state.Detached ? worldRoot.transform : attachedParents[entry]!;
            if (clone.Transform.parent != parent) clone.Transform.SetParent(parent, false);
        }

        state.Apply(clone, root.transform.parent);
    }

    internal void Destroy() {
        Object.Destroy(root);
        Object.Destroy(worldRoot);
    }

    // Keeps what draws and animates by itself; removes colliders, damage, FSMs and everything else gameplay.
    private static void StripToVisuals(GameObject clone) {
        foreach (var animator in clone.GetComponentsInChildren<UnityEngine.Animator>(true))
            animator.fireEvents = false; // their receivers are stripped
        // As without an imbuement: stripped, these would leave the element variants in the prefab's state.
        foreach (var imbuement in clone.GetComponentsInChildren<Silksong::HeroNailImbuementEffect>(true))
        foreach (var variant in imbuement.activateObjects)
            if (variant)
                variant.SetActive(false);
        // Dependents before what they require (behaviours and audio filters on audio sources, colliders, rigidbodies).
        foreach (var c in clone.GetComponentsInChildren<Component>(true))
            if (!IsVisual(c) && c is not (Collider2D or Rigidbody2D or AudioSource))
                Object.DestroyImmediate(c);
        foreach (var c in clone.GetComponentsInChildren<Collider2D>(true)) Object.DestroyImmediate(c);
        foreach (var c in clone.GetComponentsInChildren<Component>(true))
            if (!IsVisual(c))
                Object.DestroyImmediate(c);
    }

    private static bool IsVisual(Component c) => c is Transform or MeshFilter or Renderer or tk2dBaseSprite
        or tk2dSpriteAnimator or UnityEngine.Animator or ParticleSystem || c.GetType().Name == "BasicSpriteAnimator";
}
#endif
