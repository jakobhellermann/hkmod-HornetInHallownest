extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using Log = HornetPlayer.Playground.Log;

namespace HornetPlayer.HornetInHallownest.Core;

public abstract class ModuleBase {
    private const BindingFlags AllMethods =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private readonly List<IDisposable> disposables = [];

    public abstract string Id { get; }

    public abstract void Initialize();

    // Per-frame hook, called only while Hornet is the active hero.
    public virtual void HornetActiveUpdate(Silksong::HeroController hero) {
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

    protected void LogInfo(object? msg) => Log.Info($"[{Id}] {msg}");
    protected void LogDebug(object? msg) => Log.Debug($"[{Id}] {msg}");
    protected void LogError(object? msg) => Log.Error($"[{Id}] {msg}");
    protected void LogInfoOnce(string key, object? msg) => Log.InfoOnce(key, $"[{Id}] {msg}");
    protected void LogDebugOnce(string key, object? msg) => Log.DebugOnce(key, $"[{Id}] {msg}");
    protected void LogErrorOnce(string key, object? msg) => Log.ErrorOnce(key, $"[{Id}] {msg}");

    protected Hook Detour<TDelegate>(Type type, string method, TDelegate hook, params Type[] paramTypes)
        where TDelegate : Delegate {
        var mi = paramTypes.Length > 0
            ? type.GetMethod(method, AllMethods, null, paramTypes, null)
            : type.GetMethod(method, AllMethods);
        if (mi == null) throw new MissingMethodException(type.FullName, method);
        return Track(new Hook(mi, hook));
    }
}
