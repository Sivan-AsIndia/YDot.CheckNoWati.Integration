namespace YDot.Integration.Domain.Common;

/// <summary>
/// Base type for every persisted row.
///
/// THE ID IS A VERSION-7 UUID, generated in the worker before the row is saved. Version 7 is
/// time-ordered, so new rows land at the end of the primary-key index instead of at random
/// positions (better insert performance on large audit tables), and an entity has its identity
/// before it is ever written - which the pipeline relies on, for example to name a job folder
/// after the input file id before the database insert happens.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}
