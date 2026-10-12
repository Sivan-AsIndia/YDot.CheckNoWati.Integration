namespace YDot.Integration.Application.Options;

/// <summary>
/// How the worker host behaves: where its files live, how often it looks for work, how many
/// jobs it runs at once. Section <c>Worker</c> in appsettings.json.
/// </summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>Name shown in logs and int_worker_instances. Blank = machine name + process id.</summary>
    public string InstanceName { get; set; } = string.Empty;

    /// <summary>
    /// Root of the worker's folders (inbox, files, checknumber, output, rejected). Relative paths
    /// resolve against the content root. In Docker this is the volume shared with the CAM API,
    /// which writes uploads into {DataRoot}/inbox.
    /// </summary>
    public string DataRoot { get; set; } = "data";

    /// <summary>How often the inbox is scanned for new files.</summary>
    public int ScanIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// A file is processed only after its size and modification time have not changed for this
    /// long AND it can be opened exclusively, so a file still being copied is never read half-way.
    /// </summary>
    public int FileStabilitySeconds { get; set; } = 3;

    /// <summary>Files larger than this are refused (the upload page allows 10 MB).</summary>
    public int MaxFileSizeMegabytes { get; set; } = 10;

    /// <summary>
    /// Accept files copied into the inbox by hand (no manifest). The organisation is taken from
    /// the sub-folder name (inbox/{organisation-subdomain}/file.txt) or DefaultTenantSubdomain.
    /// </summary>
    public bool AcceptFolderDrops { get; set; } = true;

    /// <summary>Organisation used for folder drops placed directly in the inbox root.</summary>
    public string? DefaultTenantSubdomain { get; set; }

    /// <summary>Jobs processed in parallel by one worker instance.</summary>
    public int MaxConcurrentJobs { get; set; } = 2;

    /// <summary>
    /// How long a claimed job stays locked to this instance without a renewal. If the instance
    /// dies, another one takes the job over after this.
    /// </summary>
    public int JobLeaseSeconds { get; set; } = 300;

    /// <summary>How often the job queue is checked for due work.</summary>
    public int JobPollIntervalSeconds { get; set; } = 5;

    /// <summary>How often the command table is checked for requests from the web application.</summary>
    public int CommandPollIntervalSeconds { get; set; } = 3;

    /// <summary>How often int_worker_instances is refreshed.</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// A stage that keeps failing with transient errors (database or network down) is retried with
    /// growing delays; after this many failures in a row the job is marked Failed.
    /// </summary>
    public int MaxConsecutiveStageFailures { get; set; } = 10;
}
