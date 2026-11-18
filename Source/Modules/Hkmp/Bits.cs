#if HK1578
using System;

namespace HornetInHallownest.Modules.Hkmp;

// Packs unsigned fields into the bool[] payload HKMP carries per animation entry.
internal sealed class BitWriter {
    private readonly bool[] bits;
    private int position;

    internal BitWriter(int length) {
        // HKMP mis-serializes effect info whose length % 8 is 0 or 7, shifting the rest of the packet.
        // https://github.com/Extremelyd1/HKMP/blob/f7c35d96f84c3d7b671fc4283795a07b4df97227/HKMP/Networking/Packet/Data/PlayerUpdate.cs#L127
        if (length % 8 is 0 or 7) throw new ArgumentException($"HKMP can't carry a {length} bit payload", nameof(length));
        bits = new bool[length];
    }

    internal BitWriter Write(uint value, int count) {
        for (var i = 0; i < count; i++) bits[position++] = (value >> i & 1) != 0;
        return this;
    }

    internal BitWriter Write(bool value) => Write(value ? 1u : 0u, 1);

    internal bool[] ToArray() => bits;
}

internal sealed class BitReader(bool[] bits) {
    private int position;

    internal uint Read(int count) {
        var value = 0u;
        for (var i = 0; i < count; i++)
            if (bits[position++])
                value |= 1u << i;
        return value;
    }

    internal bool ReadBool() => Read(1) != 0;
}
#endif
