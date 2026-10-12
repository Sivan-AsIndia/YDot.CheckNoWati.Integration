using Microsoft.Extensions.Options;

namespace YDot.Integration.Application.Options;

/// <summary>
/// Start-up checks for every settings section. A wrong value stops the host with a message that
/// names the setting, instead of failing later in the middle of a paid job.
/// </summary>
internal static class Check
{
    public static ValidateOptionsResult Result(List<string> errors) =>
        errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);

    public static void Positive(List<string> errors, int value, string name)
    {
        if (value <= 0)
        {
            errors.Add($"{name} must be greater than 0 (it is {value}).");
        }
    }

    public static void NotNegative(List<string> errors, int value, string name)
    {
        if (value < 0)
        {
            errors.Add($"{name} cannot be negative (it is {value}).");
        }
    }

    public static void AbsoluteHttpsUrl(List<string> errors, string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{name} must be an absolute https URL (it is '{value}').");
        }
    }
}

internal sealed class WorkerOptionsValidator : IValidateOptions<WorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DataRoot))
        {
            errors.Add("Worker:DataRoot is required.");
        }

        Check.Positive(errors, options.ScanIntervalSeconds, "Worker:ScanIntervalSeconds");
        Check.NotNegative(errors, options.FileStabilitySeconds, "Worker:FileStabilitySeconds");
        Check.Positive(errors, options.MaxFileSizeMegabytes, "Worker:MaxFileSizeMegabytes");
        Check.Positive(errors, options.MaxConcurrentJobs, "Worker:MaxConcurrentJobs");
        Check.Positive(errors, options.JobPollIntervalSeconds, "Worker:JobPollIntervalSeconds");
        Check.Positive(errors, options.CommandPollIntervalSeconds, "Worker:CommandPollIntervalSeconds");
        Check.Positive(errors, options.HeartbeatIntervalSeconds, "Worker:HeartbeatIntervalSeconds");
        Check.Positive(errors, options.MaxConsecutiveStageFailures, "Worker:MaxConsecutiveStageFailures");

        if (options.JobLeaseSeconds < 60)
        {
            errors.Add("Worker:JobLeaseSeconds must be at least 60, or a slow HTTP call could outlive the lock.");
        }

        return Check.Result(errors);
    }
}

internal sealed class CheckNumberOptionsValidator(IOptions<PipelineOptions> pipeline) : IValidateOptions<CheckNumberOptions>
{
    public ValidateOptionsResult Validate(string? name, CheckNumberOptions options)
    {
        var errors = new List<string>();

        Check.AbsoluteHttpsUrl(errors, options.BaseUrl, "CheckNumber:BaseUrl");

        if (pipeline.Value.EnableCheckNumber && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            errors.Add(
                "CheckNumber:ApiKey is not set. Put CheckNumber__ApiKey=<key> in the .env file "
                + "(or set Pipeline:EnableCheckNumber=false to run without CheckNumber.ai).");
        }

        if (string.IsNullOrWhiteSpace(options.TaskType))
        {
            errors.Add("CheckNumber:TaskType is required ('ws' or 'ws_active').");
        }

        Check.Positive(errors, options.RequestTimeoutSeconds, "CheckNumber:RequestTimeoutSeconds");
        Check.NotNegative(errors, options.InitialPollDelaySeconds, "CheckNumber:InitialPollDelaySeconds");
        Check.Positive(errors, options.PollingIntervalSeconds, "CheckNumber:PollingIntervalSeconds");
        Check.Positive(errors, options.MaxPollingAttempts, "CheckNumber:MaxPollingAttempts");
        Check.Positive(errors, options.MinimumNumbersPerTask, "CheckNumber:MinimumNumbersPerTask");
        Check.Positive(errors, options.MaxNumbersPerTask, "CheckNumber:MaxNumbersPerTask");
        Check.NotNegative(errors, options.ResultValidityDays, "CheckNumber:ResultValidityDays");
        Check.Positive(errors, options.DownloadTimeoutSeconds, "CheckNumber:DownloadTimeoutSeconds");
        Check.Positive(errors, options.MaxArchiveEntries, "CheckNumber:MaxArchiveEntries");

        if (options.PollingBackoffMultiplier < 1)
        {
            errors.Add("CheckNumber:PollingBackoffMultiplier must be 1 or more.");
        }

        if (options.MaxPollingIntervalSeconds < options.PollingIntervalSeconds)
        {
            errors.Add("CheckNumber:MaxPollingIntervalSeconds cannot be smaller than PollingIntervalSeconds.");
        }

        if (options.MinimumNumbersPerTask > options.MaxNumbersPerTask)
        {
            errors.Add("CheckNumber:MinimumNumbersPerTask cannot be larger than MaxNumbersPerTask.");
        }

        if (options.MaxExtractedBytes <= 0)
        {
            errors.Add("CheckNumber:MaxExtractedBytes must be greater than 0.");
        }

        return Check.Result(errors);
    }
}

internal sealed class WatiOptionsValidator : IValidateOptions<WatiOptions>
{
    public ValidateOptionsResult Validate(string? name, WatiOptions options)
    {
        var errors = new List<string>();

        // Credentials are NOT required at start-up: a worker that only checks numbers runs without
        // them, and a missing credential is recorded on the WATI job (WATI_NOT_CONFIGURED) where a
        // person will see it. Only values that would make every send wrong are checked here.
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            Check.AbsoluteHttpsUrl(errors, options.BaseUrl, "Wati:BaseUrl");
        }

        if (!string.IsNullOrWhiteSpace(options.ChannelNumber) && !options.ChannelNumber.All(char.IsAsciiDigit))
        {
            errors.Add("Wati:ChannelNumber must be digits only, with the country code (e.g. 919597304379).");
        }

        if (options.BatchSize is < 1 or > 1000)
        {
            errors.Add("Wati:BatchSize must be between 1 and 1000.");
        }

        Check.Positive(errors, options.RequestTimeoutSeconds, "Wati:RequestTimeoutSeconds");
        Check.NotNegative(errors, options.DelayBetweenBatchesMilliseconds, "Wati:DelayBetweenBatchesMilliseconds");
        Check.NotNegative(errors, options.MaxRecipientsPerJob, "Wati:MaxRecipientsPerJob");
        Check.NotNegative(errors, options.DuplicateWindowDays, "Wati:DuplicateWindowDays");
        Check.Positive(errors, options.StatusSync.IntervalSeconds, "Wati:StatusSync:IntervalSeconds");
        Check.Positive(errors, options.StatusSync.CheckIntervalSeconds, "Wati:StatusSync:CheckIntervalSeconds");
        Check.Positive(errors, options.StatusSync.BatchSize, "Wati:StatusSync:BatchSize");
        Check.Positive(errors, options.StatusSync.MaxTrackingHours, "Wati:StatusSync:MaxTrackingHours");
        Check.Positive(errors, options.TemplateSync.IntervalMinutes, "Wati:TemplateSync:IntervalMinutes");

        foreach (var number in options.AllowedNumbers.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            if (!number.Trim().TrimStart('+').All(char.IsAsciiDigit))
            {
                errors.Add($"Wati:AllowedNumbers contains '{number}', which is not a phone number (digits with country code).");
            }
        }

        foreach (var parameter in options.DefaultParameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
            {
                errors.Add("Wati:DefaultParameters has an entry without a Name.");
            }
        }

        foreach (var template in options.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.Name))
            {
                errors.Add("Wati:Templates has an entry without a Name.");
            }
        }

        return Check.Result(errors);
    }
}

internal sealed class RetryOptionsValidator : IValidateOptions<RetryOptions>
{
    public ValidateOptionsResult Validate(string? name, RetryOptions options)
    {
        var errors = new List<string>();

        if (options.MaxAttempts is < 0 or > 10)
        {
            errors.Add("Retry:MaxAttempts must be between 0 and 10.");
        }

        Check.Positive(errors, options.InitialDelaySeconds, "Retry:InitialDelaySeconds");

        if (options.MaxDelaySeconds < options.InitialDelaySeconds)
        {
            errors.Add("Retry:MaxDelaySeconds cannot be smaller than InitialDelaySeconds.");
        }

        return Check.Result(errors);
    }
}
