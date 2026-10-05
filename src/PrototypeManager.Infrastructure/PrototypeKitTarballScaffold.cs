using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace PrototypeManager.Infrastructure;

internal static class PrototypeKitTarballScaffold
{
    private const string NpmRegistryBaseUrl = "https://registry.npmjs.org";
    private const string PrototypeStarterPrefix = "package/prototype-starter/";
    private const string PackageJsonPath = "package/package.json";
    private const string LicencePath = "package/LICENCE.txt";

    private const string GitIgnore = """
        # Node.js ignores
        node_modules/

        # Prototype ignores - per-user
        .tmp/
        .env
        migrate.log

        # General ignores
        .DS_Store
        .idea
        """;

    private const string NpmRc = """
        audit=false
        ignore-scripts=true
        allow-git=none
        """;

    public static async Task WriteStarterFilesAsync(
        string workingDirectory,
        string versionSpec,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var resolvedVersion = await ResolvePackageVersionAsync(versionSpec, cancellationToken);
        var tarballUrl = $"{NpmRegistryBaseUrl}/govuk-prototype-kit/-/govuk-prototype-kit-{resolvedVersion}.tgz";

        logger.LogInformation(
            "Downloading GOV.UK Prototype Kit {Version} starter files from npm.",
            resolvedVersion);

        using var httpClient = new HttpClient();
        await using var tarballStream = await httpClient.GetStreamAsync(tarballUrl, cancellationToken);
        await using var gzipStream = new GZipStream(tarballStream, CompressionMode.Decompress);

        string? govukFrontendVersion = null;
        byte[]? licenceContents = null;
        var starterFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        using (var reader = new TarReader(gzipStream))
        {
            while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
            {
                if (entry.EntryType is not TarEntryType.RegularFile || entry.DataStream is null)
                {
                    continue;
                }

                var entryPath = entry.Name.Replace('\\', '/');

                if (string.Equals(entryPath, PackageJsonPath, StringComparison.Ordinal))
                {
                    using var document = await JsonDocument.ParseAsync(entry.DataStream, cancellationToken: cancellationToken);
                    if (document.RootElement.TryGetProperty("dependencies", out var dependencies) &&
                        dependencies.TryGetProperty("govuk-frontend", out var govukFrontend))
                    {
                        govukFrontendVersion = govukFrontend.GetString();
                    }

                    continue;
                }

                if (string.Equals(entryPath, LicencePath, StringComparison.Ordinal))
                {
                    await using var memoryStream = new MemoryStream();
                    await entry.DataStream.CopyToAsync(memoryStream, cancellationToken);
                    licenceContents = memoryStream.ToArray();
                    continue;
                }

                if (entryPath.StartsWith(PrototypeStarterPrefix, StringComparison.Ordinal))
                {
                    var relativePath = entryPath[PrototypeStarterPrefix.Length..];
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        continue;
                    }

                    await using var memoryStream = new MemoryStream();
                    await entry.DataStream.CopyToAsync(memoryStream, cancellationToken);
                    starterFiles[relativePath] = memoryStream.ToArray();
                }
            }
        }

        if (starterFiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"Unable to read GOV.UK Prototype Kit starter files from npm package {resolvedVersion}.");
        }

        Directory.CreateDirectory(workingDirectory);

        foreach (var (relativePath, contents) in starterFiles)
        {
            var destinationPath = Path.Combine(workingDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, contents, cancellationToken);
        }

        await File.WriteAllTextAsync(
            Path.Combine(workingDirectory, ".gitignore"),
            GitIgnore,
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(workingDirectory, ".npmrc"),
            NpmRc,
            cancellationToken);

        if (licenceContents is not null)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(workingDirectory, "LICENCE.txt"),
                licenceContents,
                cancellationToken);
        }

        var packageJson = new JsonObject
        {
            ["scripts"] = new JsonObject
            {
                ["dev"] = "govuk-prototype-kit dev",
                ["serve"] = "govuk-prototype-kit serve",
                ["start"] = "govuk-prototype-kit start"
            },
            ["dependencies"] = new JsonObject
            {
                ["govuk-prototype-kit"] = resolvedVersion,
                ["govuk-frontend"] = govukFrontendVersion ?? "5.11.0",
                ["@govuk-prototype-kit/common-templates"] = "*"
            }
        };

        var packageJsonPath = Path.Combine(workingDirectory, "package.json");
        await File.WriteAllTextAsync(
            packageJsonPath,
            packageJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
            Encoding.UTF8,
            cancellationToken);

        if (!File.Exists(packageJsonPath))
        {
            throw new InvalidOperationException("GOV.UK Prototype Kit scaffolding did not produce a package.json file.");
        }
    }

    private static async Task<string> ResolvePackageVersionAsync(
        string versionSpec,
        CancellationToken cancellationToken)
    {
        var trimmed = versionSpec.Trim();
        if (Version.TryParse(trimmed, out _))
        {
            return trimmed;
        }

        using var httpClient = new HttpClient();
        await using var stream = await httpClient.GetStreamAsync(
            $"{NpmRegistryBaseUrl}/govuk-prototype-kit",
            cancellationToken);

        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("versions", out var versions))
        {
            throw new InvalidOperationException("Unable to resolve GOV.UK Prototype Kit version from npm.");
        }

        Version? latestMatch = null;
        string? latestVersion = null;

        foreach (var versionProperty in versions.EnumerateObject())
        {
            if (!Version.TryParse(versionProperty.Name, out var parsedVersion))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(trimmed) &&
                parsedVersion.Major.ToString() != trimmed &&
                !versionProperty.Name.StartsWith($"{trimmed}.", StringComparison.Ordinal))
            {
                continue;
            }

            if (latestMatch is null || parsedVersion > latestMatch)
            {
                latestMatch = parsedVersion;
                latestVersion = versionProperty.Name;
            }
        }

        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            throw new InvalidOperationException(
                $"Unable to find GOV.UK Prototype Kit version matching '{versionSpec}' on npm.");
        }

        return latestVersion;
    }
}
