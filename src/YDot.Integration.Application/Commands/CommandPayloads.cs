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

/// <summary>A broadcast requested from the Wati page: the numbers a person selected, and nothing else.</summary>
/// <param name="TemplateName">WATI elementName.</param>
/// <param name="BroadcastName">Name WATI groups the messages under.</param>
/// <param name="Parameters">Template values, the same for every recipient - and the fallback for a parameter bound to a recipient field the recipient has no value for.</param>
/// <param name="Recipients">Numbers in E.164 (+919500873627) or digits (919500873627). Used when <paramref name="Selection"/> is absent (an older web application).</param>
/// <param name="CampaignId">The campaign the message is about, when one was chosen.</param>
/// <param name="Selection">The selected numbers, each with the check it was picked from. Takes precedence over <paramref name="Recipients"/>.</param>
/// <param name="RecipientGroupId">The recipient group the selection came from (cam_whatsapp_recipient_groups.id).</param>
/// <param name="RecipientGroupName">That group's name at the time of sending.</param>
/// <param name="Tags">Labels for the send.</param>
/// <param name="Bindings">Template parameters filled per recipient (for example {{name}} from the contact name).</param>
public sealed record SendWatiBroadcastPayload(
    string TemplateName,
    string BroadcastName,
    IReadOnlyList<TemplateParameter>? Parameters,
    IReadOnlyList<string> Recipients,
    Guid? CampaignId,
    string? TemplateLabel,
    IReadOnlyList<SelectedRecipient>? Selection = null,
    Guid? RecipientGroupId = null,
    string? RecipientGroupName = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ParameterBinding>? Bindings = null);

/// <summary>One selected number.</summary>
/// <param name="Number">E.164 (+919500873627) or digits.</param>
/// <param name="SourceJobId">
/// The check job (int_integration_jobs.id) the number was picked from. When it is missing, or that
/// job did not verify the number, the newest check of this organisation that did is recorded instead.
/// </param>
public sealed record SelectedRecipient(string Number, Guid? SourceJobId);

/// <summary>A template parameter filled from the recipient rather than typed once for everybody.</summary>
/// <param name="Name">The template parameter, e.g. "name".</param>
/// <param name="Source">Where its value comes from: <see cref="ParameterSources.ContactName"/>.</param>
public sealed record ParameterBinding(string Name, string Source);

/// <summary>The recipient fields a template parameter can be bound to.</summary>
public static class ParameterSources
{
    /// <summary>The name the uploaded file gave beside the number (int_input_file_numbers.contact_name).</summary>
    public const string ContactName = "contactName";

    /// <summary>The number itself, as +91XXXXXXXXXX.</summary>
    public const string PhoneNumber = "phoneNumber";

    public static bool IsKnown(string? source) => source is ContactName or PhoneNumber;
}

/// <summary>Put the retryable failed messages of one finished send back in the queue.</summary>
public sealed record RetryWatiJobPayload(Guid WatiJobId);
