using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HornetPlayer.DevServer;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Game-agnostic debug routes: scene inspection, field/method poking, screenshots. A port of DevUtils' DevRoutes,
// trimmed to the Unity-only routes (the Silksong-specific GameManager/BepInEx ones are dropped). Register from the mod.
public static class PlaygroundRoutes {
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Register() {
        DebugServer.MapGet("/scene", _ => new { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name });
        DebugServer.MapGet("/scene-tree", _ => SceneTree());
        DebugServer.MapGet("/screenshot", (AsyncRouteHandler)((_, respond) => Screenshot(respond)));
        DebugServer.MapGet("/inspect", req => Inspect(req["path"], req["depth"]));
        DebugServer.MapPost("/set-active", req => SetActive(req["name"], req["path"], req["active"]));
        DebugServer.MapPost("/set-field", req => SetField(req["path"], req["field"], req["value"]));
        DebugServer.MapPost("/invoke", req => Invoke(req["path"], req["method"]));
        DebugServer.MapPost("/set-field", req => SetField(req["path"], req["field"], req["value"]));
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
            ["layer"] = go.layer,
            ["components"] = go.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().FullName)
                .ToArray()
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

    private static object Inspect(string? rawPath, string? depthStr) {
        // Default depth 1: each field/property VALUE is expanded one level, so nested plain data (e.g. cState =
        // HeroControllerStates) shows its members instead of stringifying to the type name. Unity objects + primitives
        // stop the recursion, so it stays bounded. ?depth=N for deeper.
        var depth = int.TryParse(depthStr, out var d) ? Math.Max(0, d) : 1;
        var (path, error) = ParsePath(rawPath);
        if (error != null) return error;

        var target = path!.ResolveGameObject();
        if (target == null) return DevResponse.Json(new { error = $"GameObject not found: {rawPath}" }, 404);

        // No @Component selector → list the components so the caller can pick one to dump.
        if (path.Component == null)
            return new {
                path = ComponentPath.GetPath(target),
                layer = target.layer,
                components = target.GetComponents<Component>()
                    .Select(c => c == null ? "null" : c.GetType().FullName).ToArray()
            };

        var comp = ComponentPath.FindComponent(target, path.Component);
        if (comp == null) return DevResponse.Json(new { error = $"Component '{path.Component}' not found" }, 404);

        var fields = new Dictionary<string, object?>();
        var properties = new Dictionary<string, object?>();

        for (var type = comp.GetType(); type != null && type != typeof(object); type = type.BaseType) {
            foreach (var field in type.GetFields(MemberFlags | BindingFlags.DeclaredOnly))
                fields[$"{field.DeclaringType?.Name}.{field.Name}"] = SafeFmt(() => field.GetValue(comp), depth);

            foreach (var prop in type.GetProperties(MemberFlags | BindingFlags.DeclaredOnly)) {
                if (!prop.CanRead) continue;
                properties[$"{prop.DeclaringType?.Name}.{prop.Name}"] = SafeFmt(() => prop.GetValue(comp), depth);
            }
        }

        return new { type = comp.GetType().FullName, fields, properties };
    }

    private static object SetActive(string? name, string? rawPath, string? activeStr) {
        if (activeStr == null) return DevResponse.Json(new { error = "missing 'active' param" }, 400);
        if (name == null && rawPath == null)
            return DevResponse.Json(new { error = "missing 'name' or 'path' param" }, 400);
        var active = activeStr.ToLowerInvariant() is "true" or "1";

        // `name` is a contains-match over every GameObject; `path` resolves a single one.
        if (name != null) {
            var count = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None)) {
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

        var (target, comp, error) = ResolveComponent(rawPath);
        if (error != null) return error;

        // Support dotted paths like "transform.position" — walk properties/fields.
        object? current = comp;
        Type? type = comp!.GetType();
        var parts = fieldName.Split('.');
        for (var i = 0; i < parts.Length - 1; i++) {
            var prop = type!.GetProperty(parts[i], MemberFlags);
            if (prop != null) { current = prop.GetValue(current); type = current?.GetType(); continue; }
            var field = type!.GetField(parts[i], MemberFlags);
            if (field != null) { current = field.GetValue(current); type = current?.GetType(); continue; }
            return DevResponse.Json(new { error = $"Cannot resolve '{parts[i]}' in '{fieldName}'" }, 404);
        }
        var leaf = parts[^1];
        var leafProp = type!.GetProperty(leaf, MemberFlags);
        var leafField = type!.GetField(leaf, MemberFlags);
        if (leafProp == null && leafField == null)
            return DevResponse.Json(new { error = $"Field/property not found: {fieldName}" }, 404);
        var fieldType = leafProp?.PropertyType ?? leafField!.FieldType;

        var newValue = ParseValue(value, fieldType);
        if (leafProp != null) leafProp.SetValue(current, newValue);
        else leafField!.SetValue(current, newValue);
        return new { ok = true };
    }

    private static object? ParseValue(string? value, Type type) {
        if (value == "null") return null;
        if (type == typeof(string)) return value;
        if (type == typeof(bool)) return value == "true";
        if (type == typeof(int)) return int.Parse(value ?? "0", CultureInfo.InvariantCulture);
        if (type == typeof(float)) return float.Parse(value ?? "0", CultureInfo.InvariantCulture);
        if (type == typeof(Vector3)) {
            var p = (value ?? "0,0,0").Split(',');
            return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture),
                               float.Parse(p.Length > 1 ? p[1] : "0", CultureInfo.InvariantCulture),
                               float.Parse(p.Length > 2 ? p[2] : "0", CultureInfo.InvariantCulture));
        }
        if (type == typeof(Vector2)) {
            var p = (value ?? "0,0").Split(',');
            return new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture),
                                float.Parse(p.Length > 1 ? p[1] : "0", CultureInfo.InvariantCulture));
        }
        return value ?? "";
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
        object? result;
        try {
            result = method.Invoke(comp, args);
        } catch (Exception e) {
            return new { ok = false, invoked = methodName, error = (e.InnerException ?? e).Message };
        }

        // Return the actual result (expanded one level), not just "ok" — the whole point of invoking a bool/struct
        // getter like CanOpenInventory() is to SEE what it returns.
        return new {
            ok = true, invoked = methodName, returnType = method.ReturnType.Name, returned = Format(result, 1)
        };
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
            return (null, null,
                DevResponse.Json(new { error = "missing component selector (use path@Component)" }, 400));

        var target = path.ResolveGameObject();
        if (target == null)
            return (null, null, DevResponse.Json(new { error = $"GameObject not found: {rawPath}" }, 404));

        var comp = path.ResolveComponent(target);
        if (comp == null)
            return (null, null, DevResponse.Json(new { error = $"Component '{path.Component}' not found" }, 404));

        return (target, comp, null);
    }

    // Get a value safely (catching getter exceptions) and format it bounded to `depth`.
    private static object? SafeFmt(Func<object?> getter, int depth) {
        try {
            return Format(getter(), depth);
        } catch (Exception e) {
            return $"<error: {e.Message}>";
        }
    }

    // Bounded value formatter. Primitives/strings/enums pass through. A UnityEngine.Object becomes "Type:name" and is
    // NEVER expanded — recursing the scene/component graph (Transform->children->components->…) would explode. Plain
    // (non-Unity) classes/structs expand their instance fields, one level per remaining `depth`; collections list their
    // elements (capped). Since recursion stops at Unity objects + primitives, `depth` bounds the output. This is what
    // lets /inspect show cState (HeroControllerStates) as its bools instead of the bare type name.
    private static object? Format(object? value, int depth) {
        switch (value) {
            case null or bool or int or float or double or string: return value;
        }

        var t = value.GetType();
        if (t.IsEnum || t.IsPrimitive) return value.ToString(); // long/byte/enum/…
        if (value is Object uo) return $"{t.Name}:{uo.name}"; // UnityEngine.Object — don't recurse
        if (value is Vector2 or Vector3 or Vector4 or Quaternion or Color) return value.ToString();
        if (depth <= 0) return value.ToString();

        if (value is IDictionary) return value.ToString(); // skip (rare; avoid key formatting)
        if (value is IEnumerable en) {
            var items = new List<object?>();
            foreach (var item in en) {
                if (items.Count >= 64) {
                    items.Add("…(truncated)");
                    break;
                }

                items.Add(Format(item, depth - 1));
            }

            return items;
        }

        // Plain class/struct: expand instance fields one level down.
        var dict = new Dictionary<string, object?>();
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            dict[f.Name] = SafeFmt(() => f.GetValue(value), depth - 1);
        return dict.Count > 0 ? dict : value.ToString();
    }
}
