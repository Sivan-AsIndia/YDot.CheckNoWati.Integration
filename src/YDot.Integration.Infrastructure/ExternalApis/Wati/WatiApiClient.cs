using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Wati;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Infrastructure.ExternalApis.Wati;

/// <summary>
/// The WATI tenant API, as used by the reference project (D:\Permaspace\Goldwin\WatiIntegration):
///
///   POST {base}/{tenant}/api/v2/sendTemplateMessages            template batch, client localMessageId per receiver
///   GET  {base}/{tenant}/api/v1/whatsApp/messages/{channel}/{id} one message's status by localMessageId
///   GET  {base}/{tenant}/api/v1/getMessageTemplates              the account's templates
///
/// Authorization: Bearer {token}. Every call is recorded in int_api_call_logs (the token, being a
/// header, never is).
/// </summary>
internal sealed class WatiApiClient(
    HttpClient http,
    IOptions<WatiOptions> options,
    IApiCallRecorder recorder,
    TimeProvider time) : IWatiClient
{
    private const int TemplatePageSize = 100;
    private const int MaxTemplatePages = 20;

    public bool IsConfigured => options.Value.IsConfigured;

    private WatiOptions Settings => options.Value;

    public async Task<WatiSendResult> SendTemplateMessagesAsync(
        WatiSendRequest request, ApiCallContext context, int attempt, CancellationToken cancellationToken)
    {
        var body = new
        {
            template_name = request.TemplateName,
            broadcast_name = request.BroadcastName,
            channel_number = request.ChannelNumber,
            receivers = request.Receivers.Select(r => new
            {
                whatsappNumber = r.WhatsAppNumber,
                localMessageId = r.LocalMessageId,
                customParams = r.CustomParams.Select(p => new { name = p.Name, value = p.Value }),
            }),
        };

        var requestJson = JsonSerializer.Serialize(body);
        using var message = new HttpRequestMessage(HttpMethod.Post, Url("api/v2/sendTemplateMessages"))
        {
            Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json"),
        };

        var (status, responseBody) = await SendAsync(message, "SendTemplateMessages", requestJson, context, attempt,
            treatNotFoundAsSuccess: false, cancellationToken);

        return ParseSendResult(status, responseBody);
    }

    public async Task<WatiMessageLookup> GetMessageAsync(string localMessageId, ApiCallContext context, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get,
            Url($"api/v1/whatsApp/messages/{Uri.EscapeDataString(Settings.ChannelNumber)}/{Uri.EscapeDataString(localMessageId)}"));

        var (status, body) = await SendAsync(message, "GetMessage", null, context, attempt: 1, treatNotFoundAsSuccess: true, cancellationToken);

        return status == (int)HttpStatusCode.NotFound
            ? new WatiMessageLookup(false, status, null, null, null, null, null, null, null, null, null, body)
            : ParseLookup(status, body);
    }

    public async Task<IReadOnlyList<WatiTemplateInfo>> GetTemplatesAsync(ApiCallContext context, CancellationToken cancellationToken)
    {
        var templates = new List<WatiTemplateInfo>();

        for (var page = 1; page <= MaxTemplatePages; page++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get,
                Url($"api/v1/getMessageTemplates?pageSize={TemplatePageSize}&pageNumber={page}"));

            var (_, body) = await SendAsync(message, "GetTemplates", null, context, attempt: 1, treatNotFoundAsSuccess: false, cancellationToken);
            var pageTemplates = ParseTemplates(body);
            templates.AddRange(pageTemplates);

            if (pageTemplates.Count < TemplatePageSize)
            {
                break;
            }
        }

        return templates;
    }

    // ================================================================================ transport

    private string Url(string relative) =>
        $"{Settings.BaseUrl.TrimEnd('/')}/{Settings.TenantId.Trim().Trim('/')}/{relative.TrimStart('/')}";

    private async Task<(int Status, string Body)> SendAsync(
        HttpRequestMessage message,
        string operation,
        string? requestBody,
        ApiCallContext context,
        int attempt,
        bool treatNotFoundAsSuccess,
        CancellationToken cancellationToken)
    {
        var token = Settings.ApiToken.Trim();
        message.Headers.Authorization = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? AuthenticationHeaderValue.Parse(token)
            : new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var started = time.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        int? status = null;
        string? body = null;
        string? error = null;
        var success = false;

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            status = (int)response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode && !(treatNotFoundAsSuccess && response.StatusCode == HttpStatusCode.NotFound))
            {
                throw HttpFailures.FromResponse(ApiProvider.Wati, operation, response, body);
            }

            success = true;
            return (status.Value, body);
        }
        catch (ExternalApiException exception)
        {
            error = exception.Message;
            throw;
        }
        catch (HttpRequestException exception)
        {
            var failure = HttpFailures.FromTransport(ApiProvider.Wati, operation, exception);
            error = failure.Message;
            throw failure;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var failure = HttpFailures.FromTimeout(ApiProvider.Wati, operation, http.Timeout, exception);
            error = failure.Message;
            throw failure;
        }
        finally
        {
            stopwatch.Stop();
            await recorder.RecordAsync(new ApiCallLog
            {
                Provider = ApiProvider.Wati,
                Operation = operation,
                TenantId = context.TenantId,
                JobId = context.JobId,
                WatiJobId = context.WatiJobId,
                RelatedEntityId = context.RelatedEntityId,
                HttpMethod = message.Method.Method,
                Url = Text.RedactUrl(message.RequestUri?.ToString()),
                RequestBody = Text.Truncate(requestBody, 8000),
                ResponseStatusCode = status,
                ResponseBody = Text.Truncate(body, 8000),
                DurationMs = stopwatch.ElapsedMilliseconds,
                Attempt = attempt,
                IsSuccess = success,
                ErrorMessage = Text.Truncate(error, 2000),
                StartedAtUtc = started,
            }, CancellationToken.None);
        }
    }

    // ================================================================================ parsing

    /// <summary>Understands the v2 shape (receivers[]) and the v1 shape (errors.invalidWhatsappNumbers).</summary>
    private static WatiSendResult ParseSendResult(int status, string body)
    {
        var receivers = new List<WatiReceiverResult>();
        var invalidNumbers = new List<string>();
        var invalidParameters = new List<string>();
        var result = true;
        string? error = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            return new WatiSendResult(status, result, null, receivers, invalidNumbers, invalidParameters, body);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new WatiSendResult(status, result, null, receivers, invalidNumbers, invalidParameters, body);
            }

            if (Property(root, "result") is { ValueKind: JsonValueKind.False })
            {
                result = false;
            }

            error = String(Property(root, "error")) ?? String(Property(root, "message"));

            if (Property(root, "receivers") is { ValueKind: JsonValueKind.Array } list)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var errors = new List<string>();
                    if (Property(item, "errors") is { ValueKind: JsonValueKind.Array } itemErrors)
                    {
                        errors.AddRange(itemErrors.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText()));
                    }

                    receivers.Add(new WatiReceiverResult(
                        String(Property(item, "localMessageId")),
                        String(Property(item, "waId")) ?? String(Property(item, "whatsappNumber")),
                        Property(item, "isValidWhatsAppNumber") is { ValueKind: JsonValueKind.True or JsonValueKind.False } valid ? valid.GetBoolean() : null,
                        errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToList(),
                        item.GetRawText()));
                }
            }

            if (Property(root, "errors") is { ValueKind: JsonValueKind.Object } v1Errors)
            {
                error ??= String(Property(v1Errors, "error"));
                if (Property(v1Errors, "invalidWhatsappNumbers") is { ValueKind: JsonValueKind.Array } numbers)
                {
                    invalidNumbers.AddRange(numbers.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.String ? n.GetString()! : n.GetRawText()));
                }

                if (Property(v1Errors, "invalidCustomParameters") is { ValueKind: JsonValueKind.Array } parameters)
                {
                    invalidParameters.AddRange(parameters.EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String ? p.GetString()! : p.GetRawText()));
                }
            }
        }
        catch (JsonException)
        {
            // A 2xx with a non-JSON body: treat as accepted, keep the body for the audit trail.
        }

        return new WatiSendResult(status, result, string.IsNullOrWhiteSpace(error) ? null : error, receivers, invalidNumbers, invalidParameters, body);
    }

    private static WatiMessageLookup ParseLookup(int status, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var item = Property(root, "result") is { ValueKind: JsonValueKind.Object } result ? result : root;

            if (item.ValueKind != JsonValueKind.Object)
            {
                return new WatiMessageLookup(false, status, null, null, null, null, null, null, null, null, null, body);
            }

            var statusString = String(Property(item, "statusString")) ?? String(Property(item, "status"));
            var localId = String(Property(item, "localMessageId"));
            var id = String(Property(item, "id"));

            return new WatiMessageLookup(
                Found: statusString is not null || localId is not null || id is not null,
                status,
                statusString,
                String(Property(item, "eventType")),
                String(Property(item, "failedCode")),
                String(Property(item, "failedDetail")),
                String(Property(item, "waId")),
                id,
                String(Property(item, "whatsappMessageId")),
                String(Property(item, "conversationId")),
                Timestamp(Property(item, "timestamp")) ?? Timestamp(Property(item, "created")),
                body);
        }
        catch (JsonException)
        {
            return new WatiMessageLookup(false, status, null, null, null, null, null, null, null, null, null, body);
        }
    }

    private static List<WatiTemplateInfo> ParseTemplates(string body)
    {
        var templates = new List<WatiTemplateInfo>();
        using var document = JsonDocument.Parse(body);

        if (Property(document.RootElement, "messageTemplates") is not { ValueKind: JsonValueKind.Array } list)
        {
            return templates;
        }

        foreach (var item in list.EnumerateArray())
        {
            var name = String(Property(item, "elementName"));
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var parameters = new List<TemplateParameter>();
            if (Property(item, "customParams") is { ValueKind: JsonValueKind.Array } customParams)
            {
                foreach (var parameter in customParams.EnumerateArray())
                {
                    var parameterName = String(Property(parameter, "paramName")) ?? String(Property(parameter, "name"));
                    if (!string.IsNullOrWhiteSpace(parameterName))
                    {
                        parameters.Add(new TemplateParameter(parameterName, String(Property(parameter, "paramValue")) ?? String(Property(parameter, "value")) ?? string.Empty));
                    }
                }
            }

            var language = Property(item, "language") switch
            {
                { ValueKind: JsonValueKind.Object } languageObject => String(Property(languageObject, "value")) ?? String(Property(languageObject, "key")),
                { ValueKind: JsonValueKind.String } languageText => languageText.GetString(),
                _ => null,
            };

            templates.Add(new WatiTemplateInfo(
                name,
                String(Property(item, "category")),
                String(Property(item, "status")),
                language,
                String(Property(item, "bodyOriginal")) ?? String(Property(item, "body")),
                Property(item, "header") is { ValueKind: JsonValueKind.Object } header ? header.GetRawText() : null,
                String(Property(item, "footer")),
                Property(item, "buttons") is { ValueKind: JsonValueKind.Array } buttons ? buttons.GetRawText() : null,
                parameters));
        }

        return templates;
    }

    /// <summary>Case-insensitive property lookup (WATI is not consistent about casing).</summary>
    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? String(JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        _ => null,
    };

    /// <summary>An ISO date string or a Unix time in seconds / milliseconds.</summary>
    private static DateTimeOffset? Timestamp(JsonElement? element)
    {
        switch (element)
        {
            case { ValueKind: JsonValueKind.String } text when DateTimeOffset.TryParse(text.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed):
                return parsed;
            case { ValueKind: JsonValueKind.String } text when long.TryParse(text.GetString(), out var unixText):
                return FromUnix(unixText);
            case { ValueKind: JsonValueKind.Number } number when number.TryGetInt64(out var unix):
                return FromUnix(unix);
            default:
                return null;
        }
    }

    private static DateTimeOffset? FromUnix(long value) =>
        value switch
        {
            > 100_000_000_000 => DateTimeOffset.FromUnixTimeMilliseconds(value),
            > 1_000_000_000 => DateTimeOffset.FromUnixTimeSeconds(value),
            _ => null,
        };
}
