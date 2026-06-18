using Mono.Cecil;

// Prefix the namespaces of a SET of assemblies with <prefix>, rename each assembly, and rewrite cross-references
// among the set so they keep resolving. Shared/un-listed dependencies (UnityEngine, TeamCherry.*, PlayMaker, …) are
// left untouched and bind to whatever is loaded at runtime.
//
// Usage: SilksongPrefixer <prefix> <outDir> --managed <dir> <in1.dll> <in2.dll> ...
//   Each <inN.dll> is renamed to "<prefix>.<SanitizedAssemblyName>" and its types to "<prefix>.<originalNamespace>".

if (args.Length < 4) {
    Console.Error.WriteLine("usage: SilksongPrefixer <prefix> <outDir> --managed <dir> <dll>...");
    return 1;
}

var prefix = args[0];
var outDir = args[1];
string? managed = null;
var inputs = new List<string>();
for (var i = 2; i < args.Length; i++) {
    if (args[i] == "--managed") { managed = args[++i]; continue; }
    inputs.Add(args[i]);
}
managed ??= Path.GetDirectoryName(Path.GetFullPath(inputs[0]))!;
Directory.CreateDirectory(outDir);

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managed);
var readerParams = new ReaderParameters { AssemblyResolver = resolver };

string NewName(string original) => prefix + "." + original.Replace("-", "").Replace(".", "");
string Prefix(string ns) => ns.Length == 0 ? prefix : prefix + "." + ns;

// Read all inputs; build original-name -> new-name map for the set.
var asms = inputs.Select(p => AssemblyDefinition.ReadAssembly(p, readerParams)).ToList();
var rename = asms.ToDictionary(a => a.Name.Name, a => NewName(a.Name.Name));

foreach (var asm in asms) {
    var module = asm.MainModule;

    // 1. Rename cross-references to other in-set assemblies (scope of all their type refs follows automatically).
    var inSetRefs = new HashSet<AssemblyNameReference>();
    foreach (var r in module.AssemblyReferences)
        if (rename.TryGetValue(r.Name, out var nn)) { r.Name = nn; inSetRefs.Add(r); }

    // 2. Prefix this assembly's own type definitions.
    foreach (var t in module.Types) {
        if (t.Name == "<Module>") continue;
        t.Namespace = Prefix(t.Namespace);
    }

    // 3. Prefix the namespace of type references that point at in-set assemblies (cross-assembly type refs).
    foreach (var tr in module.GetTypeReferences()) {
        if (tr.DeclaringType != null) continue; // nested: namespace lives on the declaring type
        if (tr.Scope is AssemblyNameReference anr && inSetRefs.Contains(anr))
            tr.Namespace = Prefix(tr.Namespace);
    }

    // 3.5 Prefix type references buried in custom-attribute arguments (enum-typed args, typeof(...)). Cecil's rename
    //     of type defs/refs doesn't touch these — the attribute blob stores them by full name, so the runtime fails
    //     to decode them at type load (e.g. GlobalEnums.MapZone). Mutating the TypeReference + re-emitting fixes it.
    var mod = module;
    void PrefixRef(TypeReference? tr) {
        if (tr is GenericInstanceType git) {
            foreach (var ga in git.GenericArguments) PrefixRef(ga);
            return;
        }
        if (tr is null || tr.IsGenericParameter || tr.DeclaringType != null) return;
        var inSet = ReferenceEquals(tr.Scope, mod) || (tr.Scope is AssemblyNameReference a && inSetRefs.Contains(a));
        if (inSet && tr.Namespace != prefix && !tr.Namespace.StartsWith(prefix + "."))
            tr.Namespace = Prefix(tr.Namespace);
    }
    void FixArg(CustomAttributeArgument arg) {
        PrefixRef(arg.Type);
        if (arg.Value is TypeReference vtr) PrefixRef(vtr);
        else if (arg.Value is CustomAttributeArgument[] arr)
            foreach (var a2 in arr) FixArg(a2);
    }
    void FixAttrs(ICustomAttributeProvider? p) {
        if (p is null || !p.HasCustomAttributes) return;
        foreach (var ca in p.CustomAttributes) {
            foreach (var a in ca.ConstructorArguments) FixArg(a);
            foreach (var f in ca.Fields) FixArg(f.Argument);
            foreach (var pr in ca.Properties) FixArg(pr.Argument);
        }
    }
    FixAttrs(asm);
    FixAttrs(module);
    var stack = new Stack<TypeDefinition>(module.Types);
    while (stack.Count > 0) {
        var t = stack.Pop();
        FixAttrs(t);
        foreach (var n in t.NestedTypes) stack.Push(n);
        foreach (var f in t.Fields) FixAttrs(f);
        foreach (var pr in t.Properties) FixAttrs(pr);
        foreach (var ev in t.Events) FixAttrs(ev);
        foreach (var m in t.Methods) {
            FixAttrs(m);
            FixAttrs(m.MethodReturnType);
            foreach (var par in m.Parameters) FixAttrs(par);
        }
    }

    // 4. Rename the assembly itself.
    var newName = rename[asm.Name.Name];
    asm.Name.Name = newName;
    module.Name = newName + ".dll";
    var outPath = Path.Combine(outDir, newName + ".dll");
    asm.Write(outPath);
    Console.WriteLine($"wrote {outPath} ({module.Types.Count} types)");
}

return 0;
