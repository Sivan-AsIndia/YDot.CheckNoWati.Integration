using YDot.Integration.Application.Wati;

namespace YDot.Integration.Application.Abstractions.ExternalApis;

/// <summary>One receiver of a template batch.</summary>
public sealed record WatiReceiver(string WhatsAppNumber, string LocalMessageId, IReadOnlyList<TemplateParameter> CustomParams);

/// <summary>A sendTemplateMessages request.</summary>
public sealed record WatiSendRequest(
    string TemplateName,
    string BroadcastName,
    string ChannelNumber,
    IReadOnlyList<WatiReceiver> Receivers);

/// <summary>What WATI said about one receiver.</summary>
public sealed record WatiReceiverResult(
    string? LocalMessageId,
    string? WaId,
    bool? IsValidWhatsAppNumber,
    IReadOnlyList<string> Errors,
    string RawJson);

/// <summary>
/// Answer of POST /{tenant}/api/v2/sendTemplateMessages:
/// { "result": true, "error": null, "templateName": "...", "receivers": [ { "localMessageId",
/// "waId", "isValidWhatsAppNumber", "errors": [] } ] }. The older v1 shape
/// { "result": true, "errors": { "invalidWhatsappNumbers": [] } } is understood as well.
/// </summary>
public sealed record WatiSendResult(
    int HttpStatusCode,
    bool Result,
    string? Error,
    IReadOnlyList<WatiReceiverResult> Receivers,
    IReadOnlyList<string> InvalidWhatsAppNumbers,
    IReadOnlyList<string> InvalidCustomParameters,
    string RawBody);

/// <summary>Answer of GET /{tenant}/api/v1/whatsApp/messages/{channel}/{localMessageId}.</summary>
public sealed record WatiMessageLookup(
    bool Found,
    int HttpStatusCode,
    string? StatusString,
    string? EventType,
    string? FailedCode,
    string? FailedDetail,
    string? WaId,
    string? WatiMessageId,
    string? WhatsAppMessageId,
    string? ConversationId,
    DateTimeOffset? Timestamp,
    string RawJson);

/// <summary>One template from GET /{tenant}/api/v1/getMessageTemplates.</summary>
public sealed record WatiTemplateInfo(
    string ElementName,
    string? Category,
    string? Status,
    string? Language,
    string? Body,
    string? HeaderJson,
    string? Footer,
    string? ButtonsJson,
    IReadOnlyList<TemplateParameter> Parameters);

/// <summary>
/// The WATI tenant API (reference implementation: D:\Permaspace\Goldwin\WatiIntegration).
/// The v2 send is used because it accepts a client-generated localMessageId per receiver, which
/// is what makes individual messages traceable and a timed-out batch verifiable.
/// </summary>
public interface IWatiClient
{
    /// <summary>False when the tenant id, token or channel number is missing.</summary>
    bool IsConfigured { get; }

    Task<WatiSendResult> SendTemplateMessagesAsync(
        WatiSendRequest request, ApiCallContext context, int attempt, CancellationToken cancellationToken);

    /// <summary>Found = false when WATI has no message with that localMessageId (HTTP 404 or empty result).</summary>
    Task<WatiMessageLookup> GetMessageAsync(
        string localMessageId, ApiCallContext context, CancellationToken cancellationToken);

    Task<IReadOnlyList<WatiTemplateInfo>> GetTemplatesAsync(ApiCallContext context, CancellationToken cancellationToken);
}
