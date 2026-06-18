using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

using HornetPlayer.DevServer;
namespace HornetPlayer.Playground;

// Game-agnostic debug routes: scene inspection, field/method poking, screenshots. A port of DevUtils' DevRoutes,
// trimmed to the Unity-only routes (the Silksong-specific GameManager/BepInEx ones are dropped). Register from the mod.
public static class PlaygroundRoutes {
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Register() {
        DebugServer.MapGet("/scene-tree", _ => SceneTree());
        DebugServer.MapGet("/screenshot", (AsyncRouteHandler)((_, respond) => Screenshot(respond)));
        DebugServer.MapGet("/inspect", req => Inspect(req["path"]));
        DebugServer.MapPost("/set-active", req => SetActive(req["name"], req["path"], req["active"]));
        DebugServer.MapPost("/set-field", req => SetField(req["path"], req["field"], req["value"]));
        DebugServer.MapPost("/invoke", req => Invoke(req["path"], req["method"]));
    }

    private static object SceneTree() {
        return Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)
            .Where(go => go.transform.parent == null)
            .Select(SceneNode)
            .ToArray();
    }

    private static object SceneNode(GameObject go) {
        var children = go.transform.Cast<Transform>().Select(t => SceneNode(t.gameObject)).ToArray();
        var node = new Dictionary<string, object?> {
            ["name"] = go.name,
            ["active"] = go.activeSelf,
            ["components"] = go.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().FullName).ToArray()
        };
        // Omit children entirely on leaf nodes rather than emitting an empty array.
        if (children.Length > 0) node["children"] = children;
        return node;
    }

    private static IEnumerator Screenshot(Action<object?> respond) {
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        var png = tex.EncodeToPNG();
        Object.Destroy(tex);
        respond(DevResponse.Bytes(png, "image/png"));
    }

    private static object Inspect(string? rawPath) {
        var (path, error) = ParsePath(rawPath);
        if (error != null) return error;

        var target = path!.ResolveGameObject();
        if (target == null) return DevResponse.Json(new { error = $"GameObject not found: {rawPath}" }, 404);

        // No @Component selector → list the components so the caller can pick one to dump.
        if (path.Component == null)
            return new {
                path = ComponentPath.GetPath(target),
                components = target.GetComponents<Component>()
                    .Select(c => c == null ? "null" : c.GetType().FullName).ToArray()
            };

        var comp = ComponentPath.FindComponent(target, path.Component);
        if (comp == null) return DevResponse.Json(new { error = $"Component '{path.Component}' not found" }, 404);

        var fields = new Dictionary<string, object?>();
        var properties = new Dictionary<string, object?>();

        for (var type = comp.GetType(); type != null && type != typeof(object); type = type.BaseType) {
            foreach (var field in type.GetFields(MemberFlags | BindingFlags.DeclaredOnly))
                fields[$"{field.DeclaringType?.Name}.{field.Name}"] = SafeValue(() => field.GetValue(comp));

            foreach (var prop in type.GetProperties(MemberFlags | BindingFlags.DeclaredOnly)) {
                if (!prop.CanRead) continue;
                properties[$"{prop.DeclaringType?.Name}.{prop.Name}"] = SafeValue(() => prop.GetValue(comp));
            }
        }

        return new { type = comp.GetType().FullName, fields, properties };
    }

    private static object SetActive(string? name, string? rawPath, string? activeStr) {
        if (activeStr == null) return DevResponse.Json(new { error = "missing 'active' param" }, 400);
        if (name == null && rawPath == null) return DevResponse.Json(new { error = "missing 'name' or 'path' param" }, 400);
        var active = activeStr.ToLowerInvariant() is "true" or "1";

        // `name` is a contains-match over every GameObject; `path` resolves a single one.
        if (name != null) {
            var count = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (!go.name.Contains(name)) continue;
                go.SetActive(active);
                count++;
            }

            return new { affected = count, active };
        }

        var (path, error) = ParsePath(rawPath);
        if (error != null) return error;
        var target = path!.ResolveGameObject();
        if (target == null) return DevResponse.Json(new { error = $"GameObject not found: {rawPath}" }, 404);
        target.SetActive(active);
        return new { affected = 1, active };
    }

    private static object SetField(string? rawPath, string? fieldName, string? value) {
        if (fieldName == null) return DevResponse.Json(new { error = "missing 'field' param" }, 400);

        var (_, comp, error) = ResolveComponent(rawPath);
        if (error != null) return error;

        var field = comp!.GetType().GetField(fieldName, MemberFlags);
        if (field == null) return DevResponse.Json(new { error = $"Field not found: {fieldName}" }, 404);

        var newValue = value == "null" ? null
            : field.FieldType == typeof(bool) ? value == "true"
            : field.FieldType == typeof(int) ? int.Parse(value ?? "0", CultureInfo.InvariantCulture)
            : field.FieldType == typeof(float) ? float.Parse(value ?? "0", CultureInfo.InvariantCulture)
            : (object)(value ?? "");
        field.SetValue(comp, newValue);
        return new { ok = true };
    }

    private static object Invoke(string? rawPath, string? methodName) {
        if (methodName == null) return DevResponse.Json(new { error = "missing 'method' param" }, 400);

        var (_, comp, error) = ResolveComponent(rawPath);
        if (error != null) return error;

        var method = comp!.GetType().GetMethod(methodName, MemberFlags);
        if (method == null) return DevResponse.Json(new { error = $"Method not found: {methodName}" }, 404);

        var parms = method.GetParameters();
        var args = new object[parms.Length];
        for (var i = 0; i < parms.Length; i++)
            args[i] = parms[i].DefaultValue ?? Activator.CreateInstance(parms[i].ParameterType)!;
        method.Invoke(comp, args);
        return new { ok = true, invoked = methodName };
    }

    private static (ComponentPath? path, object? error) ParsePath(string? raw) {
        if (raw == null) return (null, DevResponse.Json(new { error = "missing 'path' param" }, 400));
        try {
            return (ComponentPath.Parse(raw), null);
        } catch (FormatException e) {
            return (null, DevResponse.Json(new { error = $"invalid path: {e.Message}" }, 400));
        }
    }

    // Resolves `path@Component` to a live component. The `@Component` selector is required here.
    private static (GameObject? target, Component? comp, object? error) ResolveComponent(string? rawPath) {
        var (path, error) = ParsePath(rawPath);
        if (error != null) return (null, null, error);
        if (path!.Component == null)
            return (null, null, DevResponse.Json(new { error = "missing component selector (use path@Component)" }, 400));

        var target = path.ResolveGameObject();
        if (target == null) return (null, null, DevResponse.Json(new { error = $"GameObject not found: {rawPath}" }, 404));

        var comp = path.ResolveComponent(target);
        if (comp == null) return (null, null, DevResponse.Json(new { error = $"Component '{path.Component}' not found" }, 404));

        return (target, comp, null);
    }

    // Primitives pass through; anything else is stringified so Newtonsoft can't recurse into deep Unity graphs.
    private static object? SafeValue(Func<object?> getter) {
        object? value;
        try {
            value = getter();
        } catch (Exception e) {
            return $"<error: {e.Message}>";
        }

        return value switch {
            null or bool or int or float or double or string => value,
            _ => value.ToString()
        };
    }
}
