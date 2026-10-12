using YDot.Integration.Domain.Entities;

namespace YDot.Integration.Application.Abstractions.Diagnostics;

/// <summary>
/// Saves one int_api_call_logs row on its own connection. Never throws: a failure to write the
/// audit row is logged, and the HTTP call's own outcome is what the caller sees.
/// </summary>
public interface IApiCallRecorder
{
    Task RecordAsync(ApiCallLog entry, CancellationToken cancellationToken = default);
}
