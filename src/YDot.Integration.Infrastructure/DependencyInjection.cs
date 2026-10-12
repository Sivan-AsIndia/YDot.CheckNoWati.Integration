using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Options;
using YDot.Integration.Infrastructure.Diagnostics;
using YDot.Integration.Infrastructure.ExternalApis.CheckNumber;
using YDot.Integration.Infrastructure.ExternalApis.Wati;
using YDot.Integration.Infrastructure.Files;
using YDot.Integration.Infrastructure.Persistence;

namespace YDot.Integration.Infrastructure;

/// <summary>Registers PostgreSQL, the HTTP clients, the file system services and the diagnostics.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddIntegrationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{DatabaseOptions.ConnectionStringName} is not set. Put "
                + $"ConnectionStrings__{DatabaseOptions.ConnectionStringName}=Host=...;Port=...;Database=ydotphaseupdated;Username=...;Password=... "
                + "in the .env file.");
        }

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

        // ---- PostgreSQL -------------------------------------------------------------------------
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton(dataSource);

        services.AddDbContextFactory<IntegrationDbContext>(options => options
            .UseNpgsql(dataSource, npgsql => npgsql
                .MigrationsHistoryTable(IntegrationDbContext.MigrationsHistoryTable)
                .CommandTimeout(databaseOptions.CommandTimeoutSeconds))
            .UseSnakeCaseNamingConvention());

        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<IntegrationDbContext>>().CreateDbContext());
        services.AddScoped<IIntegrationDbContext>(provider => provider.GetRequiredService<IntegrationDbContext>());

        services.AddSingleton<ILeaseStore, LeaseStore>();
        services.AddSingleton<IReferenceGenerator, ReferenceGenerator>();
        services.AddSingleton<ITenantDirectory, TenantDirectory>();
        services.AddSingleton<IDistributedLock, PostgresAdvisoryLock>();
        services.AddScoped<IPhoneNumberRegistry, PhoneNumberRegistry>();
        services.AddSingleton<DatabaseMigrator>();

        // ---- Diagnostics ------------------------------------------------------------------------------
        services.AddSingleton<IWorkerIdentity, WorkerIdentity>();
        services.AddSingleton<IJobJournal, JobJournal>();
        services.AddSingleton<IApiCallRecorder, ApiCallRecorder>();

        // ---- Files --------------------------------------------------------------------------------------
        services.AddSingleton<IDataPaths, DataPaths>();
        services.AddSingleton<IInboxFileSystem, InboxFileSystem>();
        services.AddSingleton<INumberFileReader, NumberFileReader>();
        services.AddSingleton<IArchiveExtractor, ZipArchiveExtractor>();

        // ---- CheckNumber.ai -------------------------------------------------------------------------------
        services.AddHttpClient<ICheckNumberClient, CheckNumberApiClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<CheckNumberOptions>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.Add("X-API-Key", settings.ApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // The result archive comes from a pre-signed S3 link: a client WITHOUT the API key, with the
        // timeout applied per attempt by the client code instead.
        services.AddHttpClient(CheckNumberApiClient.StorageClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

        // ---- WATI -------------------------------------------------------------------------------------------
        services.AddHttpClient<IWatiClient, WatiApiClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<WatiOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        });

        return services;
    }
}
