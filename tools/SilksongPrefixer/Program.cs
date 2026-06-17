using Mono.Cecil;

// Prefix every type namespace in an assembly with a prefix (e.g. "Silksong"), and optionally rename the assembly
// itself (to avoid the assembly-name colliding with HK's same-named one when both are referenced).
//
// Usage: SilksongPrefixer <input.dll> <output.dll> <namespacePrefix> <newAssemblyName> [--managed <dir>]
//
// Prefixing only TYPE DEFINITIONS in the main module is enough for intra-assembly references (they point at the same
// TypeDefinition) and for external consumers (they see the new namespaces). Cross-assembly references to OTHER
// (un-prefixed) assemblies are left untouched.

if (args.Length < 4) {
    Console.Error.WriteLine("usage: SilksongPrefixer <input.dll> <output.dll> <prefix> <newAssemblyName> [--managed <dir>]");
    return 1;
}

var input = args[0];
var output = args[1];
var prefix = args[2];
var newAssemblyName = args[3];
var managed = Path.GetDirectoryName(Path.GetFullPath(input))!;
for (var i = 4; i < args.Length - 1; i++)
    if (args[i] == "--managed") managed = args[i + 1];

var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managed);

var asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });

// Rename the assembly so it doesn't clash with HK's same-named assembly when both are referenced.
asm.Name.Name = newAssemblyName;
asm.MainModule.Name = newAssemblyName + ".dll";

var renamed = 0;
foreach (var type in asm.MainModule.Types) {
    if (type.Name == "<Module>") continue;
    type.Namespace = string.IsNullOrEmpty(type.Namespace) ? prefix : prefix + "." + type.Namespace;
    renamed++;
}

asm.Write(output);
Console.WriteLine($"prefixed {renamed} top-level types -> '{prefix}.*', assembly '{newAssemblyName}', wrote {output}");
return 0;
