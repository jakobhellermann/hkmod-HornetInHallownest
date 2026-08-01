using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace HornetInHallownest.Core;

// Rename a set of assemblies to "<prefix>.<name>", fixing cross-references within the set.
internal static class AssemblyPrefixer {
    internal static string PrefixedName(string prefix, string original) => $"{prefix}.{original}";

    // managedDir is the resolver search path for the inputs' own dependencies. Namespace prefixing breaks PlayMaker's
    // FsmStateAction lookup, so only the assembly identity is renamed.
    internal static void Prefix(string prefix, string managedDir, IEnumerable<string> inputPaths, string outDir) {
        Directory.CreateDirectory(outDir);

        var resolver = new InMemoryResolver();
        resolver.AddSearchDirectory(managedDir);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };

        var asms = inputPaths.Select(p => AssemblyDefinition.ReadAssembly(p, readerParams)).ToList();
        try {
            var rename = asms.ToDictionary(a => a.Name.Name, a => PrefixedName(prefix, a.Name.Name));

            foreach (var asm in asms) {
                var module = asm.MainModule;
                foreach (var r in module.AssemblyReferences)
                    if (rename.TryGetValue(r.Name, out var nn)) r.Name = nn;
                var newName = rename[asm.Name.Name];
                asm.Name.Name = newName;
                module.Name = newName + ".dll";
            }

            // Register the renamed assemblies so cross-refs (e.g. Silksong.Assembly-CSharp -> Silksong.PlayMaker) resolve
            // when Cecil writes.
            foreach (var asm in asms) resolver.Add(asm);

            foreach (var asm in asms)
                asm.Write(Path.Combine(outDir, asm.Name.Name + ".dll"));
        } finally {
            foreach (var asm in asms) asm.Dispose();
        }
    }

    // Serves the in-memory (renamed) assemblies by their new simple name; falls back to disk for everything else.
    private sealed class InMemoryResolver : DefaultAssemblyResolver {
        private readonly Dictionary<string, AssemblyDefinition> registered = new();
        public void Add(AssemblyDefinition asm) => registered[asm.Name.Name] = asm;
        public override AssemblyDefinition Resolve(AssemblyNameReference name) =>
            registered.TryGetValue(name.Name, out var asm) ? asm : base.Resolve(name);
    }
}
