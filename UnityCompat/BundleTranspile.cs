// ReSharper disable CheckNamespace
using System;
using System.IO;
using System.Text;
using CsBindgen;

namespace UnityEngine.Compat;

internal static class BundleTranspile {
    // Retargets the bundle at `path` to `toVersion` in native code, the result is owned by the returned stream.
    internal static unsafe Stream Transpile(string path, string fromVersion, string toVersion) {
        var pathBytes = Encoding.UTF8.GetBytes(path);
        var fromBytes = Encoding.UTF8.GetBytes(fromVersion);
        var toBytes = Encoding.UTF8.GetBytes(toVersion);

        byte* ptr;
        nuint len;
        fixed (byte* p = pathBytes, from = fromBytes, to = toBytes) {
            if (NativeMethods.transpile(
                    p, (nuint)pathBytes.Length,
                    from, (nuint)fromBytes.Length,
                    to, (nuint)toBytes.Length,
                    &ptr, &len) != 0)
                throw new Exception($"transpile failed: {path} ({fromVersion} -> {toVersion})");
        }
        return new TranspiledBundleStream(ptr, len);
    }

    private sealed unsafe class TranspiledBundleStream : UnmanagedMemoryStream {
        private byte* ptr;
        private readonly nuint len;

        public TranspiledBundleStream(byte* ptr, nuint len) : base(ptr, (long)len) {
            this.ptr = ptr;
            this.len = len;
        }

        protected override void Dispose(bool disposing) {
            if (ptr != null) {
                NativeMethods.transpile_free(ptr, len);
                ptr = null;
            }
            base.Dispose(disposing);
        }
    }
}
