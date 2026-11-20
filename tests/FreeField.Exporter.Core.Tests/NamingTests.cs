namespace FreeField.Exporter.Core.Tests;

public class NamingTests
{
    [Theory]
    [InlineData("A103", "A103")]
    [InlineData("A/103 x", "A_103_x")]
    [InlineData("  ..A  B..  ", "A_B")]
    [InlineData("a--b", "a--b")]
    [InlineData("", "_")]
    [InlineData(null, "_")]
    [InlineData("///", "_")]
    [InlineData("Ünï", "n")]
    public void Sanitize(string? input, string expected) => Assert.Equal(expected, BundleNaming.SanitizeFileStem(input));

    [Fact]
    public void MakeUnique_Dedupes_CaseInsensitive()
    {
        var used = new HashSet<string>();
        Assert.Equal("A1", BundleNaming.MakeUnique("A1", used));
        Assert.Equal("a1_2", BundleNaming.MakeUnique("a1", used));
        Assert.Equal("A1_3", BundleNaming.MakeUnique("A1", used));
    }
}
