using System.ComponentModel.DataAnnotations;
using PrototypeManager.Domain;

namespace PrototypeManager.Tests;

public sealed class PrototypeAppSettingCatalogTests
{
    [Theory]
    [InlineData("demo example", "demo_example")]
    [InlineData("Demo-Example", "demo_example")]
    [InlineData("  API.BASE URL  ", "api_base_url")]
    [InlineData("Already_ok", "already_ok")]
    public void NormaliseKey_ConvertsSpacesCaseAndSeparators(string input, string expected)
    {
        Assert.Equal(expected, PrototypeAppSettingCatalog.NormaliseKey(input));
    }

    [Fact]
    public void ValidateUserKey_RejectsKeysStartingWithNumber()
    {
        var exception = Assert.Throws<ValidationException>(() =>
            PrototypeAppSettingCatalog.ValidateUserKey("1demo"));

        Assert.Contains("start with a letter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateUserKey_AcceptsNormalisedLowercaseKey()
    {
        PrototypeAppSettingCatalog.ValidateUserKey("demo_example");
    }
}
