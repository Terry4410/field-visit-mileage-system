using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

/// <summary>
/// NONDEPLOY: boot the immutable accepted RC API on loopback against the very
/// same disposable SQL2022 database created by ModelBaselineRunner.
/// No mock DB, Azure connection, user seed, tokens or deployment.
/// </summary>
internal static class CleanInstallHttpProbe
{
    private const string ApiHost = "http://127.0.0.1:18376";

    public static async Task RunAsync(string rcRoot, string sameCleanDbConnection)
    {
        var dll = Path.Combine(rcRoot, "backend", "src", "FieldVisit.Api",
            "bin", "Release", "net8.0", "FieldVisit.Api.dll");
        if (!File.Exists(dll))
            throw new InvalidOperationException("RC_API_COMPILED_BINARY_MISSING");

        var hostDir = Path.Combine(rcRoot, "backend", "src", "FieldVisit.Api");
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = hostDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(dll);
        start.Environment["ASPNETCORE_URLS"] = ApiHost;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__DefaultConnection"] = sameCleanDbConnection;
        start.Environment["Auth__Mode"] = "Demo";
        start.Environment["Auth__JwtKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        start.Environment["BackgroundJobs__WorkerEnabled"] = "false";
        start.Environment["BackgroundJobs__RecoverOnStartup"] = "false";
        start.Environment["OptionC__Candidate__Enabled"] = "false";
        start.Environment["Logging__LogLevel__Default"] = "Critical";

        using var app = new Process { StartInfo = start };
        app.OutputDataReceived += (_, _) => { };
        app.ErrorDataReceived += (_, _) => { };
        var started = false;
        try
        {
            if (!app.Start()) throw new InvalidOperationException("RC_API_PROCESS_START_DENIED");
            started = true;
            app.BeginOutputReadLine();
            app.BeginErrorReadLine();
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(ApiHost),
                Timeout = TimeSpan.FromSeconds(2)
            };
            bool ready = false;
            for (int i = 0; i < 48; i++)
            {
                if (app.HasExited)
                    throw new InvalidOperationException("RC_API_PROCESS_EXITED_BEFORE_READY");
                try
                {
                    using var response = await client.GetAsync("/health/ready");
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using var document = JsonDocument.Parse(
                            await response.Content.ReadAsStringAsync());
                        if (document.RootElement.GetProperty("status").GetString() != "ready")
                            throw new InvalidOperationException("RC_API_READY_CONTRACT_INVALID");
                        ready = true;
                        break;
                    }
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(250);
            }
            if (!ready) throw new InvalidOperationException("RC_API_FRESH_SQL_READY_UNAVAILABLE");

            using var live = await client.GetAsync("/health/live");
            if (live.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("RC_API_LIVENESS_FAILED");

            using var unauthVisitor = await client.GetAsync(
                "/api/v1/trips/context?visitDate=2026-10-23");
            if (unauthVisitor.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("RC_API_VISITOR_ANONYMOUS_NOT_DENIED");

            using var request = new HttpRequestMessage(
                HttpMethod.Get, "/api/v1/leader/review-queue");
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", "invalid-disposable-token");
            using var invalidLeader = await client.SendAsync(request);
            if (invalidLeader.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("RC_API_LEADER_INVALID_JWT_NOT_DENIED");
        }
        finally
        {
            if (started)
            {
                if (!app.HasExited) app.Kill(entireProcessTree: true);
                app.WaitForExit(5_000);
            }
        }
    }
}
