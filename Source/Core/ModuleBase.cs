extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Log = HornetInHallownest.Util.Log;

namespace HornetInHallownest.Core;

public abstract class ModuleBase {
    private const BindingFlags AllMethods =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static MonoBehaviour Runtime = null!;

    private readonly List<IDisposable> disposables = [];

    internal static bool Paused => Time.timeScale == 0f;

    // Pass as Detour's paramTypes to hook the parameterless overload of an overloaded method.
    protected static readonly Type[] NoParams = new Type[0];

    public abstract string Id { get; }

    // Whether HornetActiveUpdate should run while the game is paused
    public virtual bool RunWhilePaused => false;

    public abstract void Initialize();

    // Per-frame hook, called only while Hornet is the active hero.
    public virtual void HornetActiveUpdate(Silksong::HeroController hero) {
    }

    // Called when switching between hornet and knight
    public virtual void HornetToggled(bool active) {
    }

    public void Deinitialize() {
        OnDeinitialize();
        for (var i = disposables.Count - 1; i >= 0; i--)
            try {
                disposables[i].Dispose();
            } catch (Exception e) {
                Log.Error($"[{Id}] dispose: {e}");
            }

        disposables.Clear();
    }

    protected virtual void OnDeinitialize() {
    }

    protected T Track<T>(T disposable) where T : IDisposable {
        disposables.Add(disposable);
        return disposable;
    }

    protected Coroutine StartCoroutine(IEnumerator routine) {
        return Runtime.StartCoroutine(routine);
    }

    protected void LogInfo(object? msg) => Log.Info($"[{Id}] {msg}");
    protected void LogDebug(object? msg) => Log.Debug($"[{Id}] {msg}");
    protected void LogError(object? msg) => Log.Error($"[{Id}] {msg}");
    protected void LogInfoOnce(string key, object? msg) => Log.InfoOnce(key, $"[{Id}] {msg}");
    protected void LogDebugOnce(string key, object? msg) => Log.DebugOnce(key, $"[{Id}] {msg}");
    protected void LogErrorOnce(string key, object? msg) => Log.ErrorOnce(key, $"[{Id}] {msg}");

    protected Hook Detour<TDelegate>(Type type, string method, TDelegate hook, params Type[] paramTypes)
        where TDelegate : Delegate {
        // NoParams (by reference) selects the parameterless overload explicitly; an empty params array means "the only
        // method with this name". (Not Type.EmptyTypes: that is the same shared empty array `params` passes.)
        var mi = paramTypes.Length > 0 || ReferenceEquals(paramTypes, NoParams)
            ? type.GetMethod(method, AllMethods, null, paramTypes, null)
            : type.GetMethod(method, AllMethods);
        if (mi == null) throw new MissingMethodException(type.FullName, method);
        return Track(new Hook(mi, hook));
    }

    // Detour that may be unavailable on this Unity version (e.g. a method that is a native internal call on Unity 2020,
    // which MonoMod can't hook): logs and returns null instead of failing the whole module.
    protected Hook? TryDetour<TDelegate>(Type type, string method, TDelegate hook, params Type[] paramTypes)
        where TDelegate : Delegate {
        try {
            return Detour(type, method, hook, paramTypes);
        } catch (Exception e) when (e is NotSupportedException or MissingMethodException) {
            LogDebug($"detour {type.Name}.{method} unavailable here ({e.Message}); skipped");
            return null;
        }
    }
}
