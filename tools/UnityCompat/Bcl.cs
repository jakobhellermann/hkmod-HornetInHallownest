// .NET Standard 2.1 BCL surface missing from Unity 2020.2's mscorlib, types under their original namespaces.
// ReSharper disable CheckNamespace
using System;
using System.Collections.Generic;

namespace System {
    // .NET Standard 2.1. Nothing persists these values, so the algorithm doesn't need to match.
    public struct HashCode {
        private int current;

        private static int Mix(int hash, object value) {
            unchecked { return hash * 31 + (value?.GetHashCode() ?? 0); }
        }

        public static int Combine<T1>(T1 a) => Mix(17, a);
        public static int Combine<T1, T2>(T1 a, T2 b) => Mix(Mix(17, a), b);
        public static int Combine<T1, T2, T3>(T1 a, T2 b, T3 c) => Mix(Mix(Mix(17, a), b), c);
        public static int Combine<T1, T2, T3, T4>(T1 a, T2 b, T3 c, T4 d) => Mix(Mix(Mix(Mix(17, a), b), c), d);
        public static int Combine<T1, T2, T3, T4, T5>(T1 a, T2 b, T3 c, T4 d, T5 e)
            => Mix(Mix(Mix(Mix(Mix(17, a), b), c), d), e);
        public static int Combine<T1, T2, T3, T4, T5, T6>(T1 a, T2 b, T3 c, T4 d, T5 e, T6 f)
            => Mix(Mix(Mix(Mix(Mix(Mix(17, a), b), c), d), e), f);

        public void Add<T>(T value) => current = Mix(current == 0 ? 17 : current, value);
        public int ToHashCode() => current;
    }

    // .NET Core 2.0 single-precision math. Unity 2020.2 only has the double-based System.Math.
    public static class MathF {
        public const float PI = 3.14159274f;
        public const float E = 2.71828175f;
        public static float Abs(float x) => Math.Abs(x);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Tan(float x) => (float)Math.Tan(x);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Floor(float x) => (float)Math.Floor(x);
        public static float Ceiling(float x) => (float)Math.Ceiling(x);
        public static float Round(float x) => (float)Math.Round(x);
        public static float Log(float x) => (float)Math.Log(x);
        public static float Sign(float x) => Math.Sign(x);
    }

    // .NET Standard 2.1 span helpers. Only the string overload is reached (ResourceManager's dynamic-string reader).
    public static class MemoryExtensions {
        // Copies: aliasing the string's storage needs span internals Unity 2020.2 doesn't expose.
        public static ReadOnlySpan<char> AsSpan(this string value)
            => value == null ? default : new ReadOnlySpan<char>(value.ToCharArray());
    }
}

namespace System.Buffers {
    public delegate void SpanAction<T, in TArg>(Span<T> span, TArg arg);
}

namespace System.Diagnostics.CodeAnalysis {
    // Nullability annotations.
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue)]
    public sealed class AllowNullAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue)]
    public sealed class MaybeNullAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue)]
    public sealed class NotNullAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, AllowMultiple = true)]
    public sealed class NotNullIfNotNullAttribute : Attribute {
        public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;
        public string ParameterName { get; }
    }
}

namespace UnityEngine.Compat {
    using System.Buffers;

    // BCL members with no Unity 2020.2 counterpart, receiver as first parameter (see HornetCompat).
    public static class BclCompat {
        // string.Contains(char/string, StringComparison) — .NET Core 2.1.
        public static bool String_Contains(string self, char value, StringComparison comparisonType)
            => self != null && self.IndexOf(value.ToString(), comparisonType) >= 0;

        public static bool String_Contains(string self, string value, StringComparison comparisonType)
            => self != null && value != null && self.IndexOf(value, comparisonType) >= 0;

        // string.Split(char, StringSplitOptions) — .NET Standard 2.1.
        public static string[] String_Split(string self, char separator, StringSplitOptions options)
            => self?.Split(new[] { separator }, options);

        // string.TrimStart(char) — .NET Standard 2.1 (2.0 only has the params overload).
        public static string String_TrimStart(string self, char trimChar) => self?.TrimStart(trimChar);

        // string.Create<TState>(int, TState, SpanAction<char, TState>) — .NET Standard 2.1.
        public static string String_Create<TState>(int length, TState state, SpanAction<char, TState> action) {
            if (length <= 0) return string.Empty;
            var buffer = new char[length];
            action(new Span<char>(buffer), state);
            return new string(buffer);
        }

        // TimeSpan * double — .NET Core 2.0.
        public static TimeSpan TimeSpan_Multiply(TimeSpan self, double factor)
            => TimeSpan.FromTicks((long)(self.Ticks * factor));

        // ArraySegment<T>.Empty — .NET Standard 2.1.
        public static ArraySegment<T> ArraySegment_Empty<T>() => new ArraySegment<T>(new T[0]);
    }
}
