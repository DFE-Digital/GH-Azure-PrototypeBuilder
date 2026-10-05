using System.Text;
using System.Text.Json;

namespace PrototypeManager.Infrastructure;

internal static class PublishProfileReader
{
    public static string ReadXml(Stream stream)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        return ParseContent(reader.ReadToEnd());
    }

    public static string ReadXml(BinaryData data)
    {
        using var stream = data.ToStream();
        return ReadXml(stream);
    }

    private static string ParseContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Azure returned an empty publish profile.");
        }

        content = content.Trim('\uFEFF', '\u200B', ' ', '\t', '\r', '\n');

        if (content.StartsWith('"') && content.EndsWith('"'))
        {
            content = JsonSerializer.Deserialize<string>(content) ?? content;
            content = content.Trim('\uFEFF', '\u200B', ' ', '\t', '\r', '\n');
        }

        if (!content.StartsWith("<", StringComparison.Ordinal))
        {
            var preview = content.Length <= 80 ? content : content[..80];
            throw new InvalidOperationException(
                $"Azure returned unexpected publish profile content (starts with: {preview}).");
        }

        return content;
    }
}
