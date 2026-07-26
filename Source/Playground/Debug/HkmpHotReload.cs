#if HK1578
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using Hkmp.Api.Client;
using HornetInHallownest.Util;
using Modding;
using MonoMod.RuntimeDetour;

namespace HornetInHallownest.Playground;

// HKMP accepts addons only once at startup. On a hot-reload, swap the reloaded addon in for its stale copy instead.
internal static class HkmpHotReload {
    private static IDisposable? hook;

    internal static void Install() {
        if (ModHooks.GetMod("HKMP") != null) InstallHook();
    }

    internal static void Cleanup() {
        hook?.Dispose();
        hook = null;
    }

    // Kept out of Install: compiling a method that touches HKMP types loads HKMP.dll.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InstallHook() {
        var manager = typeof(ClientAddon).Assembly.GetType("Hkmp.Api.Client.ClientAddonManager", true);
        hook = new Hook(manager.GetMethodInfo("RegisterAddon")!,
            (Action<ClientAddon> orig, ClientAddon addon) => {
                if (manager.GetFieldValue<bool>("_hasLoaded")) Replace(manager, addon);
                else orig(addon);
            });
    }

    private static void Replace(Type manager, ClientAddon addon) {
        var registered = manager.GetFieldValue<IList>("RegisteredAddons")!;
        var index = IndexOfSameType(registered, addon);
        if (index < 0) throw new InvalidOperationException($"no stale {addon.GetType().FullName} to replace");
        var api = registered[index].GetFieldValue<IClientApi>("_clientApi")
                  ?? throw new InvalidOperationException("stale HKMP addon was never initialized, restart the game");
        registered[index] = addon;

        // Absent when the stale addon's Initialize threw.
        var loaded = api.ClientManager.GetFieldValue<object>("_addonManager")!.GetFieldValue<IList>("_addons")!;
        var loadedIndex = IndexOfSameType(loaded, addon);
        if (loadedIndex < 0) loaded.Add(addon);
        else loaded[loadedIndex] = addon;

        typeof(ClientAddon).GetMethodInfo("InternalInitialize", [typeof(IClientApi)])!.Invoke(addon, [api]);
        Log.Info($"[HkmpHotReload] swapped in reloaded {addon.GetType().FullName}");
    }

    // Same full name, different (reloaded) assembly.
    private static int IndexOfSameType(IList addons, ClientAddon addon) {
        for (var i = 0; i < addons.Count; i++)
            if (addons[i]!.GetType().FullName == addon.GetType().FullName)
                return i;
        return -1;
    }
}
#endif
