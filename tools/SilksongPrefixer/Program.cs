using Mono.Cecil;

// Rewrite assembly identities to start with <prefix>. Also rewrites cross-references between those assemblies.
// Usage: SilksongPrefixer <prefix> <outDir> --managed <dir> <in1.dll> <in2.dll> ...
//   Each <inN.dll>'s assembly name becomes "<prefix>.<SanitizedAssemblyName>". Type namespaces and class names are
//   left intact (assembly-only rename, see the note below).

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

var resolver = new InMemoryResolver();
resolver.AddSearchDirectory(managed);
var readerParams = new ReaderParameters { AssemblyResolver = resolver };

string NewName(string original) => prefix + "." + original.Replace("-", "").Replace(".", "");

// Read all inputs; build original-name -> new-name map for the set.
var asms = inputs.Select(p => AssemblyDefinition.ReadAssembly(p, readerParams)).ToList();
var rename = asms.ToDictionary(a => a.Name.Name, a => NewName(a.Name.Name));

// I tried namespace prefixing as well, but it broke at least PlayMaker looking up FsmStateActions.
foreach (var asm in asms)
{
    var module = asm.MainModule;
    // Rename cross-references to other in-set assemblies
    foreach (var r in module.AssemblyReferences)
        if (rename.TryGetValue(r.Name, out var nn)) r.Name = nn;
    // Rename the assembly itself.
    var newName = rename[asm.Name.Name];
    asm.Name.Name = newName;
    module.Name = newName + ".dll";
}

// Register the renamed in-memory assemblies so cross-references (e.g. Silksong.AssemblyCSharp -> Silksong.PlayMaker)
// so that Cecil can resolve on write.
foreach (var asm in asms) resolver.Add(asm);

foreach (var asm in asms) {
    var outPath = Path.Combine(outDir, asm.Name.Name + ".dll");
    asm.Write(outPath);
    Console.WriteLine($"wrote {outPath} ({asm.MainModule.Types.Count} types)");
}

return 0;

// Resolver that serves the in-memory (renamed) assemblies by their new simple name
class InMemoryResolver : DefaultAssemblyResolver {
    private readonly Dictionary<string, AssemblyDefinition> registered = new();
    public void Add(AssemblyDefinition asm) => registered[asm.Name.Name] = asm;
    public override AssemblyDefinition Resolve(AssemblyNameReference name) =>
        registered.TryGetValue(name.Name, out var asm) ? asm : base.Resolve(name);
}
