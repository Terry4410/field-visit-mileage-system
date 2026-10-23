using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

/// <summary>
/// NONDEPLOY: boot the separately identified RC-derived runtime candidate API
/// on loopback against the same disposable SQL2022 database.
/// No mock DB, Azure connection, user seed, tokens or deployment.
/// </summary>
internal static class CleanInstallHttpProbe
{
    private const string ApiHost = "http://127.0.0.1:18376";

    public static async Task RunAsync(string candidateApiRoot, string sameCleanDbConnection)
    {
        var hostDir = Path.GetFullPath(candidateApiRoot);
        var dll = Path.Combine(hostDir, "FieldVisit.Api.dll");
        if (!File.Exists(dll))
            throw new InvalidOperationException("CANDIDATE_API_PACKAGE_MISSING");

        // These accounts live only in the very same newly installed disposable DB.
        // Never substitute an EnsureCreated fixture or a shared/UAT database.
        var roleActors = await CleanInstallRoleProbe.SeedAsync(sameCleanDbConnection);
        var business = await CleanInstallBusinessProbe.SeedAsync(sameCleanDbConnection, roleActors);
        var demoPassword = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
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
        start.Environment["Auth__DemoPassword"] = demoPassword;
        start.Environment["Auth__JwtKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        start.Environment["BackgroundJobs__WorkerEnabled"] = "false";
        start.Environment["BackgroundJobs__RecoverOnStartup"] = "false";
        // This test process alone enables the RC-derived candidate. The SQL
        // policy additionally requires a loopback SqlClient connection to the
        // fixed OptionCIsolated_* database; packaged defaults stay OFF.
        start.Environment["OptionC__Candidate__Enabled"] = "true";
        start.Environment["OPTIONC_EXECUTION_MODE"] = "IsolatedSqlTest";
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
