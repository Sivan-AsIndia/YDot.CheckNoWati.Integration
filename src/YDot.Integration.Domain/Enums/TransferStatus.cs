namespace YDot.Integration.Domain.Enums;

/// <summary>Progress of a download or an extraction.</summary>
public enum TransferStatus
{
    /// <summary>Not started.</summary>
    Pending,

    /// <summary>Running now.</summary>
    InProgress,

    /// <summary>Finished successfully.</summary>
    Succeeded,

    /// <summary>Finished with an error (see the error column).</summary>
    Failed
}
