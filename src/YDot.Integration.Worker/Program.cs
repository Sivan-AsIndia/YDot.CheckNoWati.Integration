using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using YDot.Integration.Application;
using YDot.Integration.Infrastructure;
using YDot.Integration.Infrastructure.Configuration;
using YDot.Integration.Infrastructure.Persistence;
using YDot.Integration.Worker.Services;

// A Windows service starts in C:\Windows\System32; relative paths (logs/, data/) must resolve
// against the installation folder instead.
if (WindowsServiceHelpers.IsWindowsService())
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
}

// ---- 1. Secrets from .env ----------------------------------------------------------------------
// Loaded BEFORE the host reads its configuration, so ConnectionStrings__IntegrationDatabase,
// CheckNumber__ApiKey and Wati__ApiToken in the .env file become ordinary settings. A variable that
// is already set in the environment (Docker, a Windows service) is never overridden.
var envFile = DotEnvFile.Load(Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

// Started before the host so a start-up failure (bad setting, unreachable database) is still logged.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Runs as a Windows service when installed as one (sc.exe create ...); a no-op elsewhere.
    builder.Services.AddWindowsService(options => options.ServiceName = "YDot Integration Worker");

    // Long enough for a job in flight to reach a safe point and release its lease.
    builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(60));

    builder.Services.AddSerilog((services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", "YDot.Integration");

        var seqUrl = builder.Configuration["Seq:ServerUrl"];
        if (!string.IsNullOrWhiteSpace(seqUrl))
        {
            configuration.WriteTo.Seq(seqUrl);
        }
    });

    // ---- 2. The layers ---------------------------------------------------------------------------
    builder.Services.AddIntegrationApplication(builder.Configuration);
    builder.Services.AddIntegrationInfrastructure(builder.Configuration);

    // ---- 3. The background services ------------------------------------------------------------------
    builder.Services.AddHostedService<HeartbeatWorker>();        // int_worker_instances
    builder.Services.AddHostedService<WatiTemplateSyncWorker>(); // int_wati_templates
    builder.Services.AddHostedService<InboxScannerWorker>();     // inbox -> int_input_files + int_integration_jobs
    builder.Services.AddHostedService<JobProcessorWorker>();     // parse -> CheckNumber.ai -> verify -> WATI job -> output
    builder.Services.AddHostedService<WatiDispatchWorker>();     // WATI batches
    builder.Services.AddHostedService<WatiStatusSyncWorker>();   // sent / delivered / read
    builder.Services.AddHostedService<CommandWorker>();          // retry / cancel / manual broadcasts from the web app

    var host = builder.Build();

    Log.Information("YDot.Integration worker starting ({Environment}). Settings file .env: {EnvFile}",
        builder.Environment.EnvironmentName, envFile ?? "(none found - using environment variables only)");

    // A setting that no longer does anything should say so rather than be silently ignored.
    if (builder.Configuration.GetValue<bool>("Pipeline:AutoSendWati"))
    {
        Log.Warning("Pipeline:AutoSendWati is set to true, but it is retired and ignored: a check only verifies numbers, "
                    + "and WATI messages only the numbers a person selects on the WhatsApp Messages page. Remove the setting.");
    }

    // ---- 4. Schema first, then the services ----------------------------------------------------------
    await host.Services.GetRequiredService<DatabaseMigrator>().MigrateAsync(CancellationToken.None);

    await host.RunAsync();
    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "YDot.Integration worker stopped because of a start-up or fatal error.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
