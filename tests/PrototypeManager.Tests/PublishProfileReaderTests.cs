using System.Text;
using PrototypeManager.Infrastructure;

namespace PrototypeManager.Tests;

public sealed class PublishProfileReaderTests
{
    private const string SampleProfile = """
        <?xml version="1.0" encoding="utf-8"?>
        <publishData>
          <publishProfile publishMethod="MSDeploy" publishUrl="example.publish.azurewebsites.windows.net:443" />
        </publishData>
        """;

    [Fact]
    public void ReadXml_DecodesUtf16PublishProfile()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(SampleProfile)).ToArray();
        using var stream = new MemoryStream(bytes);
        var xml = PublishProfileReader.ReadXml(stream);

        Assert.StartsWith("<?xml", xml, StringComparison.Ordinal);
        Assert.Contains("publishMethod=\"MSDeploy\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadXml_DecodesUtf8PublishProfile()
    {
        var xml = PublishProfileReader.ReadXml(BinaryData.FromString(SampleProfile));

        Assert.Equal(SampleProfile, xml);
    }
}
