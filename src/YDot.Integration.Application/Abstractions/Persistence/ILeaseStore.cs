namespace YDot.Integration.Application.Abstractions.Persistence;

/// <summary>
/// Claims work for THIS worker instance so two instances never process the same row.
///
/// Every claim is a single UPDATE ... WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) that sets
/// locked_by / locked_until_utc: a row another instance is claiming at the same moment is simply
/// skipped, and a row whose lease expired (its instance died) becomes claimable again, so work
/// is resumed rather than lost or duplicated.
/// </summary>
public interface ILeaseStore
{
    /// <summary>Integration jobs that are active, unlocked (or lease expired) and due.</summary>
    Task<IReadOnlyList<Guid>> ClaimIntegrationJobsAsync(int maximum, CancellationToken cancellationToken);

    /// <summary>Extends the lease; false when this instance no longer holds it.</summary>
    Task<bool> RenewIntegrationJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task ReleaseIntegrationJobAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>WATI jobs that are Queued or Sending, unlocked and due.</summary>
    Task<IReadOnlyList<Guid>> ClaimWatiJobsAsync(int maximum, CancellationToken cancellationToken);

    Task<bool> RenewWatiJobAsync(Guid watiJobId, CancellationToken cancellationToken);

    Task ReleaseWatiJobAsync(Guid watiJobId, CancellationToken cancellationToken);

    /// <summary>Pending web-app commands, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ClaimCommandsAsync(int maximum, CancellationToken cancellationToken);
}
