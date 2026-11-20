// Loads Unity 6 asset bundles on Unity 2020.2, which reads UnityFS up to format version 7 and refuses bundles
// declaring a different engine version. Silksong's bundles are v8 with uncompressed data blocks, so the header is
// rewritten and the blocks info and data are served from the file, with the bytes patched that Unity 2020 would misread.
// ReSharper disable CheckNamespace
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using AssetsTools.NET;
using UnitySceneRepacker;

namespace UnityEngine.Compat;

internal static class UnityFsBundles {
    // Streams have to stay open until their bundle is unloaded.
    private static readonly Dictionary<AssetBundleCreateRequest, Stream> streams = new Dictionary<AssetBundleCreateRequest, Stream>();

    private static GameRepacker silksong;

    private static int translatedBundles;
    private static readonly Stopwatch patchTime = new Stopwatch();

    internal static AssetBundleCreateRequest LoadFromFileAsync(string path, uint crc, ulong offset) {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new AssetBundleHeader();
        header.Read(new AssetsFileReader(file) { Position = (long)offset });
        if (header.Version < 8) {
            file.Dispose();
            return AssetBundle.LoadFromFileAsync(path, crc, offset);
        }
        if (offset != 0) throw new NotSupportedException($"UnityFS v{header.Version} bundle at offset {offset} in {path}");

        translatedBundles++;
        var started = patchTime.Elapsed;
        patchTime.Start();
        silksong ??= new GameRepacker(GameDir(path));
        var patches = silksong.CompatPatches(path, Application.unityVersion);
        patchTime.Stop();
        if (patches.Length > 0)
            Debug.Log($"[UnityCompat] {Path.GetFileName(path)}: {patches.Length} compat patches in " +
                $"{(patchTime.Elapsed - started).TotalMilliseconds:F0} ms (so far {translatedBundles} bundles, " +
                $"{patchTime.Elapsed.TotalMilliseconds:F0} ms computing patches)");

        var stream = new UnityFsDowngradeStream(file, header, Application.unityVersion, patches);
        var request = AssetBundle.LoadFromStreamAsync(stream, crc);
        streams[request] = stream;
        return request;
    }

    // The game a bundle of its StreamingAssets belongs to.
    private static string GameDir(string bundlePath) {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(bundlePath)); dir != null; dir = dir.Parent)
            if (dir.Name.EndsWith("_Data", StringComparison.Ordinal)) return dir.Parent.FullName;
        throw new InvalidOperationException($"{bundlePath} isn't inside a game's data folder");
    }

    // The bundle's stream, to be disposed once the bundle is unloaded. Null for bundles loaded from a file.
    internal static Stream TakeStream(AssetBundle bundle) {
        foreach (var entry in streams)
            if (entry.Key.isDone && entry.Key.assetBundle == bundle) {
                streams.Remove(entry.Key);
                return entry.Value;
            }
        return null;
    }
}

// Presents a UnityFS v8 bundle as v7: the rewritten header, then the original blocks info and data.
internal sealed class UnityFsDowngradeStream : Stream {
    private readonly Stream file;
    private readonly byte[] header;
    private readonly long blocksInfoFileStart;
    private readonly long blocksInfoLength;
    private readonly long dataFileStart;
    private readonly long length;
    private long position;

    private readonly BytePatch[] patches;

    public UnityFsDowngradeStream(Stream file, AssetBundleHeader source, string declaredRevision, BytePatch[] patches) {
        this.file = file;
        this.patches = patches;
        var fs = source.FileStreamHeader;
        if ((fs.Flags & (AssetBundleFSHeaderFlags.BlockAndDirAtEnd | AssetBundleFSHeaderFlags.OldWebPluginCompatibility)) != 0)
            throw new NotSupportedException($"UnityFS v{source.Version} flags {fs.Flags}");
        blocksInfoFileStart = source.GetBundleInfoOffset();
        blocksInfoLength = fs.CompressedSize;
        dataFileStart = source.GetFileDataOffset();

        // Unity 2020.2 doesn't know the blocks info padding flag; the data follows the blocks info directly.
        var v7 = new AssetBundleHeader {
            Signature = source.Signature,
            Version = 7,
            GenerationVersion = source.GenerationVersion,
            EngineVersion = declaredRevision,
            FileStreamHeader = new AssetBundleFSHeader {
                CompressedSize = fs.CompressedSize,
                DecompressedSize = fs.DecompressedSize,
                Flags = fs.Flags & ~AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart,
            },
        };
        var headerLength = v7.GetBundleInfoOffset();
        length = headerLength + blocksInfoLength + (file.Length - dataFileStart);
        v7.FileStreamHeader.TotalFileSize = length;

        var bytes = new MemoryStream();
        var writer = new AssetsFileWriter(bytes);
        v7.Write(writer);
        writer.Flush();
        if (bytes.Length > headerLength) throw new InvalidOperationException("v7 header overruns the blocks info offset");
        bytes.SetLength(headerLength);
        header = bytes.ToArray();
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position {
        get => position;
        set => position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) {
        var total = 0;
        while (count > 0 && position < length) {
            int read;
            if (position < header.Length) {
                read = (int)Math.Min(count, header.Length - position);
                Buffer.BlockCopy(header, (int)position, buffer, offset, read);
            } else {
                var afterHeader = position - header.Length;
                var filePosition = afterHeader < blocksInfoLength
                    ? blocksInfoFileStart + afterHeader
                    : dataFileStart + (afterHeader - blocksInfoLength);
                var segmentEnd = afterHeader < blocksInfoLength ? header.Length + blocksInfoLength : length;
                file.Position = filePosition;
                read = file.Read(buffer, offset, (int)Math.Min(count, segmentEnd - position));
                if (read == 0) throw new EndOfStreamException();
                if (filePosition >= dataFileStart) ApplyPatches(filePosition - dataFileStart, buffer, offset, read);
            }
            position += read;
            offset += read;
            count -= read;
            total += read;
        }
        return total;
    }

    private void ApplyPatches(long dataPosition, byte[] buffer, int offset, int count) {
        var end = dataPosition + count;
        foreach (var patch in patches) {
            if (patch.Offset >= end) break;
            var patchEnd = patch.Offset + patch.Bytes.Length;
            if (patchEnd <= dataPosition) continue;
            var from = Math.Max(patch.Offset, dataPosition);
            var to = Math.Min(patchEnd, end);
            Buffer.BlockCopy(patch.Bytes, (int)(from - patch.Offset), buffer, offset + (int)(from - dataPosition), (int)(to - from));
        }
    }

    public override long Seek(long offset, SeekOrigin origin) {
        position = origin switch {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing) {
        if (disposing) file.Dispose();
        base.Dispose(disposing);
    }
}
