using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Log = HornetInHallownest.Playground.Log; // TEMP: shared infra, slated to move into HornetInHallownest.

namespace HornetInHallownest.HornetInHallownest.Util;

// https://github.com/EverestAPI/CelesteTAS-EverestInterop/blob/6425dc23e7a091bdea731f3ae60f319ea841a1b9/CelesteTAS-EverestInterop/Source/Utils/Extensions.cs#L97
public static class ReflectionExtension {
    internal const BindingFlags InstanceAnyVisibility =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal const BindingFlags StaticAnyVisibility =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    internal const BindingFlags StaticInstanceAnyVisibility =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal const BindingFlags InstanceAnyVisibilityDeclaredOnly = BindingFlags.Public | BindingFlags.NonPublic |
                                                                    BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<MemberKey, MemberInfo?> CachedMemberInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, FieldInfo?> CachedFieldInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, PropertyInfo?> CachedPropertyInfos = new();
    private static readonly ConcurrentDictionary<MethodKey, MethodInfo?> CachedMethodInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, EventInfo?> CachedEventInfos = new();

    private static readonly ConcurrentDictionary<MemberKey, MethodInfo?> CachedGetMethodInfos = new();
    private static readonly ConcurrentDictionary<MemberKey, MethodInfo?> CachedSetMethodInfos = new();

    private static readonly ConcurrentDictionary<AllMemberKey, IEnumerable<FieldInfo>> CachedAllFieldInfos = new();

    private static readonly ConcurrentDictionary<AllMemberKey, IEnumerable<PropertyInfo>>
        CachedAllPropertyInfos = new();

    private static readonly ConcurrentDictionary<AllMemberKey, IEnumerable<MethodInfo>> CachedAllMethodInfos = new();

    /// Resolves the target member on the type, caching the result
    public static MemberInfo? GetMemberInfo(this Type type, string name,
        BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new MemberKey(type, name);
        if (CachedMemberInfos.TryGetValue(key, out var result)) return result;

        var currentType = type;
        do {
            result = currentType.GetMember(name, bindingFlags).FirstOrDefault();
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) Log.Error($"Failed to find member '{name}' on type '{type}'");

        return CachedMemberInfos[key] = result;
    }

    /// Resolves the target field on the type, caching the result
    public static FieldInfo? GetFieldInfo(this Type type, string name,
        BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new MemberKey(type, name);
        if (CachedFieldInfos.TryGetValue(key, out var result)) return result;

        var currentType = type;
        do {
            result = currentType.GetField(name, bindingFlags);
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) Log.Error($"Failed to find field '{name}' on type '{type}'");

        return CachedFieldInfos[key] = result;
    }

    /// Resolves the target property on the type, caching the result
    public static PropertyInfo? GetPropertyInfo(this Type type, string name,
        BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new MemberKey(type, name);
        if (CachedPropertyInfos.TryGetValue(key, out var result)) return result;

        var currentType = type;
        do {
            result = currentType.GetProperty(name, bindingFlags);
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) Log.Error($"Failed to find property '{name}' on type '{type}'");

        return CachedPropertyInfos[key] = result;
    }

    /// Resolves the target method on the type, with the specific parameter types, caching the result
    public static MethodInfo? GetMethodInfo(this Type type, string name, Type?[]? parameterTypes = null,
        BindingFlags bindingFlags = StaticInstanceAnyVisibility, bool logFailure = true) {
        var key = new MethodKey(type, name, parameterTypes.GetCustomHashCode());
        if (CachedMethodInfos.TryGetValue(key, out var result)) return result;

        var currentType = type;
        do {
            if (parameterTypes != null)
                foreach (var method in currentType.GetAllMethodInfos(bindingFlags)) {
                    if (method.Name != name) continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != parameterTypes.Length) continue;

                    for (var i = 0; i < parameters.Length; i++)
                        // Treat a null type as a wild card
                        if (parameterTypes[i] != null && parameterTypes[i] != parameters[i].ParameterType)
                            goto NextMethod;

                    if (result != null) {
                        // "Amphibious" matches on different types indicate overrides. Choose the "latest" method
                        if (result.DeclaringType != null && result.DeclaringType != method.DeclaringType) {
                            if (method.DeclaringType!.IsSubclassOf(result.DeclaringType)) result = method;
                        }
                        else {
                            if (logFailure)
                                Log.Error(
                                    $"Method '{name}' with parameters ({string.Join<Type?>(", ", parameterTypes)}) on type '{type}' is ambiguous between '{result}' and '{method}'");

                            result = null;
                            break;
                        }
                    }
                    else {
                        result = method;
                    }

                    NextMethod: ;
                }
            else
                result = currentType.GetMethod(name, bindingFlags);

            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null && logFailure) {
            if (parameterTypes == null)
                Log.Error($"Failed to find method '{name}' on type '{type}'");
            else
                Log.Error(
                    $"Failed to find method '{name}' with parameters ({string.Join<Type?>(", ", parameterTypes)}) on type '{type}'");
        }

        return CachedMethodInfos[key] = result;
    }

    /// Resolves the target event on the type, with the specific parameter types, caching the result
    public static EventInfo? GetEventInfo(this Type type, string name,
        BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
        var key = new MemberKey(type, name);
        if (CachedEventInfos.TryGetValue(key, out var result)) return result;

        var currentType = type;
        do {
            result = currentType.GetEvent(name, bindingFlags);
            currentType = currentType.BaseType;
        } while (result == null && currentType != null);

        if (result == null) Log.Error($"Failed to find event '{name}' on type '{type}'");

        return CachedEventInfos[key] = result;
    }

    /// Resolves the target get-method of the property on the type, caching the result
    public static MethodInfo? GetGetMethod(this Type type, string name,
        BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
        var key = new MemberKey(type, name);
        if (CachedGetMethodInfos.TryGetValue(key, out var result)) return result;

        result = type.GetPropertyInfo(name, bindingFlags)?.GetGetMethod(true);
        if (result == null) Log.Error($"Failed to find get-method of property '{name}' on type '{type}'");

        return CachedGetMethodInfos[key] = result;
    }

    /// Resolves the target set-method of the property on the type, caching the result
    public static MethodInfo? GetSetMethod(this Type type, string name,
        BindingFlags bindingFlags = StaticInstanceAnyVisibility) {
        var key = new MemberKey(type, name);
        if (CachedSetMethodInfos.TryGetValue(key, out var result)) return result;

        result = type.GetPropertyInfo(name, bindingFlags)?.GetSetMethod(true);
        if (result == null) Log.Error($"Failed to find set-method of property '{name}' on type '{type}'");

        return CachedSetMethodInfos[key] = result;
    }

    /// Resolves all fields of the type, caching the result
    public static IEnumerable<FieldInfo> GetAllFieldInfos(this Type type,
        BindingFlags bindingFlags = InstanceAnyVisibility) {
        bindingFlags |= BindingFlags.DeclaredOnly;

        var key = new AllMemberKey(type, bindingFlags);
        if (CachedAllFieldInfos.TryGetValue(key, out var result)) return result;

        HashSet<FieldInfo> allFields = [];

        var currentType = type;
        while (currentType != null && currentType.IsSubclassOf(typeof(object))) {
            allFields.UnionWith(currentType.GetFields(bindingFlags));

            currentType = currentType.BaseType;
        }

        return CachedAllFieldInfos[key] = allFields;
    }

    /// Resolves all properties of the type, caching the result
    public static IEnumerable<PropertyInfo> GetAllPropertyInfos(this Type type,
        BindingFlags bindingFlags = InstanceAnyVisibility) {
        bindingFlags |= BindingFlags.DeclaredOnly;

        var key = new AllMemberKey(type, bindingFlags);
        if (CachedAllPropertyInfos.TryGetValue(key, out var result)) return result;

        HashSet<PropertyInfo> allProperties = [];

        var currentType = type;
        while (currentType != null && currentType.IsSubclassOf(typeof(object))) {
            allProperties.UnionWith(currentType.GetProperties(bindingFlags));

            currentType = currentType.BaseType;
        }

        return CachedAllPropertyInfos[key] = allProperties;
    }

    /// Resolves all methods of the type, caching the result
    public static IEnumerable<MethodInfo> GetAllMethodInfos(this Type type,
        BindingFlags bindingFlags = InstanceAnyVisibility) {
        bindingFlags |= BindingFlags.DeclaredOnly;

        var key = new AllMemberKey(type, bindingFlags);
        if (CachedAllMethodInfos.TryGetValue(key, out var result)) return result;

        HashSet<MethodInfo> allMethods = [];

        var currentType = type;
        while (currentType != null && currentType.IsSubclassOf(typeof(object))) {
            allMethods.UnionWith(currentType.GetMethods(bindingFlags));

            currentType = currentType.BaseType;
        }

        return CachedAllMethodInfos[key] = allMethods;
    }

    /// Gets the value of the instance field on the object
    public static T? GetFieldValue<T>(this object obj, string name) {
        if (obj.GetType().GetFieldInfo(name, InstanceAnyVisibility) is not { } field) return default;

        return (T?)field.GetValue(obj);
    }

    /// Gets the value of the static field on the type
    public static T? GetFieldValue<T>(this Type type, string name) {
        if (type.GetFieldInfo(name, StaticAnyVisibility) is not { } field) return default;

        return (T?)field.GetValue(null);
    }

    /// Sets the value of the instance field on the object
    public static void SetFieldValue(this object obj, string name, object? value) {
        if (obj.GetType().GetFieldInfo(name, InstanceAnyVisibility) is not { } field) return;

        field.SetValue(obj, value);
    }

    /// Sets the value of the static field on the type
    public static void SetFieldValue(this Type type, string name, object? value) {
        if (type.GetFieldInfo(name, StaticAnyVisibility) is not { } field) return;

        field.SetValue(null, value);
    }

    /// Gets the value of the instance property on the object
    public static T? GetPropertyValue<T>(this object obj, string name) {
        if (obj.GetType().GetPropertyInfo(name, InstanceAnyVisibility) is not { } property) return default;

        if (!property.CanRead) {
            Log.Error($"Property '{name}' on type '{obj.GetType()}' is not readable");
            return default;
        }

        return (T?)property.GetValue(obj);
    }

    /// Gets the value of the static property on the type
    public static T? GetPropertyValue<T>(this Type type, string name) {
        if (type.GetPropertyInfo(name, StaticAnyVisibility) is not { } property) return default;

        if (!property.CanRead) {
            Log.Error($"Property '{name}' on type '{type}' is not readable");
            return default;
        }

        return (T?)property.GetValue(null);
    }

    /// Sets the value of the instance property on the object
    public static void SetPropertyValue(this object obj, string name, object? value) {
        if (obj.GetType().GetPropertyInfo(name, InstanceAnyVisibility) is not { } property) return;

        if (!property.CanWrite) {
            Log.Error($"Property '{name}' on type '{obj.GetType()}' is not writable");
            return;
        }

        property.SetValue(obj, value);
    }

    /// Sets the value of the static property on the type
    public static void SetPropertyValue(this Type type, string name, object? value) {
        if (type.GetPropertyInfo(name, StaticAnyVisibility) is not { } property) return;

        if (!property.CanWrite) {
            Log.Error($"Property '{name}' on type '{type}' is not writable");
            return;
        }

        property.SetValue(null, value);
    }

    /// Invokes the instance method on the type
    public static void InvokeMethod(this object obj, string name, params object?[]? parameters) {
        if (obj.GetType().GetMethodInfo(name,
                parameters?.Select(param => param?.GetType()).ToArray(),
                InstanceAnyVisibility) is not { } method)
            return;

        method.Invoke(obj, parameters);
    }

    /// Invokes the static method on the type
    public static void InvokeMethod(this Type type, string name, params object?[]? parameters) {
        if (type.GetMethodInfo(name, parameters?.Select(param => param?.GetType()).ToArray(), StaticAnyVisibility) is
            not { } method)
            return;

        method.Invoke(null, parameters);
    }

    /// Invokes the instance method on the type, returning the result
    public static T? InvokeMethod<T>(this object obj, string name, params object?[]? parameters) {
        if (obj.GetType().GetMethodInfo(name,
                parameters?.Select(param => param?.GetType()).ToArray(),
                InstanceAnyVisibility) is not { } method)
            return default;

        return (T?)method.Invoke(obj, parameters);
    }

    /// Invokes the static method on the type, returning the result
    public static T? InvokeMethod<T>(this Type type, string name, params object?[]? parameters) {
        if (type.GetMethodInfo(name, parameters?.Select(param => param?.GetType()).ToArray(), StaticAnyVisibility) is
            not { } method)
            return default;

        return (T?)method.Invoke(null, parameters);
    }

    private readonly record struct MemberKey(Type Type, string Name);

    private readonly record struct AllMemberKey(Type Type, BindingFlags BindingFlags);

    private readonly record struct MethodKey(Type Type, string Name, long ParameterHash);
}

internal static class HashCodeExtensions {
    public static long GetCustomHashCode<T>(this IEnumerable<T>? enumerable) {
        if (enumerable == null) return 0;

        unchecked {
            long hash = 17;
            foreach (var item in enumerable) hash = hash * -1521134295 + EqualityComparer<T>.Default.GetHashCode(item!);

            return hash;
        }
    }
}
