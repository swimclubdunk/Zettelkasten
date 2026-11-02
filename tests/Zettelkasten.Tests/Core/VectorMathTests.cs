using Zettelkasten.Core.Search;

namespace Zettelkasten.Tests.Core;

public class VectorMathTests
{
    [Fact]
    public void Normalize_then_dot_with_self_is_one()
    {
        var v = new float[] { 3, 4 };
        VectorMath.Normalize(v);
        Assert.Equal(0.6f, v[0], 5);
        Assert.Equal(1f, VectorMath.Dot(v, v), 5);
    }

    [Fact]
    public void Normalize_zero_vector_stays_zero()
    {
        var v = new float[3];
        VectorMath.Normalize(v);
        Assert.All(v, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void Bytes_round_trip()
    {
        var v = new[] { 1.5f, -2f, 0.25f };
        Assert.Equal(v, VectorMath.FromBytes(VectorMath.ToBytes(v)));
    }
}
