using System.Data.Common;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Pipeline;

namespace YDot.Integration.Application.Common;

/// <summary>Is this a failure that is expected to pass if the same work is tried again later?</summary>
public static class TransientErrors
{
    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case JobStageException stage:
                    return stage.IsTransient;
                case ExternalApiException api:
                    return api.IsTransient;

                // Npgsql sets IsTransient for "connection refused", "server closed the connection",
                // serialization failures and the like.
                case DbException { IsTransient: true }:
                case TimeoutException:
                case IOException and not FileNotFoundException and not DirectoryNotFoundException:
                    return true;
            }
        }

        return false;
    }
}
