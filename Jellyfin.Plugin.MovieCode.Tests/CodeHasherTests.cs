using Jellyfin.Plugin.MovieCode.Services;

namespace Jellyfin.Plugin.MovieCode.Tests;

public sealed class CodeHasherTests
{
    [Fact]
    public void GenerateCode_ReturnsSixDigits()
    {
        var code = CodeHasher.GenerateCode();

        Assert.Equal(6, code.Length);
        Assert.All(code, character => Assert.True(char.IsAsciiDigit(character)));
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("123 456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData(" 004271 ", "004271")]
    public void Normalize_AcceptsCommonFormats(string input, string expected)
    {
        Assert.Equal(expected, CodeHasher.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData(null)]
    public void Normalize_RejectsInvalidInput(string? input)
    {
        Assert.Null(CodeHasher.Normalize(input));
    }

    [Fact]
    public void Verify_ReturnsTrueForMatchingCode()
    {
        var code = CodeHasher.GenerateCode();
        var (hash, salt) = CodeHasher.Hash(code);

        Assert.True(CodeHasher.Verify(code, hash, salt));
    }

    [Fact]
    public void Verify_ReturnsFalseForWrongCode()
    {
        var (hash, salt) = CodeHasher.Hash("123456");

        Assert.False(CodeHasher.Verify("654321", hash, salt));
    }

    [Fact]
    public void Verify_ReturnsFalseForBrokenHash()
    {
        Assert.False(CodeHasher.Verify("123456", "not-base64", "also-not-base64"));
    }
}
