using FieldVisit.Infrastructure;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V180LocationPlusCodeTests
{
    [Fact]
    public void Full_code_decodes_center()
    {
        var point = V180LocationPlusCode.Decode(" 849vcwc8+r9 ");
        Assert.Equal(37.4220625m, point.Latitude);
        Assert.Equal(-122.0840625m, point.Longitude);
    }

    [Fact]
    public void Grid_refinement_decodes_first_subcell()
    {
        var point = V180LocationPlusCode.Decode("849VCWC8+R92");
        Assert.Equal(37.4220125m, point.Latitude);
        Assert.Equal(-122.08410938m, point.Longitude);
    }

    [Theory]
    [InlineData("CWC8+R9")]
    [InlineData("849VCW00+")]
    [InlineData("849VCWC8+R")]
    [InlineData("Z49VCWC8+R9")]
    [InlineData("X49VCWC8+R9")]
    [InlineData("8X9VCWC8+R9")]
    [InlineData("849VCWC8+R9 Taipei")]
    public void Short_coarse_or_invalid_code_is_denied(string code)
        => Assert.Throws<InvalidOperationException>(() => V180LocationPlusCode.Decode(code));
}
