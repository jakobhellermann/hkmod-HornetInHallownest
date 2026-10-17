using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HornetInHallownest.Core.SilksongSetup;

// Rewrites Silksong's Unity 6 / netstandard2.1 IL in memory so it loads on Hollow Knight 1.5.78 (Unity 2020.2):
// - renamed API is renamed back (Rigidbody2D.linearVelocity -> velocity)
// - API the host lacks is redirected to static stand-ins on the UnityCompat shim
// - type references the host can't resolve are pointed at the host assembly that defines the type (tk2d lives in
//   HK's Assembly-CSharp, Span<T> in mscorlib), else at the shim, which defines missing types under their original name
// - default interface methods, which Unity 2020.2's Mono can't load, are copied down into the implementors
internal sealed class UnityDowngrader {
    private static readonly string[] shimClasses = ["UnityEngine.Compat.HornetCompat", "UnityEngine.Compat.BclCompat"];

    // Assemblies whose types win over same-named types elsewhere in the host.
    private static readonly string[] preferredHostAssemblies = ["mscorlib", "System.Core", "System", "netstandard"];

    // May stay unresolved when the host doesn't ship them. GOG Galaxy SDK: only in GOG builds of HK, and Silksong
    // only touches it from GogGalaxyManager, which nothing instantiates.
    private static readonly string[] optionalHostAssemblies = ["GalaxyCSharp"];

    private static readonly Dictionary<string, (string Namespace, string Name)> typeRenames = new() {
        ["UnityEngine.PhysicsMaterial"] = ("UnityEngine", "PhysicMaterial"),
        ["UnityEngine.UI.InputField/EndEditEvent"] = ("", "SubmitEvent"),
    };

    // A nested type can't be added to a host type, so these become top-level shim types in UnityEngine.Compat.
    private static readonly Dictionary<string, string> nestedToShim = new() {
        ["tk2dSpriteAnimator/AnimationCompletedDelegate"] = "Tk2dAnimationCompletedDelegate",
        ["tk2dSpriteAnimator/AnimationEventDelegate"] = "Tk2dAnimationEventDelegate",
        ["tk2dSpriteAnimator/AnimationChangedDelegate"] = "Tk2dAnimationChangedDelegate",
    };

    private static readonly Dictionary<(string Type, string Member), string> memberRenames = new() {
        [("UnityEngine.Rigidbody2D", "get_linearVelocity")] = "get_velocity",
        [("UnityEngine.Rigidbody2D", "set_linearVelocity")] = "set_velocity",
        [("UnityEngine.Rigidbody2D", "get_linearDamping")] = "get_drag",
        [("UnityEngine.Rigidbody2D", "set_linearDamping")] = "set_drag",
        [("UnityEngine.Rigidbody2D", "get_angularDamping")] = "get_angularDrag",
        [("UnityEngine.Rigidbody2D", "set_angularDamping")] = "set_angularDrag",
        [("UnityEngine.Rigidbody", "get_linearVelocity")] = "get_velocity",
        [("UnityEngine.Rigidbody", "set_linearVelocity")] = "set_velocity",
        [("UnityEngine.Rigidbody", "get_linearDamping")] = "get_drag",
        [("UnityEngine.Rigidbody", "set_linearDamping")] = "set_drag",
        [("UnityEngine.Rigidbody", "get_angularDamping")] = "get_angularDrag",
        [("UnityEngine.Rigidbody", "set_angularDamping")] = "set_angularDrag",
        [("UnityEngine.Collider2D", "Overlap")] = "OverlapCollider",
    };

    // ldfld/stfld have the same stack shape as a static call taking the receiver first.
    private static readonly Dictionary<(string Type, string Field), string> fieldToShim = new() {
        [("tk2dSpriteAnimator", "FrameChanged")] = "tk2dSpriteAnimator_FrameChanged",
    };

    // Only applied when the host lacks the exact overload; instance members get the receiver as first parameter.
    private static readonly Dictionary<(string Type, string Member), string> memberToShim = BuildMemberToShim();

    private static Dictionary<(string Type, string Member), string> BuildMemberToShim() {
        var map = new Dictionary<(string Type, string Member), string> {
            [("UnityEngine.Object", "FindAnyObjectByType")] = "FindAnyObjectByType",
            [("UnityEngine.Object", "FindObjectsByType")] = "FindObjectsByType",
            [("UnityEngine.MonoBehaviour", "get_didAwake")] = "MonoBehaviour_get_didAwake",
            [("UnityEngine.Component", "GetComponentInParent")] = "GetComponentInParent",
            [("UnityEngine.GameObject", "GetComponentInParent")] = "GetComponentInParent",
            [("UnityEngine.AssetBundle", "UnloadAsync")] = "AssetBundle_UnloadAsync",
            [("UnityEngine.Networking.DownloadHandlerAssetBundle", "get_autoLoadAssetBundle")] = "DownloadHandlerAssetBundle_get_autoLoadAssetBundle",
            [("UnityEngine.Networking.DownloadHandlerAssetBundle", "set_autoLoadAssetBundle")] = "DownloadHandlerAssetBundle_set_autoLoadAssetBundle",
            [("UnityEngine.Networking.DownloadHandlerAssetBundle", "get_isDownloadComplete")] = "DownloadHandlerAssetBundle_get_isDownloadComplete",
            [("UnityEngine.Mathf", "PerlinNoise1D")] = "PerlinNoise1D",
            [("UnityEngine.Screen", "SetResolution")] = "SetResolution",
            [("UnityEngine.Resolution", "get_refreshRateRatio")] = "Resolution_get_refreshRateRatio",
            [("UnityEngine.Resolution", "set_refreshRateRatio")] = "Resolution_set_refreshRateRatio",
            [("UnityEngine.Video.VideoPlayer", "get_canSetTimeUpdateMode")] = "VideoPlayer_get_canSetTimeUpdateMode",
            [("UnityEngine.Video.VideoPlayer", "get_timeUpdateMode")] = "VideoPlayer_get_timeUpdateMode",
            [("UnityEngine.Video.VideoPlayer", "set_timeUpdateMode")] = "VideoPlayer_set_timeUpdateMode",
            [("System.String", "Contains")] = "String_Contains",
            [("System.String", "Split")] = "String_Split",
            [("System.String", "TrimStart")] = "String_TrimStart",
            [("System.String", "Create")] = "String_Create",
            [("System.TimeSpan", "op_Multiply")] = "TimeSpan_Multiply",
            [("System.ArraySegment`1", "get_Empty")] = "ArraySegment_Empty",
            [("tk2dSprite", "get_HasKeywordsDefined")] = "tk2dSprite_get_HasKeywordsDefined",
            [("tk2dSprite", "EnableKeyword")] = "tk2dSprite_EnableKeyword",
            [("tk2dSprite", "DisableKeyword")] = "tk2dSprite_DisableKeyword",
            [("tk2dSprite", "MoveKeywords")] = "tk2dSprite_MoveKeywords",
            [("tk2dSprite", "SetFloat")] = "tk2dSprite_SetFloat",
        };
        foreach (var e in new[] { "AnimationCompletedEvent", "AnimationEventTriggeredEvent", "AnimationChanged" }) {
            map[("tk2dSpriteAnimator", "add_" + e)] = "tk2dSpriteAnimator_add_" + e;
            map[("tk2dSpriteAnimator", "remove_" + e)] = "tk2dSpriteAnimator_remove_" + e;
        }
        foreach (var p in new[] { "excludeLayers", "includeLayers", "callbackLayers", "contactCaptureLayers",
                                  "forceSendLayers", "forceReceiveLayers", "layerOverridePriority" }) {
            map[("UnityEngine.Collider2D", "get_" + p)] = "Collider2D_get_" + p;
            map[("UnityEngine.Collider2D", "set_" + p)] = "Collider2D_set_" + p;
        }
        return map;
    }

    private readonly AssemblyNameReference shim;
    private readonly Dictionary<string, TypeDefinition> shimMethodOwner = new();
    private readonly HashSet<string> shimTypes = new();
    private readonly Dictionary<string, AssemblyNameReference> hostTypes = new();
    private readonly Dictionary<string, AssemblyNameReference> hostAssemblies = new();
    private readonly Action<string> log;
    private HashSet<string> ours = new();

    // The assemblies being downgraded must be read with a resolver that serves the host, the shim and each other.
    public UnityDowngrader(IEnumerable<AssemblyDefinition> host, AssemblyDefinition shim, Action<string> log) {
        this.log = log;
        this.shim = shim.Name;

        var hostOrder = host
            .OrderBy(a => {
                var i = Array.IndexOf(preferredHostAssemblies, a.Name.Name);
                return i < 0 ? preferredHostAssemblies.Length : i;
            })
            .ThenBy(a => a.Name.Name, StringComparer.Ordinal);
        foreach (var asm in hostOrder) {
            hostAssemblies[asm.Name.Name] = asm.Name;
            foreach (var type in asm.MainModule.Types.Where(t => t.IsPublic))
                if (!hostTypes.ContainsKey(type.FullName)) hostTypes[type.FullName] = asm.Name;
        }

        foreach (var type in shim.MainModule.Types.Where(t => t.IsPublic)) shimTypes.Add(type.FullName);
        foreach (var className in shimClasses) {
            var type = shim.MainModule.GetType(className) ?? throw new InvalidOperationException($"{className} missing from {shim.Name.Name}");
            foreach (var method in type.Methods.Where(m => m.IsStatic)) {
                if (shimMethodOwner.TryGetValue(method.Name, out var owner) && owner != type)
                    throw new InvalidOperationException($"shim method {method.Name} defined in both {owner.FullName} and {type.FullName}");
                shimMethodOwner[method.Name] = type;
            }
        }
    }

    private bool IsMissingOptional(string assembly) =>
        optionalHostAssemblies.Contains(assembly) && !hostAssemblies.ContainsKey(assembly);

    // Throws listing every type reference neither the host nor the shim defines, across all assemblies.
    public void Downgrade(IReadOnlyCollection<AssemblyDefinition> assemblies) {
        ours = new HashSet<string>(assemblies.Select(a => a.Name.Name));
        var unresolved = new List<string>();
        foreach (var asm in assemblies) {
            var rewriter = new ModuleRewriter(this, asm.MainModule);
            var missing = rewriter.Run();
            if (missing.Count > 0)
                unresolved.Add($"{asm.Name.Name}: {string.Join(", ", missing.OrderBy(n => n, StringComparer.Ordinal))}");
            else
                log($"[UnityDowngrader] {asm.Name.Name}: {rewriter.Changes} rewrites");
        }
        if (unresolved.Count > 0)
            throw new InvalidOperationException($"no host or shim definition for:\n{string.Join("\n", unresolved)}");
    }

    private sealed class ModuleRewriter(UnityDowngrader d, ModuleDefinition module) {
        public int Changes { get; private set; }
        private readonly HashSet<MethodReference> rewrittenMethods = new();
        private readonly HashSet<MethodReference> madeStatic = new();
        private readonly Dictionary<MethodReference, GenericInstanceMethod> genericShimCalls = new();

        // Returns the type references nobody defines; the module is left half-rewritten if there are any.
        public List<string> Run() {
            RebindToHostVersions();
            RenameTypes();
            var unresolved = RehomeTypeReferences().Concat(RehomeAttributeTypeArguments()).Distinct().ToList();
            if (unresolved.Count > 0) return unresolved;
            RewriteMembers();
            FlattenDefaultInterfaceMethods();
            return unresolved;
        }

        // Silksong links newer versions of assemblies the host also ships (Newtonsoft.Json 13 vs 11); bind to the
        // host's identity, strong name included, instead of loading a second copy.
        private void RebindToHostVersions() {
            foreach (var reference in module.AssemblyReferences) {
                if (d.ours.Contains(reference.Name) || !d.hostAssemblies.TryGetValue(reference.Name, out var host)) continue;
                var token = host.PublicKeyToken ?? [];
                if (reference.Version == host.Version && (reference.PublicKeyToken ?? []).SequenceEqual(token)) continue;
                reference.Version = host.Version;
                reference.PublicKeyToken = token.Length == 0 ? null : token;
                Changes++;
            }
        }

        private void RenameTypes() {
            foreach (var tr in module.GetTypeReferences()) {
                if (typeRenames.TryGetValue(tr.FullName, out var rename)) {
                    if (rename.Namespace != "") tr.Namespace = rename.Namespace;
                    tr.Name = rename.Name;
                    Changes++;
                } else if (nestedToShim.TryGetValue(tr.FullName, out var shimName)) {
                    tr.DeclaringType = null; // a nested reference takes its scope from the declaring type
                    tr.Namespace = "UnityEngine.Compat";
                    tr.Name = shimName;
                    tr.Scope = ShimScope();
                    Changes++;
                }
            }
        }

        private IEnumerable<string> RehomeTypeReferences() {
            var unresolved = new List<string>();
            foreach (var tr in module.GetTypeReferences().Where(t => t.DeclaringType == null))
                if (!Rehome(tr)) unresolved.Add(tr.FullName);
            return unresolved;
        }

        // typeof(...) arguments are stored as assembly-qualified names in the attribute blob, so they don't show up
        // as type references. Only attributes taking a Type or object are decoded: decoding the rest can require
        // resolving enum types from assemblies the host doesn't ship.
        private IEnumerable<string> RehomeAttributeTypeArguments() {
            var unresolved = new List<string>();
            foreach (var provider in AttributeProviders())
            foreach (var attribute in provider.CustomAttributes) {
                if (!attribute.Constructor.Parameters.Any(p => MayHoldType(p.ParameterType))) continue;
                foreach (var argument in attribute.ConstructorArguments) RehomeArgument(argument, unresolved);
            }
            return unresolved;
        }

        private static bool MayHoldType(TypeReference type) {
            var element = type is ArrayType array ? array.ElementType : type;
            return element.FullName is "System.Type" or "System.Object";
        }

        private void RehomeArgument(CustomAttributeArgument argument, List<string> unresolved) {
            switch (argument.Value) {
                case TypeReference type:
                    var top = Outermost(type);
                    if (!Rehome(top)) unresolved.Add(top.FullName);
                    break;
                case CustomAttributeArgument boxed:
                    RehomeArgument(boxed, unresolved);
                    break;
                case CustomAttributeArgument[] items:
                    foreach (var item in items) RehomeArgument(item, unresolved);
                    break;
            }
        }

        private static TypeReference Outermost(TypeReference type) {
            while (type is TypeSpecification spec) type = spec.ElementType;
            while (type.DeclaringType != null) type = type.DeclaringType;
            return type;
        }

        private IEnumerable<ICustomAttributeProvider> AttributeProviders() {
            yield return module.Assembly;
            yield return module;
            foreach (var type in module.GetTypes()) {
                yield return type;
                foreach (var gp in type.GenericParameters) yield return gp;
                foreach (var field in type.Fields) yield return field;
                foreach (var property in type.Properties) yield return property;
                foreach (var e in type.Events) yield return e;
                foreach (var method in type.Methods) {
                    yield return method;
                    yield return method.MethodReturnType;
                    foreach (var p in method.Parameters) yield return p;
                    foreach (var gp in method.GenericParameters) yield return gp;
                }
            }
        }

        // Points a top-level type reference the host can't resolve at whoever defines it. False if nobody does.
        private bool Rehome(TypeReference tr) {
            if (Resolves(tr)) return true;
            var scope = (tr.Scope as AssemblyNameReference)?.Name;
            if (scope != null && d.ours.Contains(scope)) return false;

            AssemblyNameReference target;
            if (d.hostTypes.TryGetValue(tr.FullName, out var host)) target = host;
            else if (d.shimTypes.Contains(tr.FullName)) target = d.shim;
            else return scope != null && d.IsMissingOptional(scope);

            tr.Scope = Scope(target);
            Changes++;
            return true;
        }

        private void RewriteMembers() {
            foreach (var body in Bodies())
            foreach (var ins in body.Instructions) {
                if (ins.Operand is MethodReference callee) RewriteCallee(callee);
                else if (ins is { Operand: FieldReference field, OpCode.Code: Code.Ldfld or Code.Stfld })
                    RewriteFieldAccess(ins, field);
            }

            if (genericShimCalls.Count == 0 && madeStatic.Count == 0) return;
            foreach (var body in Bodies())
            foreach (var ins in body.Instructions) {
                if (ins.Operand is not MethodReference callee) continue;
                if (genericShimCalls.TryGetValue(callee, out var generic)) ins.Operand = generic;
                if (ins.OpCode.Code == Code.Callvirt && madeStatic.Contains(Element(callee))) ins.OpCode = OpCodes.Call;
            }
        }

        private void RewriteCallee(MethodReference callee) {
            var method = Element(callee);
            if (!rewrittenMethods.Add(method)) return; // member references are shared between call sites

            var declaring = method.DeclaringType is GenericInstanceType git ? git.ElementType.FullName : method.DeclaringType.FullName;
            var key = (declaring, method.Name);
            if (memberRenames.TryGetValue(key, out var newName)) {
                method.Name = newName;
                Changes++;
                return;
            }
            if (!memberToShim.TryGetValue(key, out var shimName) || ResolvesMethod(method)) return;

            if (!d.shimMethodOwner.TryGetValue(shimName, out var owner))
                throw new InvalidOperationException($"shim method {shimName} missing");
            if (method.HasThis) {
                var receiver = method.DeclaringType.IsValueType ? new ByReferenceType(method.DeclaringType) : method.DeclaringType;
                method.Parameters.Insert(0, new ParameterDefinition(module.ImportReference(receiver)));
                method.HasThis = false;
                madeStatic.Add(method);
            }

            // A static member of a generic type (ArraySegment<T>.Empty) maps onto a generic shim method.
            if (method.DeclaringType is GenericInstanceType genericOwner && !madeStatic.Contains(method)) {
                var shimMethod = owner.Methods.First(m => m.Name == shimName);
                if (!shimMethod.HasGenericParameters || shimMethod.GenericParameters.Count != genericOwner.GenericArguments.Count)
                    throw new InvalidOperationException($"shim method {shimName} needs {genericOwner.GenericArguments.Count} generic parameters");
                var instance = new GenericInstanceMethod(module.ImportReference(shimMethod));
                foreach (var argument in genericOwner.GenericArguments) instance.GenericArguments.Add(argument);
                genericShimCalls[callee] = instance;
            }

            method.DeclaringType = new TypeReference(owner.Namespace, owner.Name, module, ShimScope());
            method.Name = shimName;
            Changes++;
        }

        private void RewriteFieldAccess(Instruction ins, FieldReference field) {
            if (!fieldToShim.TryGetValue((field.DeclaringType.FullName, field.Name), out var stem)) return;
            var isLoad = ins.OpCode.Code == Code.Ldfld;
            // "tk2dSpriteAnimator_FrameChanged" -> "tk2dSpriteAnimator_get_FrameChanged"
            var name = stem.Insert(stem.LastIndexOf('_') + 1, isLoad ? "get_" : "set_");
            if (!d.shimMethodOwner.TryGetValue(name, out var owner))
                throw new InvalidOperationException($"shim method {name} missing");
            var accessor = new MethodReference(name, isLoad ? field.FieldType : module.TypeSystem.Void,
                new TypeReference(owner.Namespace, owner.Name, module, ShimScope()));
            accessor.Parameters.Add(new ParameterDefinition(field.DeclaringType));
            if (!isLoad) accessor.Parameters.Add(new ParameterDefinition(field.FieldType));
            ins.OpCode = OpCodes.Call;
            ins.Operand = accessor;
            Changes++;
        }

        // Unity 2020.2's Mono can't dispatch default interface methods and fails to load any type relying on one.
        // Copy each default body into implementors lacking an implementation, then make the interface method abstract.
        private void FlattenDefaultInterfaceMethods() {
            var defaults = module.GetTypes()
                .Where(t => t.IsInterface)
                .SelectMany(i => i.Methods.Where(m => !m.IsStatic && m.HasBody).Select(m => (Interface: i, Method: m)))
                .ToList();
            if (defaults.Count == 0) return;

            foreach (var type in module.GetTypes().Where(t => !t.IsInterface))
            foreach (var (iface, method) in defaults)
                if (Implements(type, iface) && !HasImplementation(type, method)) {
                    type.Methods.Add(CloneAsExplicitImplementation(iface, method));
                    Changes++;
                }

            foreach (var (iface, method) in defaults) {
                // Implementors can't provide non-public members, so those keep their body.
                if (!method.IsPublic) {
                    d.log($"[UnityDowngrader] {module.Name}: non-public default member {iface.FullName}.{method.Name} left intact");
                    continue;
                }
                method.Body = null;
                method.IsAbstract = true;
                method.IsVirtual = true;
                method.IsNewSlot = true;
                Changes++;
            }
        }

        private bool Implements(TypeDefinition type, TypeDefinition iface) {
            for (var t = type; t != null; t = ResolveInHierarchy(t.BaseType))
                if (t.Interfaces.Any(i => ResolveInHierarchy(i.InterfaceType) is { } resolved && (resolved == iface || Implements(resolved, iface))))
                    return true;
            return false;
        }

        private bool HasImplementation(TypeDefinition type, MethodDefinition ifaceMethod) {
            for (var t = type; t != null; t = ResolveInHierarchy(t.BaseType))
                if (t.Methods.Any(m =>
                        m.Overrides.Any(o => o.Name == ifaceMethod.Name && o.DeclaringType.FullName == ifaceMethod.DeclaringType.FullName)
                        || m.Name == ifaceMethod.Name && SameSignature(m, ifaceMethod)))
                    return true;
            return false;
        }

        // Null for types from an optional host assembly the host doesn't ship; they can't implement our interfaces.
        private TypeDefinition? ResolveInHierarchy(TypeReference? tr) {
            if (tr == null) return null;
            try {
                return tr.Resolve();
            } catch (AssemblyResolutionException e) when (d.IsMissingOptional(e.AssemblyReference.Name)) {
                return null;
            }
        }

        private static bool SameSignature(MethodDefinition a, MethodDefinition b) =>
            a.ReturnType.FullName == b.ReturnType.FullName
            && a.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(b.Parameters.Select(p => p.ParameterType.FullName));

        private MethodDefinition CloneAsExplicitImplementation(TypeDefinition iface, MethodDefinition source) {
            const MethodAttributes attributes = MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.Virtual
                                                | MethodAttributes.NewSlot | MethodAttributes.HideBySig;
            var clone = new MethodDefinition($"{iface.FullName}.{source.Name}", attributes, source.ReturnType) {
                IsSpecialName = source.IsSpecialName,
            };
            foreach (var p in source.Parameters) clone.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));

            var body = clone.Body;
            body.InitLocals = source.Body.InitLocals;
            body.MaxStackSize = source.Body.MaxStackSize;
            foreach (var v in source.Body.Variables) body.Variables.Add(new VariableDefinition(v.VariableType));

            var map = new Dictionary<Instruction, Instruction>();
            foreach (var ins in source.Body.Instructions) {
                var copy = Instruction.Create(OpCodes.Nop);
                copy.OpCode = ins.OpCode;
                copy.Operand = ins.Operand;
                map[ins] = copy;
                body.Instructions.Add(copy);
            }
            foreach (var copy in body.Instructions) {
                copy.Operand = copy.Operand switch {
                    Instruction target => map[target],
                    Instruction[] targets => targets.Select(t => map[t]).ToArray(),
                    ParameterDefinition { Index: >= 0 } p => clone.Parameters[p.Index],
                    VariableDefinition v => body.Variables[v.Index],
                    var other => other,
                };
            }
            foreach (var h in source.Body.ExceptionHandlers) {
                body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType) {
                    CatchType = h.CatchType,
                    TryStart = MapOrNull(h.TryStart),
                    TryEnd = MapOrNull(h.TryEnd),
                    HandlerStart = MapOrNull(h.HandlerStart),
                    HandlerEnd = MapOrNull(h.HandlerEnd),
                    FilterStart = MapOrNull(h.FilterStart),
                });
            }
            clone.Overrides.Add(module.ImportReference(source));
            return clone;

            Instruction? MapOrNull(Instruction? i) => i == null ? null : map[i];
        }

        private IEnumerable<MethodBody> Bodies() =>
            module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody).Select(m => m.Body);

        private AssemblyNameReference ShimScope() => Scope(d.shim);

        private AssemblyNameReference Scope(AssemblyNameReference target) {
            var existing = module.AssemblyReferences.FirstOrDefault(r => r.Name == target.Name);
            if (existing != null) return existing;
            var reference = new AssemblyNameReference(target.Name, target.Version) { PublicKeyToken = target.PublicKeyToken };
            module.AssemblyReferences.Add(reference);
            return reference;
        }

        private static MethodReference Element(MethodReference m) => m is GenericInstanceMethod g ? g.ElementMethod : m;

        // AssemblyResolutionException means the scope assembly isn't available on the host.
        private static bool Resolves(TypeReference tr) {
            try {
                return tr.Resolve() != null;
            } catch (AssemblyResolutionException) {
                return false;
            }
        }

        private static bool ResolvesMethod(MethodReference mr) {
            try {
                return mr.Resolve() != null;
            } catch (AssemblyResolutionException) {
                return false;
            }
        }
    }
}
