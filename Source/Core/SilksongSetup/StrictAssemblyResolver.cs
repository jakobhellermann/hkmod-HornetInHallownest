using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;

namespace HornetInHallownest.Core.SilksongSetup;

// Resolves registered in-memory assemblies, then the given files by simple name. Never falls back to the GAC or the
// running runtime, so resolution matches what the game will load.
internal sealed class StrictAssemblyResolver : IAssemblyResolver {
    private readonly Dictionary<string, string> files = new();
    private readonly Dictionary<string, AssemblyDefinition> byName = new();
    private readonly List<AssemblyDefinition> owned = [];

    public StrictAssemblyResolver(IEnumerable<string> files) {
        foreach (var file in files) this.files[Path.GetFileNameWithoutExtension(file)] = file;
    }

    // Read fully into memory, so the file can be overwritten while the definition is alive.
    public AssemblyDefinition Read(string path) {
        var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = this, InMemory = true });
        owned.Add(asm);
        return asm;
    }

    // Makes an in-memory assembly resolvable under its current name.
    public void Register(AssemblyDefinition asm) => byName[asm.Name.Name] = asm;

    public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters());

    public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) {
        if (byName.TryGetValue(name.Name, out var asm)) return asm;
        if (!files.TryGetValue(name.Name, out var path)) throw new AssemblyResolutionException(name);
        asm = Read(path);
        byName[name.Name] = asm;
        return asm;
    }

    public void Dispose() {
        foreach (var asm in owned) asm.Dispose();
        owned.Clear();
        byName.Clear();
    }
}
