using System.Runtime.InteropServices;

namespace Zettelkasten.Core.Search;

public static class VectorMath
{
    public static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vector lengths differ.");
        var sum = 0f;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    public static void Normalize(Span<float> v)
    {
        var norm = MathF.Sqrt(Dot(v, v));
        if (norm == 0) return;
        for (var i = 0; i < v.Length; i++) v[i] /= norm;
    }

    public static byte[] ToBytes(float[] v) => MemoryMarshal.AsBytes(v.AsSpan()).ToArray();

    public static float[] FromBytes(byte[] b) => MemoryMarshal.Cast<byte, float>(b).ToArray();
}
