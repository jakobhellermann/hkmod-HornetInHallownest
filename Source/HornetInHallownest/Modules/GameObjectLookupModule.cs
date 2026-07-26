using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using UnityEngine;

namespace HornetPlayer.HornetInHallownest.Modules;

// Mapping for unity lookups (tag, GetComponent, Find variants)
public sealed class GameObjectLookupModule : ModuleBase {
    private const string RecoilerTag = "Recoiler";

    public override string Id => "unity-lookup";

    public override void Initialize() {
        Detour(typeof(GameObject), "CompareTag", OnCompareTag, typeof(string));
        Detour(typeof(GameObject), "GetComponent", OnGetComponent, typeof(string));
        Detour(typeof(GameObject), "Find", OnFind, typeof(string));
        Detour(typeof(GameObject), "FindGameObjectWithTag", OnFindWithTag, typeof(string));
    }

    private static bool OnCompareTag(Func<GameObject, string, bool> orig, GameObject self, string tag) {
        // Unknown to hollow knight. Always true seems to work well enough for now.
        if (tag == RecoilerTag) {
            return true;
        }

        return orig(self, tag);
    }

    // A string GetComponent resolves to the HK type only, so it misses Hornet's Silksong component. Fall back to a
    // by-name scan of the object's own components.
    private static Component? OnGetComponent(Func<GameObject, string, Component> orig, GameObject self, string name) {
        var c = orig(self, name);
        if (c != null || string.IsNullOrEmpty(name)) return c;
        foreach (var comp in self.GetComponents<Component>()) {
            if (!comp) continue; // missing-script component
            for (var ty = comp.GetType(); ty != null && ty != typeof(object); ty = ty.BaseType)
                if (ty.Name == name || ty.FullName == name)
                    return comp;
        }

        return null;
    }

    private GameObject? OnFind(Func<string, GameObject> orig, string name) {
        if (SilksongContext.Active) return Intercept("Find", name, ResolveName(name));
        var r = orig(name);
        LogDebugOnce($"find|Find|{name}", $"Find('{name}') -> {(r ? "'" + r.name + "'" : "null")}");
        return r;
    }

    private GameObject? OnFindWithTag(Func<string, GameObject> orig, string tag) {
        // Both Knight and Hornet carry "Player"
        if (tag == "Player" && HeroSwitch.HornetActive && HeroSwitch.ActiveHeroGameObject is { } hero) return hero;
        
        if (SilksongContext.Active) return Intercept("FindWithTag", tag, ResolveTag(tag));
        
        try {
            var r = orig(tag);
            LogDebugOnce($"find|FindWithTag|{tag}", $"FindWithTag('{tag}') -> {(r ? "'" + r.name + "'" : "null")}");
            return r;
        } catch (Exception e) {
            // Tag not defined in HK's tag manager, log instead of crash
            LogError($"FindWithTag('{tag}') threw {e.GetType().Name} (tag not defined in HK) -> null");
            return null;
        }
    }

    private GameObject? Intercept(string method, string key, GameObject? redirect) {
        LogDebug(redirect
            ? $"{method}('{key}') -> REDIRECT '{redirect!.name}' (silksong-context)"
            : $"{method}('{key}') -> null (silksong-context, no mapping, blocked)");
        return redirect;
    }

    // Known Silksong tags -> the Silksong object they should resolve to
    private static GameObject? ResolveTag(string tag) {
        return tag switch {
            "CameraTarget" => GameCamerasBootstrap.CameraTargetGo, // Sprint FSM needs Silksong's CameraTarget (SetSprint)
            _ => null
        };
    }

    private static GameObject? ResolveName(string name) {
        return null; // no name mappings needed yet
    }
}
