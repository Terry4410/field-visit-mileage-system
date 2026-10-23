using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;

/// <summary>
/// Exercises the RC-derived, explicitly enabled Option C candidate through
/// loopback HTTP and the same genuinely clean-installed SQL Server 2022 DB.
/// Candidate enablement is test-process-only; every Option C service also
/// requires IsolatedSqlTest plus the fixed OptionCIsolated_* loopback target.
/// </summary>
internal static class CleanInstallOptionCCreateApprovalProbe
{
    private const string TargetDatabase = "OptionCIsolated_FieldVisitV180CleanInstall";
    private static InvalidOperationException Fail(string code) => new("CLEAN_BIZ_OPTIONC_" + code);

    private static async Task<HttpClient> LoginAsync(HttpClient host, string account, string password)
    {
        using var response = await host.PostAsJsonAsync("/api/v1/auth/demo-login",
            new { account, password });
        if (response.StatusCode != HttpStatusCode.OK) throw Fail("LOGIN");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("accessToken").GetString();
        if (string.IsNullOrWhiteSpace(token)) throw Fail("TOKEN_MISSING");
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        })
        {
            BaseAddress = host.BaseAddress,
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonDocument> RequireJsonAsync(
        HttpResponseMessage response, HttpStatusCode expected, string stage)
    {
        if (response.StatusCode != expected) throw Fail(stage + "_HTTP");
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync()); }
        catch (JsonException) { throw Fail(stage + "_JSON"); }
    }

    internal static async Task VerifyAsync(
        HttpClient host, string cs, CleanInstallBusinessProbe.Fixture fixture,
        string demoPassword)
    {
        var builder = new SqlConnectionStringBuilder(cs);
        if (builder.DataSource != "localhost,1433" || builder.InitialCatalog != TargetDatabase
            || builder.UserID != "sa" || builder.IntegratedSecurity)
            throw Fail("NONDISPOSABLE_TARGET");

        using var visitor = await LoginAsync(host, fixture.Visitor.Account, demoPassword);
        using var leader = await LoginAsync(host, fixture.Leader.Account, demoPassword);
        using var admin = await LoginAsync(host, fixture.Admin.Account, demoPassword);

        async Task<Guid> SubmitCreateAsync(HttpClient requester, string suffix)
        {
            using var response = await requester.PostAsJsonAsync(
                "/api/v1/optionc-candidate/locations", new
                {
                    teamId = fixture.TeamId,
                    reason = "Disposable three-role clean-install approval test",
                    proposed = new
                    {
                        locationName = "Synthetic Option C " + suffix,
                        city = "Taipei",
                        district = "Datong",
                        address = "100 Disposable Test Road " + suffix,
                        plusCode = (string?)null,
                        taxId = (string?)null,
                        m