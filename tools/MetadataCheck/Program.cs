// Validate rewritten assemblies. See MetadataCheck.csproj for why this exists.
// usage: mdcheck <assembly.dll>...   (exit 1 if any assembly has malformed metadata)
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Force-decode every blob-heap-backed structure. Mono asserts `index < meta->heap_blob.size` when a row points at
// a blob that was never written, which is exactly what a bad IL rewrite produces.
int bad = 0;
foreach (var path in args.OrderBy(x => x)) {
    var problems = new List<string>();
    try {
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        var p = new Sig();

        void Try(string what, Action a) { try { a(); } catch (Exception e) { problems.Add($"{what}: {e.GetType().Name}: {e.Message}"); } }

        foreach (var h in md.MethodDefinitions) {
            var m = md.GetMethodDefinition(h);
            var name = md.GetString(m.Name);
            Try($"method sig '{name}'", () => m.DecodeSignature(p, null));
            // Method bodies: header, IL stream and the local-variable signature each carry their own blob refs.
            Try($"method body '{name}'", () => {
                if (m.RelativeVirtualAddress == 0) return;
                var body = pe.GetMethodBody(m.RelativeVirtualAddress);
                var il = body.GetILBytes();
                if (il == null) throw new InvalidOperationException("null IL");
                if (!body.LocalSignature.IsNil)
                    md.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(p, null);
                foreach (var r in body.ExceptionRegions) { var _ = r.CatchType; }
            });
            foreach (var ph in m.GetParameters()) {
                var par = md.GetParameter(ph);
                Try($"param default '{name}.{md.GetString(par.Name)}'", () => {
                    var dv = par.GetDefaultValue();
                    if (!dv.IsNil) { var c = md.GetConstant(dv); md.GetBlobBytes(c.Value); }
                    var ma = par.GetMarshallingDescriptor();
                    if (!ma.IsNil) md.GetBlobBytes(ma);
                });
            }
        }
        foreach (var h in md.FieldDefinitions) {
            var f = md.GetFieldDefinition(h);
            Try($"field sig '{md.GetString(f.Name)}'", () => f.DecodeSignature(p, null));
        }
        foreach (var h in md.MemberReferences) {
            var mr = md.GetMemberReference(h);
            Try($"memberref '{md.GetString(mr.Name)}'", () => { if (mr.GetKind() == MemberReferenceKind.Method) mr.DecodeMethodSignature(p, null); else mr.DecodeFieldSignature(p, null); });
        }
        foreach (var h in md.CustomAttributes) Try("customattr", () => md.GetBlobBytes(md.GetCustomAttribute(h).Value));
        foreach (var h in md.PropertyDefinitions) {
            var pd = md.GetPropertyDefinition(h);
            Try($"property sig '{md.GetString(pd.Name)}'", () => pd.DecodeSignature(p, null));
        }
        foreach (var h in md.EventDefinitions) { var ed = md.GetEventDefinition(h); Try("event", () => md.GetString(ed.Name)); }
        for (var i = 1; i <= md.GetTableRowCount(TableIndex.MethodSpec); i++) {
            var h = MetadataTokens.MethodSpecificationHandle(i);
            Try("methodspec", () => md.GetMethodSpecification(h).DecodeSignature(p, null));
        }
        // Type-forward chains: every nested row must climb to a top-level row backed by an AssemblyRef, and only
        // that top-level row may carry the Forwarder flag.
        foreach (var h in md.ExportedTypes) {
            var et = md.GetExportedType(h);
            Try($"exportedtype '{md.GetString(et.Name)}'", () => {
                var cur = et;
                var depth = 0;
                while (cur.Implementation.Kind == HandleKind.ExportedType) {
                    if ((cur.Attributes & (System.Reflection.TypeAttributes)0x00200000) != 0)
                        throw new InvalidOperationException("nested row carries the Forwarder flag");
                    if (!cur.Namespace.IsNil) throw new InvalidOperationException("nested row has a namespace");
                    cur = md.GetExportedType((ExportedTypeHandle)cur.Implementation);
                    if (++depth > 16) throw new InvalidOperationException("forward chain too deep / cyclic");
                }
                if (cur.Implementation.Kind != HandleKind.AssemblyReference)
                    throw new InvalidOperationException($"chain ends at {cur.Implementation.Kind}, not AssemblyReference");
                if ((cur.Attributes & (System.Reflection.TypeAttributes)0x00200000) == 0)
                    throw new InvalidOperationException("top-level row missing the Forwarder flag");
            });
        }
        for (var i = 1; i <= md.GetTableRowCount(TableIndex.TypeSpec); i++) {
            var h = MetadataTokens.TypeSpecificationHandle(i);
            Try("typespec", () => md.GetTypeSpecification(h).DecodeSignature(p, null));
        }
        for (var i = 1; i <= md.GetTableRowCount(TableIndex.StandAloneSig); i++) {
            var h = MetadataTokens.StandaloneSignatureHandle(i);
            Try("standalonesig", () => md.GetBlobBytes(md.GetStandaloneSignature(h).Signature));
        }
        foreach (var h in md.TypeDefinitions) {
            var t = md.GetTypeDefinition(h);
            foreach (var ih in t.GetInterfaceImplementations()) Try("ifaceimpl", () => md.GetInterfaceImplementation(ih));
            foreach (var mi in t.GetMethodImplementations()) Try("methodimpl", () => md.GetMethodImplementation(mi));
        }
    } catch (Exception e) { problems.Add($"OPEN: {e.GetType().Name}: {e.Message}"); }

    if (problems.Count > 0) {
        bad++;
        Console.WriteLine($"### {Path.GetFileName(path)}: {problems.Count} problem(s)");
        foreach (var s in problems.Distinct().Take(6)) Console.WriteLine($"    {s}");
    }
}
Console.WriteLine(bad == 0 ? "all assemblies decode cleanly" : $"{bad} assembly(ies) with malformed metadata");
return bad == 0 ? 0 : 1;

sealed class Sig : ISignatureTypeProvider<string, object>, ICustomAttributeTypeProvider<string> {
    public string GetArrayType(string e, ArrayShape s) => e + "[]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> si) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<>";
    public string GetGenericMethodParameter(object gc, int i) => "!!" + i;
    public string GetGenericTypeParameter(object gc, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool req) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte raw) => "def";
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte raw) => "ref";
    public string GetTypeFromSpecification(MetadataReader r, object gc, TypeSpecificationHandle h, byte raw) => "spec";
    public string GetSystemType() => "Type";
    public string GetTypeFromSerializedName(string n) => n;
    public PrimitiveTypeCode GetUnderlyingEnumType(string t) => PrimitiveTypeCode.Int32;
    public bool IsSystemType(string t) => t == "Type";
}

