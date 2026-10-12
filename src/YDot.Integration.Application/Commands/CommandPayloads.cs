using YDot.Integration.Application.Wati;

namespace YDot.Integration.Application.Commands;

/// <summary>
/// The JSON payloads the YDot web application (CAM API) writes into int_worker_commands.payload_json.
/// Property names are camelCase on the wire. THIS IS A CONTRACT WITH THE CAM API - add fields,
/// never rename them.
/// </summary>
public sealed record RetryJobPayload(Guid JobId);

/// <summary>Stop a job that is still running.</summary>
public sealed record CancelJobPayload(Guid JobId, string? Reason);

/// <summary>A broadcast requested from the Wati page.</summary>
/// <param name="TemplateName">WATI elementName.</param>
/// <param name="BroadcastName">Name WATI groups the messages under.</param>
/// <param name="Parameters">Template values, the same for every recipient.</param>
/// <param name="Recipients">Numbers in E.164 (+919500873627) or digits (919500873627).</param>
/// <param name="CampaignId">The campaign the message is about, when one was chosen.</param>
public sealed record SendWatiBroadcastPayload(
    string TemplateName,
    string BroadcastName,
    IReadOnlyList<TemplateParameter>? Parameters,
    IReadOnlyList<string> Recipients,
    Guid? CampaignId,
    string? TemplateLabel);
