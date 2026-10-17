using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace HornetInHallownest.Core.SilksongSetup;

// Builds the Silksong assemblies for a host from the Silksong install: deps as-is, conflicting assemblies prefixed,
// and with a shim (hosts older than Silksong's Unity) everything downgraded onto it.
internal static class SilksongAssemblyBuilder {
    internal static void Build(string silksongManaged, string hostManaged, IReadOnlyCollection<string> deps,
        string? shimPath, string outDir, Action<string> log) {
        // A previous install may still sit in the host's folder; it must not count as the host.
        var installed = new HashSet<string>(SilksongAssemblies.Deps.Concat(SilksongAssemblies.Hk1578Deps)
            .Concat(SilksongAssemblies.Hk1578PlatformDeps).Concat(SilksongAssemblies.Prefixed)
            .Concat(SilksongAssemblies.ShimFiles));
        var hostFiles = Directory.GetFiles(hostManaged, "*.dll")
            .Where(f => !installed.Contains(Path.GetFileNameWithoutExtension(f)))
            .ToList();

        // Shipped next to the shim, which references them.
        var shimDeps = shimPath == null
            ? []
            : SilksongAssemblies.ShimFiles.Where(n => n != SilksongAssemblies.Shim)
                .Select(n => $"{Path.GetDirectoryName(shimPath)}/{n}.dll").ToList();
        using var resolver = new StrictAssemblyResolver(hostFiles.Concat(shimDeps));
        var prefixed = SilksongAssemblies.PrefixInputs.Select(n => resolver.Read($"{silksongManaged}/{n}.dll")).ToList();
        AssemblyPrefixer.Prefix(SilksongAssemblies.Prefix, prefixed);
        Directory.CreateDirectory(outDir);

        if (shimPath == null) {
            foreach (var asm in prefixed) resolver.Register(asm);
            foreach (var asm in prefixed) asm.Write($"{outDir}/{asm.Name.Name}.dll");
            foreach (var dep in deps) File.Copy($"{silksongManaged}/{dep}.dll", $"{outDir}/{dep}.dll", overwrite: true);
            return;
        }

        var shim = resolver.Read(shimPath);
        // The shim is downgraded too: it is compiled against reference assemblies, not against the host.
        var all = prefixed.Concat(deps.Select(n => resolver.Read($"{silksongManaged}/{n}.dll"))).Append(shim).ToList();
        foreach (var asm in all) resolver.Register(asm);

        var host = hostFiles.Select(f => resolver.Resolve(new AssemblyNameReference(Path.GetFileNameWithoutExtension(f), null)));
        new UnityDowngrader(host, shim, log).Downgrade(all);

        foreach (var asm in all) asm.Write($"{outDir}/{asm.Name.Name}.dll");
        foreach (var dep in shimDeps) File.Copy(dep, $"{outDir}/{Path.GetFileName(dep)}", overwrite: true);
    }
}
