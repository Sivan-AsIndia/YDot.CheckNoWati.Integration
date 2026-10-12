namespace YDot.Integration.Application.Pipeline;

/// <summary>
/// A stage could not finish. <see cref="IsTransient"/> decides what happens to the job:
///
///   permanent (default)  the job is marked Failed with <see cref="ErrorCode"/> and the message;
///                        a person can retry it, and it resumes from the stage that failed.
///   transient            the job keeps its status and is picked up again after a growing delay
///                        (network or database trouble that is expected to pass).
/// </summary>
public sealed class JobStageException(string errorCode, string message, bool isTransient = false, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>See <c>ErrorCodes</c>.</summary>
    public string ErrorCode { get; } = errorCode;

    public bool IsTransient { get; } = isTransient;
}
