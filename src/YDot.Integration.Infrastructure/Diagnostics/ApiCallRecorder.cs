using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Infrastructure.Persistence;

namespace YDot.Integration.Infrastructure.Diagnostics;

/// <summary>Saves int_api_call_logs rows on their own context; a failure is logged, never thrown.</summary>
internal sealed class ApiCallRecorder(IDbContextFactory<IntegrationDbContext> contexts, ILogger<ApiCallRecorder> logger) : IApiCallRecorder
{
    public async Task RecordAsync(ApiCallLog entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await contexts.CreateDbContextAsync(cancellationToken);
            context.ApiCallLogs.Add(entry);
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not record the {Provider} {Operation} call.", entry.Provider, entry.Operation);
        }
    }
}
