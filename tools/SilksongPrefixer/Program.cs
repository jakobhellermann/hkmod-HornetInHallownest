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

// Read all inputs; build original-name -> new-name map for the set.
var asms = inputs.Select(p => AssemblyDefinition.ReadAssembly(p, readerParams)).ToList();
var rename = asms.ToDictionary(a => a.Name.Name, a => NewName(a.Name.Name));

// ASSEMBLY-ONLY rename: we change ONLY the assembly identity, NOT type namespaces/class names. Renaming the assembly
// is required to avoid a simple-name collision with HK's own Assembly-CSharp and to let the (remapped) MonoScripts
// bind the bundle's components to OUR copy. Type names are left intact so that name-based runtime resolution keeps
// working — PlayMaker looks up FsmStateActions by full type name, and Unity resolves nested [Serializable] classes
// by name too; prefixing namespaces broke both. The shadow seam still holds: a renamed assembly's internal type
// references (e.g. HeroController -> GameManager) resolve within the same (renamed) assembly, not to HK's.
foreach (var asm in asms) {
    var module = asm.MainModule;

    // Rename cross-references to other in-set assemblies (the scope of all their type refs follows automatically).
    foreach (var r in module.AssemblyReferences)
        if (rename.TryGetValue(r.Name, out var nn)) r.Name = nn;

    // Rename the assembly itself.
    var newName = rename[asm.Name.Name];
    asm.Name.Name = newName;
    module.Name = newName + ".dll";
    var outPath = Path.Combine(outDir, newName + ".dll");
    asm.Write(outPath);
    Console.WriteLine($"wrote {outPath} ({module.Types.Count} types)");
}

return 0;
