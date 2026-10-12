namespace YDot.Integration.Domain.Common;

/// <summary>
/// Marks a row that belongs to exactly one organisation (an IAM tenant).
///
/// The YDot web application reads these tables for the WhatsApp screens and must only ever show
/// an organisation its own rows, so every tenant-owned table carries <see cref="TenantId"/> and
/// the read side filters on it. There is deliberately NO foreign key to iam_tenants: each YDot
/// service owns its own tables and migrates independently, so cross-owner foreign keys would
/// make one service's migration depend on another's.
/// </summary>
public interface ITenantScoped
{
    /// <summary>The organisation (iam_tenants.id) the row belongs to.</summary>
    Guid TenantId { get; }
}
