using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Files;
using YDot.Integration.Application.Options;

namespace YDot.Integration.Infrastructure.Files;

/// <summary>The worker's folder layout under Worker:DataRoot (see <see cref="IDataPaths"/>).</summary>
internal sealed class DataPaths : IDataPaths
{
    public DataPaths(IOptions<WorkerOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.DataRoot;
        Root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
        Inbox = Path.Combine(Root, "inbox");
        FilesRoot = Path.Combine(Root, "files");
        CheckNumberRoot = Path.Combine(Root, "checknumber");
        OutputRoot = Path.Combine(Root, "output");
        RejectedRoot = Path.Combine(Root, "rejected");
    }

    public string Root { get; }

    public string Inbox { get; }

    public string FilesRoot { get; }

    public string CheckNumberRoot { get; }

    public string OutputRoot { get; }

    public string RejectedRoot { get; }

    public void EnsureCreated()
    {
        foreach (var folder in (string[])[Root, Inbox, FilesRoot, CheckNumberRoot, OutputRoot, RejectedRoot])
        {
            Directory.CreateDirectory(folder);
        }
    }

    public string InputFileFolder(Guid inputFileId, DateTimeOffset receivedAtUtc) =>
        Path.Combine(FilesRoot, receivedAtUtc.ToString("yyyy", CultureInfo.InvariantCulture),
            receivedAtUtc.ToString("MM", CultureInfo.InvariantCulture), inputFileId.ToString());

    public string CheckNumberFolder(string jobNumber, int taskSequence) =>
        taskSequence <= 1
            ? Path.Combine(CheckNumberRoot, jobNumber)
            : Path.Combine(CheckNumberRoot, jobNumber, $"task-{taskSequence:D2}");

    public string OutputFolder(string jobNumber) => Path.Combine(OutputRoot, jobNumber);

    public string RejectedFolder(Guid inputFileId, DateTimeOffset atUtc) =>
        Path.Combine(RejectedRoot, atUtc.ToString("yyyy", CultureInfo.InvariantCulture),
            atUtc.ToString("MM", CultureInfo.InvariantCulture), inputFileId.ToString());

    public string ToRelative(string absolutePath) =>
        Path.GetRelativePath(Root, Path.GetFullPath(absolutePath)).Replace('\\', '/');

    public string ToAbsolute(string relativePath) =>
        Path.GetFullPath(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
