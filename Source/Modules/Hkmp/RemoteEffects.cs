#if HK1578
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Modules.Hkmp;

// Visual-only copies of Hornet's effects for one remote player, cloned from the Hero_Hornet prefab.
internal sealed class RemoteEffects {
    private readonly GameObject root;
    private readonly GameObject worldRoot;
    private readonly Transform[] clones;
    private readonly tk2dSpriteAnimator?[] animators;

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
        for (var i = 0; i < sources.Length; i++) {
            if (!HornetEffects.IsTopLevel(i)) continue;
            var clone = Object.Instantiate(sources[i].gameObject, root.transform, false);
            clone.name = sources[i].name;
            StripToVisuals(clone);
        }

        clones = HornetEffects.Resolve(root.transform);
        animators = new tk2dSpriteAnimator?[clones.Length];
        for (var i = 0; i < clones.Length; i++) {
            clones[i].gameObject.SetActive(false);
            animators[i] = clones[i].GetComponent<tk2dSpriteAnimator>();
        }

        root.SetActive(true);
    }

    internal tk2dSpriteAnimator? Animator(int entry) => animators[entry];

    internal void Apply(int entry, EffectState state) {
        var clone = clones[entry];
        if (HornetEffects.IsTopLevel(entry)) {
            var parent = state.Detached ? worldRoot.transform : root.transform;
            if (clone.parent != parent) clone.SetParent(parent, false);
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
        // Behaviours first: a [RequireComponent] on them blocks removing the colliders and rigidbodies they need.
        foreach (var c in clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (!IsVisual(c))
                Object.DestroyImmediate(c);
        foreach (var c in clone.GetComponentsInChildren<Component>(true))
            if (!IsVisual(c))
                Object.DestroyImmediate(c);
    }

    private static bool IsVisual(Component c) => c is Transform or MeshFilter or Renderer or tk2dBaseSprite
        or tk2dSpriteAnimator or UnityEngine.Animator or ParticleSystem || c.GetType().Name == "BasicSpriteAnimator";
}
#endif
