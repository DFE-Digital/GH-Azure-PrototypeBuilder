using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class GitHubTokenNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("github_pat_abc\n", "github_pat_abc")]
    [InlineData(" github_pat_abc ", "github_pat_abc")]
    public void Normalize_RemovesWhitespace(string? input, string expected) =>
        Assert.Equal(expected, GitHubTokenNormalizer.Normalize(input));
}
