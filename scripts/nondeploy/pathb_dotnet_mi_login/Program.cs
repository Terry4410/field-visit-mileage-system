// NONDEPLOY candidate. NEVER run in Azure without separate exact-artifact Owner authorization.
// Exactly one App Service managed-identity token request and one SQL login; NO SQL commands.
using System.Net;
using System.Text.Json;
using Microsoft.Data.SqlClient;

internal static class Program
{
    private const string Host = "sql-fieldvisit-jpe-pathb-uat.database.windows.net";
    private const string Database = "db-fieldvisit-pathb-uat";
    private const string PrivateIp = "10.90.2.4";
    private const string Audience = "https://database.windows.net/";

    private static int Hold(string category)
    {
        Console.WriteLine("PATHB_DOTNET_MI_LOGIN=" + category);
        return 2;
    }

    private static async Task<int> Main()
    {
        // Pre-token checks; never log identity, endpoint, token, connection string or exceptions.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")))
            return Hold("HOLD_NOT_APP_CONTAINER");
        var endpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
        var header = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(header))
            return Hold("HOLD_MI_ENDPOINT_ABSENT");
        // Ordered, non-sensitive classifications. Never output the endpoint or its parts.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return Hold("HOLD_MI_ENDPOINT_PARSE");
        if (uri.Scheme != Uri.UriSchemeHttp || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return Hold("HOLD_MI_ENDPOINT_SCHEME_OR_AUTHORITY");
        // App Service supplies a platform-local token host; never accept arbitrary DNS names.
        // Only literal IPv4 loopback (127/8) or IPv4 link-local (169.254/16) is eligible.
        // No DNS resolution for the identity endpoint; no public, private-VNet or IPv6 target.
        if (!IPAddress.TryParse(uri.Host, out var identityIp) ||
            identityIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return Hold("HOLD_MI_ENDPOINT_HOST");
        var identityOctets = identityIp.GetAddressBytes();
        bool localHost = identityOctets[0] == 127 ||
                         (identityOctets[0] == 169 && identityOctets[1] == 254);
        if (!localHost)
            return Hold("HOLD_MI_ENDPOINT_HOST");
        if (uri.Port <= 0 || uri.IsDefaultPort)
            return Hold("HOLD_MI_ENDPOINT_PORT");
        // App Service emits the same fixed route with platform-dependent case and optional final slash.
        // Permit only that route; never accept a broader prefix or arbitrary token endpoint.
        var allowedPath = string.Equals(uri.AbsolutePath, "/MSI/token", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(uri.AbsolutePath, "/MSI/token/", StringComparison.OrdinalIgnoreCase);
        if (!allowedPath)
            return Hold("HOLD_MI_ENDPOINT_PATH");
        if (!string.IsNullOrEmpty(uri.Query))
            return Hold("HOLD_MI_ENDPOINT_QUERY");
        IPAddress[] ips;
        try { ips = await Dns.GetHostAddressesAsync(Host).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch { return Hold("HOLD_PRIVATE_DNS_UNVERIFIED"); }
        if (ips.Length == 0 || ips.Any(ip => ip.ToString() != PrivateIp))
            return Hold("HOLD_PRIVATE_DNS_MISMATCH");

        // Construct a single fixed token request; reject proxies and redirects.
        var builder = new UriBuilder(uri);
        builder.Query = "resource=" + Uri.EscapeDataString(Audience) + "&api-version=2019-08-01";
        string token;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
            request.Headers.TryAddWithoutValidation("X-IDENTITY-HEADER", header);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > 16384)
                return Hold("HOLD_MI_TOKEN_RESPONSE");
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var memory = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (memory.Length + read > 16384) return Hold("HOLD_MI_TOKEN_RESPONSE");
                memory.Write(buffer, 0, read);
            }
            using var json = JsonDocument.Parse(memory.ToArray());
            token = json.RootElement.GetProperty("access_token").GetString() ?? "";
            if (token.Length == 0) return Hold("HOLD_MI_TOKEN_RESPONSE");
        }
        catch { return Hold("HOLD_MI_TOKEN_REQUEST_UNKNOWN"); }

        // Direct SqlClient connection; no EF, no SQL commands, no pooling, no retries.
        var cs = new SqlConnectionStringBuilder
        {
            DataSource = "tcp:" + Host + ",1433",
            InitialCatalog = Database,
            ConnectTimeout = 8,
            TrustServerCertificate = false,
            Pooling = false,
            ConnectRetryCount = 0
        };
        cs["Encrypt"] = "True";
        try
        {
            await using var connection = new SqlConnection(cs.ConnectionString) { AccessToken = token };
            await connection.OpenAsync(new CancellationTokenSource(TimeSpan.FromSeconds(8)).Token);
            await connection.CloseAsync();
        }
        catch (SqlException ex) when (ex.Number == 18456 || ex.Number == 113)
        { return Hold("SQL_AUTH_DENIED_OR_DOSGUARD_UNKNOWN"); }
        catch (SqlException)
        { return Hold("MI_LOGIN_OR_TRANSPORT_UNKNOWN"); }
        catch
        { return Hold("MI_LOGIN_UNKNOWN"); }
        Console.WriteLine("PATHB_DOTNET_MI_LOGIN=PASS_NO_SQL_EXECUTED");
        return 0;
    }
}
