// Static stand-ins for Unity 6 members Unity 2020.2 lacks. `T Foo::Bar(A)` becomes `static T Bar(Foo, A)` (struct
// receivers by ref), so UnityDowngrader only swaps the call target.
// ReSharper disable CheckNamespace InconsistentNaming
using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace UnityEngine.Compat;

public static class HornetCompat {

    // UnityEngine.Object / Component lookups (Unity 2022.2)

    public static T FindAnyObjectByType<T>() where T : Object => Object.FindObjectOfType<T>();

    public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object
        => Object.FindObjectsOfType<T>();

    public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode sortMode) where T : Object
        => Object.FindObjectsOfType<T>(inactive == FindObjectsInactive.Include);

    // 2020.2's overload has no includeInactive parameter; walk the parents ourselves when it is requested.
    public static T GetComponentInParent<T>(Component self, bool includeInactive) where T : Component {
        if (!includeInactive) return self.GetComponentInParent<T>();
        for (var t = self.transform; t != null; t = t.parent) {
            var found = t.GetComponent<T>();
            if (found != null) return found;
        }
        return null;
    }

    public static T GetComponentInParent<T>(GameObject self, bool includeInactive) where T : Component
        => GetComponentInParent<T>(self.transform, includeInactive);

    // MonoBehaviour (Unity 6)

    // Player code only runs once Awake was called.
    public static bool MonoBehaviour_get_didAwake(MonoBehaviour self) => true;

    // AssetBundle / UnityWebRequest (Unity 2021+, used by Addressables' ResourceManager)

    public static AssetBundleUnloadOperation AssetBundle_UnloadAsync(AssetBundle self, bool unloadAllLoadedObjects) {
        // 2020.2 only has the blocking Unload.
        var stream = UnityFsBundles.TakeStream(self);
        self.Unload(unloadAllLoadedObjects);
        stream?.Dispose();
        return new AssetBundleUnloadOperation();
    }

    // Always redirected: Unity 6 bundles need translating (see UnityFsBundles).
    public static AssetBundleCreateRequest AssetBundle_LoadFromFileAsync(string path) =>
        UnityFsBundles.LoadFromFileAsync(path, 0, 0);

    public static AssetBundleCreateRequest AssetBundle_LoadFromFileAsync(string path, uint crc) =>
        UnityFsBundles.LoadFromFileAsync(path, crc, 0);

    public static AssetBundleCreateRequest AssetBundle_LoadFromFileAsync(string path, uint crc, ulong offset) =>
        UnityFsBundles.LoadFromFileAsync(path, crc, offset);

    public static bool DownloadHandlerAssetBundle_get_autoLoadAssetBundle(DownloadHandlerAssetBundle self) => true;
    public static void DownloadHandlerAssetBundle_set_autoLoadAssetBundle(DownloadHandlerAssetBundle self, bool value) { }
    public static bool DownloadHandlerAssetBundle_get_isDownloadComplete(DownloadHandlerAssetBundle self) => self.isDone;

    // tk2dSpriteAnimator events (Silksong's tk2d). Completed/EventTriggered are bridged onto HK's equivalent Action
    // fields; AnimationChanged and FrameChanged have no HK equivalent and never fire.

    private static readonly Dictionary<(object, Delegate), Delegate> bridged = new Dictionary<(object, Delegate), Delegate>();

    public static void tk2dSpriteAnimator_add_AnimationCompletedEvent(tk2dSpriteAnimator self, Tk2dAnimationCompletedDelegate handler) {
        if (handler == null) return;
        Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip> wrapper = (a, c) => handler(a, c);
        bridged[(self, handler)] = wrapper;
        self.AnimationCompleted += wrapper;
    }

    public static void tk2dSpriteAnimator_remove_AnimationCompletedEvent(tk2dSpriteAnimator self, Tk2dAnimationCompletedDelegate handler) {
        if (handler == null || !bridged.TryGetValue((self, handler), out var wrapper)) return;
        self.AnimationCompleted -= (Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip>)wrapper;
        bridged.Remove((self, handler));
    }

    public static void tk2dSpriteAnimator_add_AnimationEventTriggeredEvent(tk2dSpriteAnimator self, Tk2dAnimationEventDelegate handler) {
        if (handler == null) return;
        Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int> wrapper = (a, c, f) => handler(a, c, f);
        bridged[(self, handler)] = wrapper;
        self.AnimationEventTriggered += wrapper;
    }

    public static void tk2dSpriteAnimator_remove_AnimationEventTriggeredEvent(tk2dSpriteAnimator self, Tk2dAnimationEventDelegate handler) {
        if (handler == null || !bridged.TryGetValue((self, handler), out var wrapper)) return;
        self.AnimationEventTriggered -= (Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int>)wrapper;
        bridged.Remove((self, handler));
    }

    public static void tk2dSpriteAnimator_add_AnimationChanged(tk2dSpriteAnimator self, Tk2dAnimationChangedDelegate handler) { }
    public static void tk2dSpriteAnimator_remove_AnimationChanged(tk2dSpriteAnimator self, Tk2dAnimationChangedDelegate handler) { }

    public static Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int, int> tk2dSpriteAnimator_get_FrameChanged(tk2dSpriteAnimator self) => null;
    public static void tk2dSpriteAnimator_set_FrameChanged(tk2dSpriteAnimator self, Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int, int> value) { }

    // tk2dSprite material keywords (Silksong's tk2d); HK's tk2d has none, so keyword-driven variants don't apply.

    public static bool tk2dSprite_get_HasKeywordsDefined(tk2dSprite self) => false;
    public static void tk2dSprite_EnableKeyword(tk2dSprite self, string keyword) { }
    public static void tk2dSprite_DisableKeyword(tk2dSprite self, string keyword) { }
    public static void tk2dSprite_MoveKeywords(tk2dSprite self, tk2dSprite target) { }
    public static void tk2dSprite_SetFloat(tk2dSprite self, int nameId, float value) { }

    // Mathf (Unity 2022.2)

    public static float PerlinNoise1D(float x) => Mathf.PerlinNoise(x, 0f);

    // Screen / Resolution (Unity 2022.2 refresh-rate-as-ratio)

    public static RefreshRate Resolution_get_refreshRateRatio(ref Resolution self)
        => new RefreshRate { numerator = (uint)self.refreshRate, denominator = 1 };

    public static void Resolution_set_refreshRateRatio(ref Resolution self, RefreshRate rate)
        => self.refreshRate = (int)Math.Round(rate.value);

    public static void SetResolution(int width, int height, FullScreenMode mode, RefreshRate rate)
        => Screen.SetResolution(width, height, mode, (int)Math.Round(rate.value));

    // Collider2D layer overrides (Unity 2022.2); 2020.2's physics has none, so overrides are dropped.

    public static LayerMask Collider2D_get_excludeLayers(Collider2D self) => default;
    public static void Collider2D_set_excludeLayers(Collider2D self, LayerMask value) { }
    public static LayerMask Collider2D_get_includeLayers(Collider2D self) => default;
    public static void Collider2D_set_includeLayers(Collider2D self, LayerMask value) { }
    public static LayerMask Collider2D_get_callbackLayers(Collider2D self) => ~0;
    public static void Collider2D_set_callbackLayers(Collider2D self, LayerMask value) { }
    public static LayerMask Collider2D_get_contactCaptureLayers(Collider2D self) => ~0;
    public static void Collider2D_set_contactCaptureLayers(Collider2D self, LayerMask value) { }
    public static LayerMask Collider2D_get_forceSendLayers(Collider2D self) => ~0;
    public static void Collider2D_set_forceSendLayers(Collider2D self, LayerMask value) { }
    public static LayerMask Collider2D_get_forceReceiveLayers(Collider2D self) => ~0;
    public static void Collider2D_set_forceReceiveLayers(Collider2D self, LayerMask value) { }
    public static int Collider2D_get_layerOverridePriority(Collider2D self) => 0;
    public static void Collider2D_set_layerOverridePriority(Collider2D self, int value) { }

    // VideoPlayer (Unity 2022.1)

    public static bool VideoPlayer_get_canSetTimeUpdateMode(VideoPlayer self) => false;
    public static VideoTimeUpdateMode VideoPlayer_get_timeUpdateMode(VideoPlayer self) => VideoTimeUpdateMode.GameTime;
    public static void VideoPlayer_set_timeUpdateMode(VideoPlayer self, VideoTimeUpdateMode mode) { }
}
