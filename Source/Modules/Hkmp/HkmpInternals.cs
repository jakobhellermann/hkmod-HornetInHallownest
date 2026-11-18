#if HK1578
using System;
using System.Reflection;
using Hkmp.Api.Client;
using HornetInHallownest.Util;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetInHallownest.Modules.Hkmp;

// Unfortunately, the public API of HKMP doesn't expose enough for us to sync hornet, like passing through unknown animation clips.
// This class encapsulates the ugly hacks and reflection internals.
internal sealed class HkmpInternals : IDisposable {
    private readonly IClientApi api;
    private readonly Type clipEnum;
    private readonly MethodInfo sendAnimationEntry;
    private readonly MethodInfo currentKnightClip;

    // HKMP's own On.HeroController.Update handlers that publish position, facing, scene entry and map position.
    private readonly (object target, MethodInfo method)[] heroUpdateHandlers;
    private readonly object?[] heroUpdateArgs = new object?[2];

    private readonly Hook sendHook;
    private readonly Hook receiveHook;
    private readonly Hook skinHook;

    private Vector3 publishedPosition;
    private float publishedScaleX;
    private bool sending;

    // animationEntryReceived: (playerId, id, frame, payload) -> whether the entry was handled and HKMP must not apply it to the Knight.
    // knightAnimationDisabled: whether HKMP's own animation updates for the (inactive) Knight are dropped.
    // skipHkmpSkin: remote players shown as Hornet, since HKMP writes skins into the current library's material.
    internal HkmpInternals(IClientApi api, Func<ushort, int, int, bool[]?, bool> animationEntryReceived,
        Func<bool> knightAnimationDisabled, Func<GameObject, bool> skipHkmpSkin) {
        this.api = api;

        var asm = api.GetType().Assembly;
        var animationManager = asm.GetType("Hkmp.Animation.AnimationManager", true);
        clipEnum = asm.GetType("Hkmp.Animation.AnimationClip", true);
        sendAnimationEntry = asm.GetType("Hkmp.Networking.Client.ClientUpdateManager", true)
            .GetMethodInfo("UpdatePlayerAnimation")!;
        currentKnightClip = animationManager.GetMethodInfo("GetCurrentAnimationClip")!;

        var clientManager = api.ClientManager;

        Type?[] heroUpdateParams = [null, typeof(HeroController)];
        heroUpdateHandlers = [
            (clientManager, clientManager.GetType().GetMethodInfo("OnPlayerUpdate", heroUpdateParams)!),
            (clientManager.MapManager,
                clientManager.MapManager.GetType().GetMethodInfo("HeroControllerOnUpdate", heroUpdateParams)!),
        ];
        // The handlers start with orig(self), the real HeroController.Update: hand them a no-op so the Knight doesn't run it.
        heroUpdateArgs[0] = Delegate.CreateDelegate(heroUpdateHandlers[0].method.GetParameters()[0].ParameterType,
            typeof(HkmpInternals).GetMethodInfo(nameof(NoHeroUpdate))!);

        sendHook = new(sendAnimationEntry,
            (SendAnimationOrig orig, object self, int clip, int frame, bool[]? effectInfo) => {
                if (sending || !knightAnimationDisabled()) orig(self, clip, frame, effectInfo);
            });
        receiveHook = new(animationManager.GetMethodInfo("OnPlayerAnimationUpdate")!,
            (ReceiveAnimationOrig orig, object self, ushort id, int clipId, int frame, bool[]? effectInfo) => {
                if (!animationEntryReceived(id, clipId, frame, effectInfo)) orig(self, id, clipId, frame, effectInfo);
            });
        skinHook = new(
            asm.GetType("Hkmp.Game.Client.Skin.SkinManager", true).GetMethodInfo("UpdatePlayerSkin")!,
            (UpdateSkinOrig orig, object self, GameObject playerObject, byte skinId) => {
                if (!skipHkmpSkin(playerObject)) orig(self, playerObject, skinId);
            });
    }

    internal bool Connected => api.NetClient.IsConnected;

    // Run HKMP's local-player publishing as if the Knight stood where the given hero is.
    internal void PublishLocalHero(Transform hero) {
        var position = hero.position;
        float scaleX = hero.localScale.x;
        if (position == publishedPosition && scaleX == publishedScaleX) return;
        publishedPosition = position;
        publishedScaleX = scaleX;

        var knight = HeroController.UnsafeInstance;
        var t = knight.transform;
        var knightPosition = t.position;
        var knightScale = t.localScale;
        t.position = position;
        t.localScale = new(Mathf.Sign(scaleX) * Mathf.Abs(knightScale.x), knightScale.y, knightScale.z);
        try {
            heroUpdateArgs[1] = knight;
            foreach ((object? target, var method) in heroUpdateHandlers) method.Invoke(target, heroUpdateArgs);
        } finally {
            t.position = knightPosition;
            t.localScale = knightScale;
        }
    }

    // HKMP has no free-form channel without a server addon, so our data is sent via player animation entries,
    // the server relays id, frame and payload unchecked.
    internal void SendAnimationEntry(ushort id, int frame, bool[]? payload = null) =>
        Send(Enum.ToObject(clipEnum, id), (byte)Mathf.Min(frame, byte.MaxValue), payload);

    // The Knight's current clip as HKMP itself would report it.
    internal void SendKnightClip() => Send(currentKnightClip.Invoke(null, null), 0, null);

    private void Send(object clip, int frame, bool[]? payload) {
        object? updateManager = api.NetClient.GetPropertyValue<object>("UpdateManager");
        sending = true;
        try {
            sendAnimationEntry.Invoke(updateManager, [clip, frame, payload]);
        } finally {
            sending = false;
        }
    }

    public void Dispose() {
        sendHook.Dispose();
        receiveHook.Dispose();
        skinHook.Dispose();
    }

    private static void NoHeroUpdate(HeroController self) { }

    // The clip is HKMP's internal AnimationClip enum.
    private delegate void SendAnimationOrig(object self, int clip, int frame, bool[]? effectInfo);

    private delegate void ReceiveAnimationOrig(object self, ushort id, int clipId, int frame, bool[]? effectInfo);

    private delegate void UpdateSkinOrig(object self, GameObject playerObject, byte skinId);
}
#endif
