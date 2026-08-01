using HornetInHallownest.Core;

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

AssemblyPrefixer.Prefix(prefix, managed, inputs, outDir);
foreach (var input in inputs)
    Console.WriteLine($"prefixed {Path.GetFileName(input)} -> {outDir}");
return 0;
