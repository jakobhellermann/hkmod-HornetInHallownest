// usage: apigapcheck --host <hostManagedDir> [--probe <dir>]... <assembly.dll>...
// Exit 1 if any assembly references something the host cannot provide.
using Mono.Cecil;

var hostDir = "";
var probeDirs = new List<string>();
var allowed = new List<string>();
var inputs = new List<string>();
for (var i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--host": hostDir = args[++i]; break;
        case "--probe": probeDirs.Add(args[++i]); break;
        // Substring match for a gap that is known-unreachable and deliberately NOT shimmed. Keep the list short
        // and justify every entry at the call site.
        case "--allow": allowed.Add(args[++i]); break;
        default: inputs.Add(args[i]); break;
    }
}
if (hostDir == "" || inputs.Count == 0) {
    Console.Error.WriteLine("usage: apigapcheck --host <dir> [--probe <dir>]... <assembly.dll>...");
    return 2;
}

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(hostDir);
foreach (var d in probeDirs) resolver.AddSearchDirectory(d);

TypeDefinition R(TypeReference tr) { try { return tr?.Resolve(); } catch { return null; } }

var offenders = 0;
foreach (var path in inputs.OrderBy(x => x)) {
    var types = new SortedSet<string>();
    var members = new SortedSet<string>();
    AssemblyDefinition asm;
    try { asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver }); }
    catch (Exception e) { Console.WriteLine($"### {Path.GetFileName(path)}: unreadable: {e.Message}"); offenders++; continue; }

    foreach (var mod in asm.Modules) {
        foreach (var tr in mod.GetTypeReferences())
            if (R(tr) == null && !allowed.Any(a => tr.FullName.Contains(a)))
                types.Add($"{tr.Scope?.Name}: {tr.FullName}");
        foreach (var mr in mod.GetMemberReferences()) {
            var dt = mr.DeclaringType;
            if (dt == null || R(dt) == null) continue;              // whole type missing: already reported
            if (mr.FullName.Contains("[0...")) continue;             // multi-dim array intrinsics: CLR-internal
            object rd = null;
            try { rd = mr is MethodReference m ? (object)m.Resolve() : mr is FieldReference f ? (object)f.Resolve() : null; } catch { }
            if (rd == null && !allowed.Any(a => mr.FullName.Contains(a)))
                members.Add($"{dt.Scope?.Name}: {mr.FullName}");
        }
    }

    if (types.Count + members.Count == 0) continue;
    offenders++;
    Console.WriteLine($"### {Path.GetFileName(path)}: {types.Count} unresolvable type(s), {members.Count} member(s)");
    foreach (var s in types) Console.WriteLine("    T " + s);
    foreach (var s in members) Console.WriteLine("    M " + s);
}

if (allowed.Count > 0) Console.WriteLine($"({allowed.Count} known-unreachable gap(s) allowlisted)");
Console.WriteLine(offenders == 0
    ? "no unresolvable references: every assembly's API surface is satisfied by the host"
    : $"{offenders} assembly(ies) reference APIs the host does not provide");
return offenders == 0 ? 0 : 1;
