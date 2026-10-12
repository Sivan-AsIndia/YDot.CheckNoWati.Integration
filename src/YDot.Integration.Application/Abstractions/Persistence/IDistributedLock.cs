namespace YDot.Integration.Application.Abstractions.Persistence;

/// <summary>
/// A cluster-wide "only one instance does this" lock (PostgreSQL advisory lock), for the jobs
/// that must not run twice at once even though no row is involved - for example the template
/// sync. Returns null when another instance holds the lock.
/// </summary>
public interface IDistributedLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken);
}
