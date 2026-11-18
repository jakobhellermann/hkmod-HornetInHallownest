#if HK1578
using UnityEngine;

namespace HornetInHallownest.Modules.Hkmp;

internal readonly record struct EffectState(
    bool Active,
    bool Detached,
    short X,
    short Y,
    short Z,
    ushort Rotation,
    short ScaleX,
    short ScaleY) {
    private const int PayloadBits = 1 + 1 + 3 * 16 + RotationBits + 2 * 16;
    private const int RotationBits = 10;
    private const float PositionStep = 1f / 64;
    private const float ScaleStep = 1f / 256;

    internal static EffectState Capture(Transform effect, Transform hero) {
        if (!effect.gameObject.activeSelf) return default;

        var detached = !effect.IsChildOf(hero);
        Vector3 position;
        float rotation;
        Vector2 scale;
        if (detached) {
            position = effect.position - hero.position;
            rotation = effect.eulerAngles.z;
            scale = effect.lossyScale;
        } else {
            position = hero.InverseTransformPoint(effect.position);
            rotation = (Quaternion.Inverse(hero.rotation) * effect.rotation).eulerAngles.z;
            var heroScale = hero.lossyScale;
            scale = new Vector2(effect.lossyScale.x / heroScale.x, effect.lossyScale.y / heroScale.y);
        }

        return new EffectState(true, detached,
            Quantize(position.x, PositionStep), Quantize(position.y, PositionStep), Quantize(position.z, PositionStep),
            (ushort)(Mathf.RoundToInt(rotation / 360f * (1 << RotationBits)) & ((1 << RotationBits) - 1)),
            Quantize(scale.x, ScaleStep), Quantize(scale.y, ScaleStep));
    }

    internal bool[] Encode() => new BitWriter(PayloadBits)
        .Write(Active).Write(Detached)
        .Write((ushort)X, 16).Write((ushort)Y, 16).Write((ushort)Z, 16)
        .Write(Rotation, RotationBits)
        .Write((ushort)ScaleX, 16).Write((ushort)ScaleY, 16)
        .ToArray();

    internal static EffectState Decode(bool[] payload) {
        var r = new BitReader(payload);
        return new EffectState(r.ReadBool(), r.ReadBool(),
            (short)r.Read(16), (short)r.Read(16), (short)r.Read(16),
            (ushort)r.Read(RotationBits),
            (short)r.Read(16), (short)r.Read(16));
    }

    internal void Apply(Transform clone, Transform playerObject) {
        if (!Active) {
            clone.gameObject.SetActive(false);
            return;
        }

        var offset = new Vector3(X * PositionStep, Y * PositionStep, Z * PositionStep);
        var rotation = Quaternion.Euler(0f, 0f, Rotation * 360f / (1 << RotationBits));
        var scale = new Vector2(ScaleX * ScaleStep, ScaleY * ScaleStep);
        if (Detached) {
            clone.SetPositionAndRotation(playerObject.position + offset, rotation);
        } else {
            clone.SetPositionAndRotation(playerObject.TransformPoint(offset), playerObject.rotation * rotation);
            var heroScale = playerObject.lossyScale;
            scale = new Vector2(scale.x * heroScale.x, scale.y * heroScale.y);
        }

        var parentScale = clone.parent.lossyScale;
        clone.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, clone.localScale.z);
        clone.gameObject.SetActive(true);
    }

    private static short Quantize(float value, float step) =>
        (short)Mathf.Clamp(Mathf.RoundToInt(value / step), short.MinValue, short.MaxValue);
}
#endif
