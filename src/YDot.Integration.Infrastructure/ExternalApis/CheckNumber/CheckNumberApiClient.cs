using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Infrastructure.ExternalApis.CheckNumber;

/// <summary>
/// The CheckNumber.ai batch API. Request shapes are the ones the Phase3 worker proved against the
/// live service: multipart form-data with the part names quoted the way browsers and Postman send
/// them, the API key in X-API-Key, and the status call as a POST with a task_id form field.
///
/// The result archive is downloaded with a SEPARATE HttpClient that has no API key, so the key is
/// never sent to S3. Every attempt is recorded in int_api_call_logs (with S3 signatures removed).
/// </summary>
internal sealed class CheckNumberApiClient(
    HttpClient http,
    IHttpClientFactory httpClients,
    IOptions<CheckNumberOptions> options,
    IApiCallRecorder recorder,
    TimeProvider time) : ICheckNumberClient
{
    public const string StorageClientName = "CheckNumberStorage";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<CheckNumberCreatedTask> CreateTaskAsync(
        string fileName, IReadOnlyCollection<string> numbers, ApiCallContext context, int attempt, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', numbers));
        var safeName = SafeFileName(fileName);
        var taskType = options.Value.TaskType;

        // A fresh request per attempt: a sent HttpRequestMessage cannot be reused.
        var file = new ByteArrayContent(payload);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using var form = new MultipartFormDataContent
        {
            FormField(file, "file", safeName),
            FormField(new StringContent(taskType), "task_type"),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/tasks") { Content = form };

        var (dto, status, body) = await SendAsync<CreateTaskDto>(
            request, "CreateTask",
            $"multipart/form-data: file={safeName} ({numbers.Count} numbers, {payload.Length} bytes), task_type={taskType}",
            context, attempt, cancellationToken);

        if (string.IsNullOrWhiteSpace(dto.TaskId))
        {
            throw new ExternalApiException(ApiProvider.CheckNumber, "CreateTask", "The create-task answer did not include a task_id.", status, body);
        }

        return new CheckNumberCreatedTask(
            dto.TaskId,
            dto.Status ?? "pending",
            dto.Total,
            Text.ParseDecimal(dto.EstimatedAmount?.Amount),
            dto.EstimatedAmount?.Currency,
            dto.Message,
            dto.CreatedAt);
    }

    public async Task<CheckNumberTaskStatus> GetTaskAsync(string taskId, ApiCallContext context, int attempt, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent { FormField(new StringContent(taskId), "task_id") };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/gettasks") { Content = form };

        var (dto, status, _) = await SendAsync<TaskStatusDto>(request, "GetTask", $"multipart/form-data: task_id={taskId}", context, attempt, cancellationToken);

        return new CheckNumberTaskStatus(
            dto.TaskId ?? taskId,
            dto.Status ?? "unknown",
            dto.Total,
            dto.Success,
            dto.Failure,
            dto.ResultUrl,
            dto.CachedCount,
            Text.ParseDecimal(dto.ActualAmount?.Amount),
            dto.ActualAmount?.Currency,
            dto.CreatedAt,
            dto.UpdatedAt,
            status);
    }

    public async Task<CheckNumberDownload> DownloadResultAsync(
        string resultUrl, string destinationPath, ApiCallContext context, int attempt, CancellationToken cancellationToken)
    {
        const string operation = "DownloadResult";

        if (!Uri.TryCreate(resultUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ExternalApiException(ApiProvider.CheckNumberStorage, operation, "result_url is not an absolute https URL.");
        }

        var client = httpClients.CreateClient(StorageClientName);
        var timeout = TimeSpan.FromSeconds(options.Value.DownloadTimeoutSeconds);
        var tempPath = destinationPath + ".partial";
        var started = time.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        int? status = null;
        string? body = null;
        string? error = null;
        long bytes = 0;
        var success = false;

        using var attemptToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptToken.CancelAfter(timeout);

        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, attemptToken.Token);
            status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                body = await response.Content.ReadAsStringAsync(attemptToken.Token);
                throw HttpFailures.FromResponse(ApiProvider.CheckNumberStorage, operation, response, body);
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var file = File.Create(tempPath))
            await using (var stream = await response.Content.ReadAsStreamAsync(attemptToken.Token))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, attemptToken.Token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), attemptToken.Token);
                    hash.AppendData(buffer, 0, read);
                    bytes += read;
                }
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            success = true;
            stopwatch.Stop();
            return new CheckNumberDownload(status.Value, bytes, Convert.ToHexStringLower(hash.GetHashAndReset()), stopwatch.ElapsedMilliseconds);
        }
        catch (ExternalApiException exception)
        {
            error = exception.Message;
            throw;
        }
        catch (HttpRequestException exception)
        {
            var failure = HttpFailures.FromTransport(ApiProvider.CheckNumberStorage, operation, exception);
            error = failure.Message;
            throw failure;
        }
        catch (HttpIOException exception)
        {
            var failure = HttpFailures.Interrupted(ApiProvider.CheckNumberStorage, operation, exception);
            error = failure.Message;
            throw failure;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var failure = HttpFailures.FromTimeout(ApiProvider.CheckNumberStorage, operation, timeout, exception);
            error = failure.Message;
            throw failure;
        }
        finally
        {
            stopwatch.Stop();
            TryDelete(tempPath);

            await recorder.RecordAsync(new ApiCallLog
            {
                Provider = ApiProvider.CheckNumberStorage,
                Operation = operation,
                TenantId = context.TenantId,
                JobId = context.JobId,
                WatiJobId = context.WatiJobId,
                RelatedEntityId = context.RelatedEntityId,
                HttpMethod = "GET",
                Url = Text.RedactUrl(resultUrl),
                RequestBody = null,
                ResponseStatusCode = status,
                ResponseBody = success ? $"{bytes} bytes saved to {Path.GetFileName(destinationPath)}" : Text.Truncate(HttpFailures.RedactSecrets(body), 8000),
                DurationMs = stopwatch.ElapsedMilliseconds,
                Attempt = attempt,
                IsSuccess = success,
                ErrorMessage = Text.Truncate(error, 2000),
                StartedAtUtc = started,
            }, CancellationToken.None);
        }
    }

    private async Task<(T Value, int Status, string Body)> SendAsync<T>(
        HttpRequestMessage request, string operation, string requestSummary, ApiCallContext context, int attempt, CancellationToken cancellationToken)
    {
        var url = new Uri(http.BaseAddress!, request.RequestUri!).ToString();
        var started = time.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        int? status = null;
        string? body = null;
        string? error = null;
        var success = false;

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw HttpFailures.FromResponse(ApiProvider.CheckNumber, operation, response, body);
            }

            try
            {
                var value = JsonSerializer.Deserialize<T>(body, Json) ?? throw new JsonException("The response body was empty.");
                success = true;
                return (value, status.Value, body);
            }
            catch (JsonException exception)
            {
                throw new ExternalApiException(ApiProvider.CheckNumber, operation,
                    $"{operation} returned an unexpected response: {exception.Message}", status, body, inner: exception);
            }
        }
        catch (ExternalApiException exception)
        {
            error = exception.Message;
            throw;
        }
        catch (HttpRequestException exception)
        {
            var failure = HttpFailures.FromTransport(ApiProvider.CheckNumber, operation, exception);
            error = failure.Message;
            throw failure;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own Timeout as a cancellation.
            var failure = HttpFailures.FromTimeout(ApiProvider.CheckNumber, operation, http.Timeout, exception);
            error = failure.Message;
            throw failure;
        }
        finally
        {
            stopwatch.Stop();
            await recorder.RecordAsync(new ApiCallLog
            {
                Provider = ApiProvider.CheckNumber,
                Operation = operation,
                TenantId = context.TenantId,
                JobId = context.JobId,
                WatiJobId = context.WatiJobId,
                RelatedEntityId = context.RelatedEntityId,
                HttpMethod = request.Method.Method,
                Url = Text.RedactUrl(url),
                RequestBody = Text.Truncate(requestSummary, 8000),
                ResponseStatusCode = status,
                ResponseBody = Text.Truncate(HttpFailures.RedactSecrets(body), 8000),
                DurationMs = stopwatch.ElapsedMilliseconds,
                Attempt = attempt,
                IsSuccess = success,
                ErrorMessage = Text.Truncate(error, 2000),
                StartedAtUtc = started,
            }, CancellationToken.None);
        }
    }

    /// <summary>Quotes part names the way browsers and Postman do; .NET leaves them unquoted by default.</summary>
    private static HttpContent FormField(HttpContent content, string name, string? fileName = null)
    {
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = $"\"{name}\"",
            FileName = fileName is null ? null : $"\"{fileName}\"",
        };
        return content;
    }

    private static string SafeFileName(string fileName) =>
        string.Concat(Path.GetFileName(fileName).Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover .partial file is overwritten by the next attempt.
        }
    }

    private sealed record MoneyDto(string? Amount, string? Currency);

    private sealed record CreateTaskDto(string? TaskId, string? Status, int Total, MoneyDto? EstimatedAmount, string? Message, DateTimeOffset? CreatedAt);

    private sealed record TaskStatusDto(
        string? TaskId,
        string? Status,
        int Total,
        int Success,
        int Failure,
        string? ResultUrl,
        int? CachedCount,
        MoneyDto? ActualAmount,
        DateTimeOffset? CreatedAt,
        DateTimeOffset? UpdatedAt);
}
