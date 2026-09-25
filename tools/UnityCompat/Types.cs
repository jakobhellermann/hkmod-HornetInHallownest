// Unity 6 types with no Unity 2020.2 counterpart. Namespaces deliberately mirror Unity's so the retargeted
// references read naturally in a decompiler; only the assembly identity differs.
// ReSharper disable CheckNamespace
using System;
using System.Collections.Generic;

namespace UnityEngine {
    // Unity 2022.2 Object.FindObjectsByType selectors.
    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None, InstanceID }

    // Unity 2022.2 Resolution.refreshRateRatio. 2020.2 exposes an int `refreshRate` instead.
    public struct RefreshRate : IEquatable<RefreshRate> {
        public uint numerator;
        public uint denominator;
        public double value => denominator == 0 ? 0 : (double)numerator / denominator;
        public bool Equals(RefreshRate other) => numerator == other.numerator && denominator == other.denominator;
        public override bool Equals(object obj) => obj is RefreshRate r && Equals(r);
        public override int GetHashCode() => (int)(numerator ^ denominator);
        public override string ToString() => value.ToString("0.##");
    }
}

namespace UnityEngine.Pool {
    // Unity 2021 pooling package. Hornet's code pools effects/projectiles; a straightforward Stack-backed
    // implementation preserves the observable behaviour (Get/Release/Clear + counters).
    public interface IObjectPool<T> where T : class {
        int CountInactive { get; }
        T Get();
        void Release(T element);
        void Clear();
    }

    public class ObjectPool<T> : IObjectPool<T>, IDisposable where T : class {
        private readonly Stack<T> stack = new Stack<T>();
        private readonly Func<T> createFunc;
        private readonly Action<T> actionOnGet;
        private readonly Action<T> actionOnRelease;
        private readonly Action<T> actionOnDestroy;
        private readonly int maxSize;
        private readonly bool collectionCheck;

        public ObjectPool(Func<T> createFunc, Action<T> actionOnGet = null, Action<T> actionOnRelease = null,
                          Action<T> actionOnDestroy = null, bool collectionCheck = true,
                          int defaultCapacity = 10, int maxSize = 10000) {
            this.createFunc = createFunc ?? throw new ArgumentNullException(nameof(createFunc));
            this.actionOnGet = actionOnGet;
            this.actionOnRelease = actionOnRelease;
            this.actionOnDestroy = actionOnDestroy;
            this.collectionCheck = collectionCheck;
            this.maxSize = maxSize < 1 ? 1 : maxSize;
        }

        public int CountInactive => stack.Count;
        public int CountActive { get; private set; }
        public int CountAll => CountInactive + CountActive;

        public T Get() {
            var item = stack.Count == 0 ? createFunc() : stack.Pop();
            CountActive++;
            actionOnGet?.Invoke(item);
            return item;
        }

        public void Release(T element) {
            if (collectionCheck && stack.Count > 0 && stack.Contains(element)) return;
            CountActive--;
            actionOnRelease?.Invoke(element);
            if (stack.Count < maxSize) stack.Push(element);
            else actionOnDestroy?.Invoke(element);
        }

        public void Clear() {
            if (actionOnDestroy != null)
                foreach (var item in stack) actionOnDestroy(item);
            stack.Clear();
            CountActive = 0;
        }

        public void Dispose() => Clear();
    }

    // Unity's LinkedPool has different internals but the same contract; back it with the same store.
    public class LinkedPool<T> : IObjectPool<T>, IDisposable where T : class {
        private readonly ObjectPool<T> inner;
        public LinkedPool(Func<T> createFunc, Action<T> actionOnGet = null, Action<T> actionOnRelease = null,
                          Action<T> actionOnDestroy = null, bool collectionCheck = true, int maxSize = 10000)
            => inner = new ObjectPool<T>(createFunc, actionOnGet, actionOnRelease, actionOnDestroy, collectionCheck, 10, maxSize);
        public int CountInactive => inner.CountInactive;
        public T Get() => inner.Get();
        public void Release(T element) => inner.Release(element);
        public void Clear() => inner.Clear();
        public void Dispose() => inner.Dispose();
    }
}

namespace UnityEngine {
    // Unity 2021's AssetBundle.UnloadAsync return type. 2020.2 unloads synchronously, so instances handed out by
    // the shim represent already-completed work.
    public class AssetBundleUnloadOperation : CustomYieldInstruction {
        public override bool keepWaiting => false;
        // ResourceManager awaits the unload synchronously; the work already happened in the shim.
        public void WaitForCompletion() { }
    }
}

namespace UnityEngine.Video {
    // Unity 2022.1 VideoPlayer.timeUpdateMode.
    public enum VideoTimeUpdateMode { DSPTime, GameTime, UnscaledGameTime }
}

namespace UnityEngine.UIElements {
    // Silksong drives its debug/dev UI through UIElements (Unity 2021+). Hornet never exercises it inside
    // Hallownest, so these are presence-only stubs: enough for type resolution and member binding at JIT time,
    // with no behaviour behind them.
    public class VisualElement { }
    public class VisualTreeAsset : ScriptableObject { }
    public class PanelSettings : ScriptableObject {
        public float scale { get; set; }
    }
    public class UIDocument : MonoBehaviour {
        public PanelSettings panelSettings { get; set; }
        public VisualTreeAsset visualTreeAsset { get; set; }
        public VisualElement rootVisualElement { get; } = new VisualElement();
    }
}

// Silksong-era tk2d addition with no counterpart in Hollow Knight 1.5.78's (older) in-Assembly-CSharp tk2d, so the
// TeamCherry.TK2D facade cannot forward it. Silksong's Tk2dGlobalEventListener implements IListener and the engine
// broadcasts through here; a faithful listener registry keeps that wiring intact.
public static class Tk2dGlobalEvents {
    public interface IListener {
        void ColliderUpdated(UnityEngine.GameObject gameObject);
        void TilemapChunkCreated(UnityEngine.Transform grandChild);
        bool IsFrozenCameraRendering();
    }

    private static readonly System.Collections.Generic.List<IListener> listeners = new System.Collections.Generic.List<IListener>();

    public static void AddListener(IListener listener) {
        if (listener != null && !listeners.Contains(listener)) listeners.Add(listener);
    }

    public static void RemoveListener(IListener listener) => listeners.Remove(listener);

    public static void ColliderUpdated(UnityEngine.GameObject gameObject) {
        for (var i = 0; i < listeners.Count; i++) listeners[i].ColliderUpdated(gameObject);
    }

    public static void TilemapChunkCreated(UnityEngine.Transform grandChild) {
        for (var i = 0; i < listeners.Count; i++) listeners[i].TilemapChunkCreated(grandChild);
    }

    public static bool IsFrozenCameraRendering() {
        for (var i = 0; i < listeners.Count; i++)
            if (listeners[i].IsFrozenCameraRendering()) return true;
        return false;
    }
}

namespace UnityEngine.Compat {
    // Silksong's tk2dSpriteAnimator declares these as types nested inside the animator. HK 1.5.78's tk2d has no
    // such members, and nested types cannot be added to a forwarded type, so UnityDowngrader un-nests references
    // to them onto these top-level equivalents. Signatures are copied exactly.
    public delegate void Tk2dAnimationCompletedDelegate(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip clip);
    public delegate void Tk2dAnimationEventDelegate(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip clip, int frame);
    public delegate void Tk2dAnimationChangedDelegate(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip previousClip, tk2dSpriteAnimationClip newClip);
}

namespace Galaxy.Api {
    // Silksong links a newer GOG Galaxy SDK than Hollow Knight 1.5.78 ships: its GalaxyInstance has ShutdownEx
    // (taking ShutdownParams) where HK's only has Shutdown(). Reached once, at application shutdown.
    public class ShutdownParams {
        public ShutdownParams() { }
        public ShutdownParams(bool preserveStaticData) => this.preserveStaticData = preserveStaticData;
        public bool preserveStaticData { get; set; }
    }
}
