using PrototypeManager.Domain;

namespace PrototypeManager.Tests;

public sealed class SlugGeneratorTests
{
    [Fact]
    public void GenerateSlug_NormalisesSpacesAndSymbols()
    {
        var slug = SlugGenerator.GenerateSlug("Teacher contacts_alpha!");

        Assert.Equal("teacher-contacts-alpha", slug);
    }

    [Fact]
    public void CreateResourceName_UsesApprovedPrefixAndResourceType()
    {
        Assert.Equal("s186d01-pt-rg-teacher-contacts", PrototypeResourceNaming.CreateResourceGroupName("teacher-contacts"));
        Assert.Equal("s186d01-pt-asp-teacher-contacts", PrototypeResourceNaming.CreateAppServicePlanName("teacher-contacts"));
        Assert.Equal("s186d01-pt-app-teacher-contacts", PrototypeResourceNaming.CreateWebAppName("teacher-contacts"));
    }

    [Fact]
    public void CreateResourceName_TrimsToRequestedLength()
    {
        var name = PrototypeResourceNaming.CreateWebAppName("very-long-prototype-name-that-exceeds-limits");

        Assert.True(name.Length <= 60);
        Assert.StartsWith("s186d01-pt-app-", name);
    }

    [Fact]
    public void CreateAzureSafeName_TrimsToRequestedLength()
    {
        var name = SlugGenerator.CreateAzureSafeName(20, "rg", "proto", "very-long-team-name", "very-long-prototype-name");

        Assert.True(name.Length <= 20);
        Assert.DoesNotContain('_', name);
    }

    [Fact]
    public void ValidateAppName_AcceptsValidName()
    {
        SlugGenerator.ValidateAppName("teacher-contacts");
    }

    [Fact]
    public void ValidateAppName_RejectsUppercase()
    {
        Assert.Throws<ArgumentException>(() => SlugGenerator.ValidateAppName("Teacher-Contacts"));
    }
}
