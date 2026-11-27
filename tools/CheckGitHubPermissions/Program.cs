using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const long appId = 5934990;
const long installationId = 186088276;

var appsettingsPath = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PrototypeManager.Web", "appsettings.json"));

if (!File.Exists(appsettingsPath))
{
    Console.Error.WriteLine($"appsettings not found: {appsettingsPath}");
    return 1;
}

using var appsettings = JsonDocument.Parse(await File.ReadAllTextAsync(appsettingsPath));
var github = appsettings.RootElement.GetProperty("GitHub");
var privateKey = github.GetProperty("PrivateKey").GetString() ?? string.Empty;
privateKey = privateKey.Trim().Replace("\\n", "\n", StringComparison.Ordinal);

var jwt = CreateAppJwt(appId, privateKey);
using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("PrototypeManager-PermissionsCheck/1.0");
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

using (var installRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/app/installations/{installationId}"))
{
    installRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
    using var installResponse = await http.SendAsync(installRequest);
    var installBody = await installResponse.Content.ReadAsStringAsync();
    if (!installResponse.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Installation lookup failed {(int)installResponse.StatusCode}: {installBody}");
        return 1;
    }

    using var installDoc = JsonDocument.Parse(installBody);
    var root = installDoc.RootElement;
    Console.WriteLine($"Account: {root.GetProperty("account").GetProperty("login").GetString()}");
    Console.WriteLine($"Repository selection: {root.GetProperty("repository_selection").GetString()}");
    Console.WriteLine("Installation permissions:");
    PrintPermissions(root.GetProperty("permissions"));
}

using (var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/app/installations/{installationId}/access_tokens"))
{
    tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
    using var tokenResponse = await http.SendAsync(tokenRequest);
    var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
    if (!tokenResponse.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Token mint failed {(int)tokenResponse.StatusCode}: {tokenBody}");
        return 1;
    }

    using var tokenDoc = JsonDocument.Parse(tokenBody);
    Console.WriteLine();
    Console.WriteLine($"Token expires: {tokenDoc.RootElement.GetProperty("expires_at").GetString()}");
    Console.WriteLine("Token permissions:");
    PrintPermissions(tokenDoc.RootElement.GetProperty("permissions"));
}

return 0;

static void PrintPermissions(JsonElement permissions)
{
    foreach (var property in permissions.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {property.Name}: {property.Value.GetString()}");
    }
}

static string CreateAppJwt(long appId, string privateKeyPem)
{
    var now = DateTimeOffset.UtcNow;
    var headerJson = """{"alg":"RS256","typ":"JWT"}""";
    var payloadJson = $"{{\"iat\":{now.AddSeconds(-60).ToUnixTimeSeconds()},\"exp\":{now.AddMinutes(9).ToUnixTimeSeconds()},\"iss\":{appId}}}";
    var header = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
    var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
    var unsigned = $"{header}.{payload}";

    using var rsa = RSA.Create();
    rsa.ImportFromPem(privateKeyPem);
    var signature = rsa.SignData(
        Encoding.UTF8.GetBytes(unsigned),
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);
    return $"{unsigned}.{Base64UrlEncode(signature)}";
}

static string Base64UrlEncode(byte[] data) =>
    Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
