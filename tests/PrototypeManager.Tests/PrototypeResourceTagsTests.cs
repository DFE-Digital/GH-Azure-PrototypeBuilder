using PrototypeManager.Domain;

namespace PrototypeManager.Tests;

public sealed class PrototypeResourceTagsTests
{
    [Fact]
    public void Build_AppliesRequiredOrganisationTags()
    {
        var tags = PrototypeResourceTags.Build();

        Assert.Equal("Dev", tags["Environment"]);
        Assert.Equal("Design operations", tags["Product"]);
        Assert.Equal("Design Operations", tags["Service Offering"]);
        Assert.Equal(3, tags.Count);
    }
}
