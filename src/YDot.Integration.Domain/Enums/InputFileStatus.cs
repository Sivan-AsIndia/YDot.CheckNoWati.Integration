namespace YDot.Integration.Domain.Enums;

/// <summary>What happened when the worker tried to register a file.</summary>
public enum InputFileStatus
{
    /// <summary>Accepted: a job was created for it.</summary>
    Registered,

    /// <summary>The same content was already processed for the same organisation and campaign; no job was created.</summary>
    Duplicate,

    /// <summary>Could not be used (unsupported type, empty, too large, no organisation); no job was created.</summary>
    Rejected
}
