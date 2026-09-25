// Rewrite Silksong's Unity 6 IL so it resolves against Hollow Knight 1.5.78.11833 (Unity 2020.2.2f1).
//
// Three kinds of gap, measured with a Cecil resolve sweep against HK's UnityEngine (11 types, 40 members):
//   1. RENAMED members  — Unity 6 renamed things that still exist (Rigidbody2D.linearVelocity -> velocity,
//                         Collider2D.Overlap -> OverlapCollider, …). Rewrite the member name in place.
//   2. RENAMED types    — PhysicsMaterial -> PhysicMaterial, InputField/EndEditEvent -> InputField/SubmitEvent.
//   3. ABSENT API       — no 2020.2 equivalent (UnityEngine.Pool, FindObjectsByType, Collider2D layer overrides,
//                         RefreshRate, VideoPlayer.timeUpdateMode). Retarget onto HornetInHallownest.UnityCompat.
//
// TypeReferences and MemberReferences are deduplicated per module, so mutating them in place updates every use site.
// Instance -> static redirection additionally needs the receiver prepended as a parameter, HasThis cleared, and any
// `callvirt` at the call site downgraded to `call`.
//
// usage: UnityDowngrader --hk <hkManagedDir> --shim <UnityCompat.dll> <assembly.dll>...

using Mono.Cecil;
using Mono.Cecil.Cil;

var hkManaged = "";
var shimPath = "";
var inputs = new List<string>();
for (var i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--hk": hkManaged = args[++i]; break;
        case "--shim": shimPath = args[++i]; break;
        default: inputs.Add(args[i]); break;
    }
}
if (hkManaged == "" || shimPath == "" || inputs.Count == 0) {
    Console.Error.WriteLine("usage: UnityDowngrader --hk <hkManagedDir> --shim <UnityCompat.dll> <assembly.dll>...");
    return 1;
}

// Two shim classes: Unity-surface stand-ins and BCL stand-ins. Methods are addressed by name across both.
string[] CompatTypes = ["UnityEngine.Compat.HornetCompat", "UnityEngine.Compat.BclCompat"];

// ---- rewrite tables -------------------------------------------------------------------------------------------

// Unity 6 type -> 2020.2 type that still exists under a different name. (namespace, name)
var typeRenames = new Dictionary<string, (string Ns, string Name)> {
    ["UnityEngine.PhysicsMaterial"] = ("UnityEngine", "PhysicMaterial"),
    ["UnityEngine.UI.InputField/EndEditEvent"] = ("", "SubmitEvent"), // nested: namespace unused
};

// Types with no 2020.2 counterpart -> provided by the shim assembly (same namespace + name, different assembly).
var typesToShim = new HashSet<string> {
    "UnityEngine.FindObjectsInactive",
    "UnityEngine.FindObjectsSortMode",
    "UnityEngine.RefreshRate",
    "UnityEngine.Pool.IObjectPool`1",
    "UnityEngine.Pool.ObjectPool`1",
    "UnityEngine.Pool.LinkedPool`1",
    "UnityEngine.Video.VideoTimeUpdateMode",
    "UnityEngine.UIElements.PanelSettings",
    "UnityEngine.UIElements.UIDocument",
    "UnityEngine.AssetBundleUnloadOperation",
    "Tk2dGlobalEvents",   // nested IListener resolves through its declaring type
};

// (declaring type, old member) -> new member on the SAME type. Pure Unity 6 renames.
var memberRenames = new Dictionary<(string Type, string Member), string> {
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

// Nested Unity/tk2d types with nowhere to live on the host (you cannot add a nested type to a forwarded type):
// un-nest onto a top-level shim type. value = shim type name in namespace UnityEngine.Compat.
var nestedToTopLevel = new Dictionary<string, string> {
    ["tk2dSpriteAnimator/AnimationCompletedDelegate"] = "Tk2dAnimationCompletedDelegate",
    ["tk2dSpriteAnimator/AnimationEventDelegate"] = "Tk2dAnimationEventDelegate",
    ["tk2dSpriteAnimator/AnimationChangedDelegate"] = "Tk2dAnimationChangedDelegate",
};

// (declaring type, field) -> shim get_/set_ pair. ldfld/stfld have the same stack shape as a static call taking
// the receiver first, so the swap needs no surrounding IL changes.
var fieldToShim = new Dictionary<(string Type, string Field), string> {
    [("tk2dSpriteAnimator", "FrameChanged")] = "tk2dSpriteAnimator_FrameChanged",
};

// (declaring type, member) -> static method on HornetCompat taking the receiver as its first parameter.
var memberToShim = new Dictionary<(string Type, string Member), string> {
    [("UnityEngine.Object", "FindAnyObjectByType")] = "FindAnyObjectByType",
    [("UnityEngine.Object", "FindObjectsByType")] = "FindObjectsByType",
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
    [("UnityEngine.VideoPlayer", "get_canSetTimeUpdateMode")] = "VideoPlayer_get_canSetTimeUpdateMode",
    [("UnityEngine.Video.VideoPlayer", "get_canSetTimeUpdateMode")] = "VideoPlayer_get_canSetTimeUpdateMode",
    [("UnityEngine.Video.VideoPlayer", "get_timeUpdateMode")] = "VideoPlayer_get_timeUpdateMode",
    [("UnityEngine.Video.VideoPlayer", "set_timeUpdateMode")] = "VideoPlayer_set_timeUpdateMode",
};
// BCL members .NET Standard 2.1 has and Unity 2020.2's profile does not.
memberToShim[("System.String", "Contains")] = "String_Contains";
memberToShim[("System.String", "Split")] = "String_Split";
memberToShim[("System.String", "TrimStart")] = "String_TrimStart";
memberToShim[("System.String", "Create")] = "String_Create";
memberToShim[("System.TimeSpan", "op_Multiply")] = "TimeSpan_Multiply";
memberToShim[("System.ArraySegment`1", "get_Empty")] = "ArraySegment_Empty";

// GOG Galaxy SDK drift (static method, so no receiver is prepended).
memberToShim[("Galaxy.Api.GalaxyInstance", "ShutdownEx")] = "GalaxyInstance_ShutdownEx";

// tk2dSprite material keywords: in Silksong's tk2d, absent from the host's.
memberToShim[("tk2dSprite", "get_HasKeywordsDefined")] = "tk2dSprite_get_HasKeywordsDefined";
memberToShim[("tk2dSprite", "EnableKeyword")] = "tk2dSprite_EnableKeyword";
memberToShim[("tk2dSprite", "DisableKeyword")] = "tk2dSprite_DisableKeyword";
memberToShim[("tk2dSprite", "MoveKeywords")] = "tk2dSprite_MoveKeywords";
memberToShim[("tk2dSprite", "SetFloat")] = "tk2dSprite_SetFloat";

foreach (var e in new[] { "AnimationCompletedEvent", "AnimationEventTriggeredEvent", "AnimationChanged" }) {
    memberToShim[("tk2dSpriteAnimator", "add_" + e)] = "tk2dSpriteAnimator_add_" + e;
    memberToShim[("tk2dSpriteAnimator", "remove_" + e)] = "tk2dSpriteAnimator_remove_" + e;
}
foreach (var p in new[] { "excludeLayers", "includeLayers", "callbackLayers", "contactCaptureLayers",
                          "forceSendLayers", "forceReceiveLayers", "layerOverridePriority" }) {
    memberToShim[("UnityEngine.Collider2D", "get_" + p)] = "Collider2D_get_" + p;
    memberToShim[("UnityEngine.Collider2D", "set_" + p)] = "Collider2D_set_" + p;
}

// ---- rewrite ---------------------------------------------------------------------------------------------------

var shimAsm = AssemblyDefinition.ReadAssembly(shimPath);
var shimClasses = CompatTypes
    .Select(n => shimAsm.MainModule.GetType(n) ?? throw new InvalidOperationException($"{n} not in shim"))
    .ToArray();
// method name -> the shim class declaring it
var shimOwner = shimClasses
    .SelectMany(c => c.Methods.Where(m => m.IsStatic).Select(m => (m.Name, Owner: c)))
    .GroupBy(x => x.Name)
    .ToDictionary(g => g.Key, g => g.First().Owner);
// Types the shim supplies outright (HashCode, MathF, SpanAction, …), for the facade-gap pass.
var shimProvidedTypes = shimAsm.MainModule.GetTypes().Select(t => t.FullName).ToHashSet();

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(hkManaged);
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(shimPath))!);
foreach (var input in inputs) resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(input))!);

// Scopes that must be resolved against the HOST even though we ship a copy: tk2d has to be a single shared type
// identity across the ported hero code and the sprite bundles.
var ForceToHost = new HashSet<string> { "TeamCherry.TK2D" };

// Names of the assemblies we ship ourselves; authoritative, never rehomed (except ForceToHost).
var ourAssemblies = inputs.Select(Path.GetFileNameWithoutExtension).ToHashSet();

// Every assembly the host ships, so an unresolvable type reference can be pointed at whoever really defines it.
//
// This replaces the type-forwarder facade approach outright. A synthetic facade full of ExportedType rows loads
// fine under a modern Mono but Unity 2020.2's runtime dies following the very first forward, so instead of
// inventing an assembly that claims to own Silksong's names, the references are rewritten to name the host's
// assembly directly — which is where HK keeps tk2d (inside Assembly-CSharp) anyway.
//
// BCL facades come first so System.* types resolve to mscorlib rather than some incidental game assembly.
var hostProviders = new[] { "mscorlib", "System.Core", "System", "netstandard" }
    .Concat(Directory.GetFiles(hkManaged, "*.dll").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n))
    .Distinct()
    .Select(n => Path.Combine(hkManaged, n + ".dll"))
    .Where(File.Exists)
    .Select(p => {
        try {
            var a = AssemblyDefinition.ReadAssembly(p);
            return (Reference: (AssemblyNameReference?)new AssemblyNameReference(a.Name.Name, a.Name.Version) {
                        PublicKeyToken = a.Name.PublicKeyToken,
                    },
                    Types: a.MainModule.GetTypes().Where(t => t.IsPublic).Select(t => t.FullName).ToHashSet());
        } catch {
            return (Reference: (AssemblyNameReference?)null, Types: new HashSet<string>());
        }
    })
    .Where(x => x.Reference != null)
    .ToArray();

var grandTotal = 0;
foreach (var input in inputs) {
    // InMemory reads the whole file and releases the handle, so writing back to the same path is safe.
    // ReadWrite must stay FALSE here: it makes Cecil hold its own writable stream on the file, and the later
    // Write(input) then opens a SECOND stream onto the same path. The result passes a structural metadata check
    // but Mono rejects it ("Assertion ... index < meta->heap_blob.size").
    var asm = AssemblyDefinition.ReadAssembly(input,
        new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false, InMemory = true });
    var module = asm.MainModule;

    var shimRef = new AssemblyNameReference(shimAsm.Name.Name, shimAsm.Name.Version);
    var shimScope = module.AssemblyReferences.FirstOrDefault(a => a.Name == shimAsm.Name.Name);
    if (shimScope == null) { module.AssemblyReferences.Add(shimRef); shimScope = shimRef; }
    var shimTypeRefs = shimClasses.ToDictionary(
        c => c.FullName,
        c => new TypeReference(c.Namespace, c.Name, module, shimScope));
    TypeReference ShimTypeFor(string methodName) => shimTypeRefs[shimOwner[methodName].FullName];
    var shimTypeRef = shimTypeRefs["UnityEngine.Compat.HornetCompat"];

    int types = 0, renamed = 0, shimmed = 0, rebound = 0, refaced = 0;

    // Bind shared dependencies to the HOST's copies. Silksong links Newtonsoft.Json 13.0.0.0; HK 1.5.78 ships
    // 11.0.0.0. Two copies of the same simple name in one process is worse than one slightly older copy, and the
    // APIs these assemblies touch are stable across the gap.
    foreach (var reference in module.AssemblyReferences) {
        var hostCopy = Path.Combine(hkManaged, reference.Name + ".dll");
        if (!File.Exists(hostCopy)) continue;
        var hostName = AssemblyDefinition.ReadAssembly(hostCopy).Name;
        var hostToken = hostName.PublicKeyToken ?? [];
        var refToken = reference.PublicKeyToken ?? [];
        if (reference.Version == hostName.Version && refToken.SequenceEqual(hostToken)) continue;

        // Match the host's full identity, strong name included: HK ships an UNSIGNED Newtonsoft.Json while
        // Silksong's assemblies reference the signed build, and a token mismatch fails resolution just as hard
        // as a version mismatch.
        static string Tok(byte[] t) => t.Length == 0 ? "unsigned" : BitConverter.ToString(t).Replace("-", "").ToLowerInvariant();
        Console.WriteLine($"  rebind {reference.Name} {reference.Version}/{Tok(refToken)} -> {hostName.Version}/{Tok(hostToken)}");
        reference.Version = hostName.Version;
        reference.PublicKeyToken = hostToken.Length == 0 ? null : hostToken;
        rebound++;
    }

    // 1b. netstandard facade gap. HK 1.5.78's mscorlib DEFINES System.Span<T>/ReadOnlySpan<T>, but its
    // netstandard *2.0* facade does not forward them (2.1 does, which is what Silksong compiled against). Once the
    // reference is rebound to the host's 2.0 facade the typeref dangles, and Unity.ResourceManager — whose
    // BinaryStorageBuffer reads catalog.bin through Span — fails to load. Point such typerefs at the host assembly
    // that really provides the type.
    foreach (var tr in module.GetTypeReferences().ToList()) {
        var scopeName = tr.Scope?.Name ?? "";
        if (scopeName == shimAsm.Name.Name) continue;   // already ours

        // Our OWN shipped assemblies are authoritative and must never be rehomed. The prefixer renamed
        // Silksong's Assembly-CSharp/firstpass/PlayMaker/... precisely so Hornet binds to an isolated copy;
        // rehoming those references to the host's same-named (and older) assemblies destroys that isolation —
        // it silently pointed 119 PlayMaker references at HK's PlayMaker before this guard existed.
        //
        // tk2d is the deliberate exception: it MUST be shared with the host, or the ported HeroController and the
        // sprite bundles disagree about what a tk2dSpriteCollectionData is.
        if (ourAssemblies.Contains(scopeName) && !ForceToHost.Contains(scopeName)) continue;

        // Decide from what the HOST actually ships, not from what Cecil can resolve: this tool runs on .NET, whose
        // DefaultAssemblyResolver cheerfully satisfies System.Runtime from the host runtime directory — a facade
        // Hollow Knight 1.5.78 does not have in Managed/ or in any Mono BCL copy. If the host has no such assembly,
        // every typeref through it needs rehoming regardless of what resolves here.
        var hostShipsScope = File.Exists(Path.Combine(hkManaged, scopeName + ".dll"));
        if (hostShipsScope && SafeResolve(tr) != null) continue;
        AssemblyNameReference target;
        var provider = hostProviders.FirstOrDefault(h => h.Types.Contains(tr.FullName));
        if (provider.Reference != null) {
            target = provider.Reference;
        } else if (shimProvidedTypes.Contains(tr.FullName)) {
            // Not in the host at all (System.HashCode, System.MathF, System.Buffers.SpanAction`2, …) — the shim
            // defines it under the original namespace, so only the assembly identity changes.
            target = new AssemblyNameReference(shimAsm.Name.Name, shimAsm.Name.Version);
        } else {
            continue;
        }
        var scope = module.AssemblyReferences.FirstOrDefault(a => a.Name == target.Name);
        if (scope == null) { module.AssemblyReferences.Add(target); scope = target; }
        Console.WriteLine($"  reface {tr.FullName}: {scopeName} -> {scope.Name}");
        tr.Scope = scope;
        refaced++;
    }

    // 1+2. Types. Mutating the shared TypeReference updates every signature and instruction that names it.
    foreach (var tr in module.GetTypeReferences().ToList()) {
        var full = tr.FullName;
        if (typeRenames.TryGetValue(full, out var to)) {
            if (to.Ns != "") tr.Namespace = to.Ns;
            tr.Name = to.Name;
            types++;
        } else if (typesToShim.Contains(full)) {
            tr.Scope = shimScope;
            types++;
        } else if (nestedToTopLevel.TryGetValue(full, out var topLevel)) {
            tr.DeclaringType = null;          // must clear first: a nested ref takes its scope from its parent
            tr.Namespace = "UnityEngine.Compat";
            tr.Name = topLevel;
            tr.Scope = shimScope;
            types++;
        }
    }

    // 3. Members. Driven from instruction operands, NOT module.GetMemberReferences(): Cecil materialises those
    // table rows separately from the references a method body's operands point at, so mutating the table copies
    // leaves the actual IL untouched (verified — type refs persisted, member refs silently did not).
    var rewritten = new HashSet<MethodReference>();
    var madeStatic = new HashSet<MethodReference>();
    var rewrittenGeneric = new Dictionary<MethodReference, GenericInstanceMethod>();

    void RewriteCallee(MethodReference callee) {
        var method = Element(callee);
        if (!rewritten.Add(method)) return;              // shared across call sites; mutate exactly once
        // Key on the OPEN type for members of generic types: ArraySegment<FullQuestBase>.get_Empty() has a
        // declaring FullName of "System.ArraySegment`1<FullQuestBase>", which no table entry would ever match.
        var declaring = method.DeclaringType is GenericInstanceType gi ? gi.ElementType.FullName
                                                                      : method.DeclaringType.FullName;
        var key = (declaring, method.Name);

        if (memberRenames.TryGetValue(key, out var newName)) {
            method.Name = newName;
            renamed++;
        } else if (memberToShim.TryGetValue(key, out var shimName)) {
            // Only redirect what the host genuinely lacks. The table is keyed by NAME, so without this guard an
            // entry for Component.GetComponentInParent(bool) also captures the plain GetComponentInParent() that
            // 2020.2 has perfectly well — and Screen.SetResolution's other overloads likewise. Redirecting those
            // to a shim that has no matching signature swaps a working call for a MissingMethodException.
            if (SafeResolveMethod(method) != null) return;
            if (!shimOwner.ContainsKey(shimName))
                throw new InvalidOperationException($"shim missing {shimName}");
            var shimTarget = ShimTypeFor(shimName);
            if (method.HasThis) {
                // The static shim takes the receiver as parameter 0; struct receivers arrive as managed pointers.
                var receiver = method.DeclaringType.IsValueType
                    ? new ByReferenceType(method.DeclaringType)
                    : (TypeReference)method.DeclaringType;
                method.Parameters.Insert(0, new ParameterDefinition(module.ImportReference(receiver)));
                method.HasThis = false;
                madeStatic.Add(method);
            }
            // Static member on a GENERIC type (ArraySegment<T>.Empty): carry the type argument over onto a
            // generic shim method, else the instantiation is lost and the signature no longer matches.
            var genericOwner = method.DeclaringType as GenericInstanceType;
            method.DeclaringType = shimTarget;
            method.Name = shimName;
            if (genericOwner != null && !method.HasThis) {
                var shimDef = shimOwner[shimName].Methods.First(m => m.Name == shimName);
                if (shimDef.HasGenericParameters
                    && shimDef.GenericParameters.Count == genericOwner.GenericArguments.Count) {
                    // The genericity was on the DECLARING TYPE, so the original reference declares no generic
                    // parameters of its own — reshaping it would yield a MethodSpec over a non-generic method.
                    // Importing the shim definition produces a reference with the right arity and return type;
                    // the type argument then moves onto the method.
                    var imported = module.ImportReference(shimDef);
                    var instance = new GenericInstanceMethod(imported);
                    foreach (var arg in genericOwner.GenericArguments) instance.GenericArguments.Add(arg);
                    rewrittenGeneric[method] = instance;
                }
            }
            shimmed++;
        }
    }

    foreach (var type in module.GetTypes())
    foreach (var m in type.Methods) {
        if (!m.HasBody) continue;
        foreach (var ins in m.Body.Instructions)
            if (ins.Operand is MethodReference callee)
                RewriteCallee(callee);
    }

    // Fields -> shim accessors. Build the MethodReference from the FieldReference's own types so generic field
    // types (Action<tk2dSpriteAnimator,...>) come across already imported into this module.
    var fieldAccessor = new Dictionary<(string, bool), MethodReference>();
    foreach (var type in module.GetTypes())
    foreach (var m in type.Methods) {
        if (!m.HasBody) continue;
        foreach (var ins in m.Body.Instructions) {
            if (ins.Operand is not FieldReference fr) continue;
            var isLoad = ins.OpCode == OpCodes.Ldfld;
            var isStore = ins.OpCode == OpCodes.Stfld;
            if (!isLoad && !isStore) continue;
            if (!fieldToShim.TryGetValue((fr.DeclaringType.FullName, fr.Name), out var stem)) continue;

            var key = (stem, isLoad);
            if (!fieldAccessor.TryGetValue(key, out var accessor)) {
                // "tk2dSpriteAnimator_FrameChanged" -> "tk2dSpriteAnimator_get_FrameChanged"
                var full = stem.Insert(stem.LastIndexOf('_') + 1, isLoad ? "get_" : "set_");
                accessor = new MethodReference(full, isLoad ? fr.FieldType : module.TypeSystem.Void, shimTypeRef) {
                    HasThis = false,
                };
                accessor.Parameters.Add(new ParameterDefinition(fr.DeclaringType));
                if (!isLoad) accessor.Parameters.Add(new ParameterDefinition(fr.FieldType));
                fieldAccessor[key] = accessor;
                shimmed++;
            }
            ins.OpCode = OpCodes.Call;
            ins.Operand = accessor;
        }
    }

    if (rewrittenGeneric.Count > 0) {
        foreach (var type in module.GetTypes())
        foreach (var m in type.Methods) {
            if (!m.HasBody) continue;
            foreach (var ins in m.Body.Instructions)
                if (ins.Operand is MethodReference mr && rewrittenGeneric.TryGetValue(mr, out var inst))
                    ins.Operand = inst;
        }
    }

    // callvirt is illegal on a static target — downgrade the call sites we just made static.
    if (madeStatic.Count > 0) {
        foreach (var type in module.GetTypes())
        foreach (var m in type.Methods) {
            if (!m.HasBody) continue;
            foreach (var ins in m.Body.Instructions)
                if (ins.OpCode == OpCodes.Callvirt && ins.Operand is MethodReference callee
                    && madeStatic.Contains(Element(callee)))
                    ins.OpCode = OpCodes.Call;
        }
    }

    // 4. Default interface methods. Unity 2020.2's Mono cannot dispatch them at all ("C# 8 except default
    // interface methods"), so any implementor that relies on the interface's body fails at TYPE LOAD, not at the
    // call. Copy each default body down into the implementors that don't already override it, as an explicit
    // interface implementation. Interface and implementors share this module, so operands need no re-importing.
    var flattened = 0;
    var abstracted = 0;
    var defaults = module.GetTypes()
        .Where(t => t.IsInterface)
        .SelectMany(i => i.Methods.Where(m => !m.IsStatic && m.HasBody && m.Body.Instructions.Count > 0)
                          .Select(m => (Interface: i, Method: m)))
        .ToList();

    if (defaults.Count > 0) {
        foreach (var type in module.GetTypes()) {
            if (type.IsInterface || type.IsAbstract && type.IsSealed) continue;
            foreach (var (iface, method) in defaults) {
                if (!Implements(type, iface)) continue;
                if (HasImplementation(type, method)) continue;
                type.Methods.Add(CloneAsExplicitImpl(type, iface, method));
                flattened++;
            }
        }

        // Pushing the bodies down is only half the job. Mono 2020.2 has NO default-interface-method support, so an
        // interface that still carries a method body is a problem the moment the type is loaded at all — and Unity
        // loads every type in the assemblies listed in ScriptingAssemblies.json when it scans for serializable
        // types. Strip the bodies so these read as ordinary abstract interface methods.
        //
        // Non-public default members are left alone: an implementor cannot supply them, so there is nothing to push
        // down and making them abstract would be worse than leaving them.
        foreach (var (iface, method) in defaults) {
            if (!method.IsPublic) {
                Console.WriteLine($"  NOTE non-public default member left intact: {iface.FullName}.{method.Name}");
                continue;
            }
            method.Body = null;
            method.IsAbstract = true;
            method.IsVirtual = true;
            method.IsNewSlot = true;
            abstracted++;
        }
    }

    var total = types + renamed + shimmed + rebound + refaced + flattened + abstracted;
    grandTotal += total;
    Console.WriteLine($"{Path.GetFileName(input)}: {types} types, {renamed} member renames, {shimmed} -> shim, {rebound} rebound, {refaced} refaced, {flattened} DIM flattened, {abstracted} DIM made abstract");
    if (total > 0) asm.Write(input);
    asm.Dispose();
}
Console.WriteLine($"total rewrites: {grandTotal}");
return 0;

static MethodReference Element(MethodReference m) => m is GenericInstanceMethod g ? g.ElementMethod : m;

// Does `type` implement `iface`, directly or through a base class / interface inheritance chain?
static bool Implements(TypeDefinition? type, TypeDefinition iface) {
    for (var t = type; t != null; t = Resolve(t.BaseType)) {
        foreach (var i in t.Interfaces) {
            var resolved = Resolve(i.InterfaceType);
            if (resolved == null) continue;
            if (resolved == iface || Implements(resolved, iface)) return true;
        }
    }
    return false;
}

static TypeDefinition? Resolve(TypeReference? tr) { try { return tr?.Resolve(); } catch { return null; } }

static TypeDefinition? SafeResolve(TypeReference tr) { try { return tr.Resolve(); } catch { return null; } }

// Resolves against the HOST's assemblies (the resolver's search dirs), so a non-null result means "2020.2 already
// has this member" — the signal used to avoid redirecting members that need no shim.
static MethodDefinition? SafeResolveMethod(MethodReference mr) { try { return mr.Resolve(); } catch { return null; } }

// An implementation already exists if the type (or a base) declares an explicit override of the interface method,
// or a public virtual method matching its name and signature.
static bool HasImplementation(TypeDefinition type, MethodDefinition ifaceMethod) {
    for (var t = (TypeDefinition?)type; t != null; t = Resolve(t.BaseType)) {
        foreach (var m in t.Methods) {
            if (m.Overrides.Any(o => o.Name == ifaceMethod.Name
                                     && o.DeclaringType.FullName == ifaceMethod.DeclaringType.FullName))
                return true;
            if (m.Name == ifaceMethod.Name && SameSignature(m, ifaceMethod)) return true;
        }
    }
    return false;
}

static bool SameSignature(MethodDefinition a, MethodDefinition b)
    => a.Parameters.Count == b.Parameters.Count
       && a.ReturnType.FullName == b.ReturnType.FullName
       && a.Parameters.Zip(b.Parameters, (x, y) => x.ParameterType.FullName == y.ParameterType.FullName).All(ok => ok);

static MethodDefinition CloneAsExplicitImpl(TypeDefinition target, TypeDefinition iface, MethodDefinition source) {
    const MethodAttributes attrs = MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.Virtual
                                   | MethodAttributes.NewSlot | MethodAttributes.HideBySig;
    var clone = new MethodDefinition($"{iface.FullName}.{source.Name}", attrs, source.ReturnType) {
        IsSpecialName = source.IsSpecialName,
    };
    foreach (var p in source.Parameters)
        clone.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));

    var body = clone.Body;
    body.InitLocals = source.Body.InitLocals;
    body.MaxStackSize = source.Body.MaxStackSize;
    foreach (var v in source.Body.Variables) body.Variables.Add(new VariableDefinition(v.VariableType));

    // Same module: instruction operands (type/member refs, and the method's own parameters) stay valid as-is.
    var map = new Dictionary<Instruction, Instruction>();
    foreach (var ins in source.Body.Instructions) {
        var copy = Instruction.Create(OpCodes.Nop);
        copy.OpCode = ins.OpCode;
        copy.Operand = ins.Operand;
        map[ins] = copy;
        body.Instructions.Add(copy);
    }
    // Re-point branch targets at the cloned instructions.
    foreach (var copy in body.Instructions) {
        switch (copy.Operand) {
            case Instruction t when map.TryGetValue(t, out var mapped): copy.Operand = mapped; break;
            case Instruction[] ts: copy.Operand = ts.Select(t => map.TryGetValue(t, out var m) ? m : t).ToArray(); break;
            case ParameterDefinition pd when pd.Index >= 0: copy.Operand = clone.Parameters[pd.Index]; break;
            case VariableDefinition vd: copy.Operand = body.Variables[vd.Index]; break;
        }
    }
    foreach (var h in source.Body.ExceptionHandlers) {
        body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType) {
            CatchType = h.CatchType,
            TryStart = h.TryStart != null && map.TryGetValue(h.TryStart, out var a) ? a : null,
            TryEnd = h.TryEnd != null && map.TryGetValue(h.TryEnd, out var b) ? b : null,
            HandlerStart = h.HandlerStart != null && map.TryGetValue(h.HandlerStart, out var c) ? c : null,
            HandlerEnd = h.HandlerEnd != null && map.TryGetValue(h.HandlerEnd, out var d) ? d : null,
            FilterStart = h.FilterStart != null && map.TryGetValue(h.FilterStart, out var e) ? e : null,
        });
    }

    clone.Overrides.Add(target.Module.ImportReference(source));
    return clone;
}
