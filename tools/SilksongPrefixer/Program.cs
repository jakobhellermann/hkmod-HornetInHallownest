using HornetInHallownest.Core;
using HornetInHallownest.Core.SilksongSetup;

// Rewrite assembly identities to start with <prefix>, rewriting cross-references between them. See AssemblyPrefixer.
// Usage: SilksongPrefixer <prefix> <outDir> --managed <dir> <in1.dll> <in2.dll> ...

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

using var resolver = new StrictAssemblyResolver(Directory.GetFiles(managed, "*.dll"));
var asms = inputs.Select(resolver.Read).ToList();
AssemblyPrefixer.Prefix(prefix, asms);
Directory.CreateDirectory(outDir);
// Cross-references between the renamed assemblies have to resolve while writing.
foreach (var asm in asms) resolver.Register(asm);
foreach (var asm in asms) {
    asm.Write(Path.Combine(outDir, asm.Name.Name + ".dll"));
    Console.WriteLine($"prefixed -> {asm.Name.Name}");
}
return 0;
