using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Commands;
using YDot.Integration.Application.Diagnostics;
using YDot.Integration.Application.Inbox;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Pipeline;
using YDot.Integration.Application.Pipeline.Stages;
using YDot.Integration.Application.Retry;
using YDot.Integration.Application.Wati;

namespace YDot.Integration.Application;

/// <summary>
/// Registers the workflow: settings (validated at start-up), the five pipeline stages, the WATI
/// services, inbox registration and the command processor.
///
/// THE LIST BELOW IS THE INVENTORY of what the worker does, in pipeline order - a stage that is
/// not registered here simply never runs, and the host refuses to start when a setting is wrong.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Settings, checked when the host starts -------------------------------------------
        services.AddOptions<WorkerOptions>().Bind(configuration.GetSection(WorkerOptions.SectionName)).ValidateOnStart();
        services.AddOptions<PipelineOptions>().Bind(configuration.GetSection(PipelineOptions.SectionName)).ValidateOnStart();
        services.AddOptions<CheckNumberOptions>().Bind(configuration.GetSection(CheckNumberOptions.SectionName)).ValidateOnStart();
        services.AddOptions<WatiOptions>().Bind(configuration.GetSection(WatiOptions.SectionName)).ValidateOnStart();
        services.AddOptions<RetryOptions>().Bind(configuration.GetSection(RetryOptions.SectionName)).ValidateOnStart();
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<OutputOptions>().Bind(configuration.GetSection(OutputOptions.SectionName));

        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();
        services.AddSingleton<IValidateOptions<CheckNumberOptions>, CheckNumberOptionsValidator>();
        services.AddSingleton<IValidateOptions<WatiOptions>, WatiOptionsValidator>();
        services.AddSingleton<IValidateOptions<RetryOptions>, RetryOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RetryExecutor>();

        // ---- 1. Input file -> database ------------------------------------------------------------
        services.AddSingleton<InboxReadinessTracker>();
        services.AddScoped<InboxRegistrationService>();

        // ---- 2. The pipeline, one stage per job status ------------------------------------------------
        services.AddScoped<IJobStage, ParseStage>();         // PARSE_FILE
        services.AddScoped<IJobStage, CheckNumberStage>();   // CHECKNUMBER_CREATE_TASK / POLL / DOWNLOAD / EXTRACT / PROCESS_RESULTS
        services.AddScoped<IJobStage, VerifyStage>();        // IDENTIFY_VERIFIED
        services.AddScoped<IJobStage, WatiStage>();          // WATI_PREPARE / WATI_AWAIT
        services.AddScoped<IJobStage, OutputStage>();        // GENERATE_OUTPUT
        services.AddScoped<JobStageRunner>();
        services.AddSingleton<JobProcessor>();

        // ---- 3. WATI ------------------------------------------------------------------------------------
        services.AddScoped<WatiRecipientPlanner>();
        services.AddScoped<WatiJobFactory>();
        services.AddScoped<WatiJobSender>();
        services.AddScoped<WatiStatusSynchronizer>();
        services.AddScoped<WatiTemplateSynchronizer>();

        // ---- 4. Requests from the web application, and the heartbeat ----------------------------------------
        services.AddScoped<WorkerCommandProcessor>();
        services.AddScoped<WorkerHeartbeat>();

        return services;
    }
}
