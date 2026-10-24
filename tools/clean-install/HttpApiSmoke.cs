using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;

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
        var runtimeConnection = await CreateRuntimeConnectionAsync(sameCleanDbConnection);
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
        start.Environment["ConnectionStrings__DefaultConnection"] = runtimeConnection;
        start.Environment["Auth__Mode"] = "Demo";
        // Offline Google failure evidence: preserve the production provider's
        // canonical GoogleMapsRoutes identity, but force HTTP to the unavailable
        // local port 1. No external Google API, key, or network dependency.
        // This is synthetic failure evidence for NONDEPLOY SQL2022 CI only.
        start.Environment["Providers__Route"] = "Google";
        start.Environment["GoogleMaps__ApiKey"] = "DISPOSABLE-SYNTHETIC-ROUTES-ONLY";
        start.Environment["GoogleMaps__RoutesBaseUrl"] = "http://127.0.0.1:1";
        start.Environment["GoogleMaps__TimeoutSeconds"] = "2";
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

            // Actual test-account login issues the signed RC token from SQL-backed
            // employment, identity and effective-dated roles. Then real HTTP
            // checks /me and role-specific denial on the same fresh DB.
            // Actual role-issued JWT on this exact fresh SQL database, first
            // prove time-scoped Trip Context and a real Visitor Draft path.
            // Partial 017 schema must fail closed for published Location use;
            // never invent old migration markers to make it pass.
            await CleanInstallBusinessProbe.VerifyAsync(
                client, sameCleanDbConnection, business, demoPassword);
            await CleanInstallOptionCCreateApprovalProbe.VerifyAsync(
                client, sameCleanDbConnection, business, demoPassword);
            Console.WriteLine("CLEAN_INSTALL=RC_API_FRESH_SQL_BUSINESS_PROBE_COMPLETED");
            await CleanInstallRoleProbe.VerifyAsync(
                client, sameCleanDbConnection, roleActors, demoPassword);
            Console.WriteLine("CLEAN_INSTALL=CANDIDATE_API_NONOWNER_DML_SQL_IDENTITY_BUSINESS_FLOW_PASS");
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

    private static async Task<string> CreateRuntimeConnectionAsync(string installationConnection)
    {
        var config = new SqlConnectionStringBuilder(installationConnection);
        if (config.DataSource != "localhost,1433" ||
            config.InitialCatalog != "OptionCIsolated_FieldVisitV180CleanInstall")
            throw new InvalidOperationException("RUNTIME_PRINCIPAL_DISPOSABLE_TARGET_REQUIRED");
        // Only this isolated container receives this login. No password, SQL or
        // connection string is emitted, exported or added to appsettings.
        var password = "Aa7!" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        const string login = "fieldvisit_disposable_runtime";
        var masterConfig = new SqlConnectionStringBuilder(installationConnection) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterConfig.ConnectionString))
        {
            await master.OpenAsync();
            // Password alphabet is fixed alphanumeric plus Aa7!; never input data.
            await using var create = new SqlCommand(
                $"CREATE LOGIN [{login}] WITH PASSWORD=N'{password}', CHECK_POLICY=ON;", master)
                { CommandTimeout = 10 };
            await create.ExecuteNonQueryAsync();
        }
        await using (var install = new SqlConnection(installationConnection))
        {
            await install.OpenAsync();
            await using var grant = new SqlCommand("""
                CREATE USER [fieldvisit_disposable_runtime] FOR LOGIN [fieldvisit_disposable_runtime];
                GRANT CONNECT TO [fieldvisit_disposable_runtime];
                DECLARE @permissions nvarchar(max)=N'';
                SELECT @permissions=@permissions+
                    N'GRANT SELECT, INSERT, UPDATE, DELETE, VIEW DEFINITION ON OBJECT::'+
                    QUOTENAME(SCHEMA_NAME(schema_id))+N'.'+QUOTENAME(name)+
                    N' TO [fieldvisit_disposable_runtime];'
                FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo');
                EXEC sys.sp_executesql @permissions;
                DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SchemaVersions TO [fieldvisit_disposable_runtime];
                DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SchemaMigrationDataBaselines TO [fieldvisit_disposable_runtime];
                """, install) { CommandTimeout = 10 };
            await grant.ExecuteNonQueryAsync();
        }
        config.UserID = login;
        config.Password = password;
        await using var runtime = new SqlConnection(config.ConnectionString);
        await runtime.OpenAsync();
        await using var inspect = new SqlCommand("""
            SELECT CASE WHEN IS_SRVROLEMEMBER(N'sysadmin')=0
                AND IS_ROLEMEMBER(N'db_owner')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER')=0
                AND HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CREATE TABLE')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'UPDATE')=0
                AND HAS_PERMS_BY_NAME(N'dbo.SchemaMigrationDataBaselines',N'OBJECT',N'UPDATE')=0
                AND (SELECT COUNT(*) FROM sys.indexes WHERE
                  object_id IN (OBJECT_ID(N'dbo.LocationPublicationStages'),
                                OBJECT_ID(N'dbo.LocationPublishedVersions'))
                  AND name IN (N'UX_OptionC_Stages_OpenLocation',N'UX_OptionC_Published_Current',
                               N'UX_OptionC_Published_Request') AND filter_definition IS NOT NULL)=3
              THEN 1 ELSE 0 END;
            """, runtime) { CommandTimeout = 10 };
        if (Convert.ToInt32(await inspect.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("RUNTIME_PRINCIPAL_EFFECTIVE_PERMISSION_MISMATCH");
        try
        {
            await using var denied = new SqlCommand(
                "CREATE TABLE dbo.RuntimeDdlMustBeDenied (Id int NOT NULL);", runtime)
                { CommandTimeout = 10 };
            await denied.ExecuteNonQueryAsync();
            throw new InvalidOperationException("RUNTIME_PRINCIPAL_DDL_NOT_DENIED");
        }
        catch (SqlException error) when (error.Number is 229 or 262)
        {
            Console.WriteLine("CLEAN_INSTALL=RUNTIME_SQL_NONOWNER_NO_DDL_LEDGER_WRITE_DENY_PASS");
        }
        return config.ConnectionString;
    }
}
