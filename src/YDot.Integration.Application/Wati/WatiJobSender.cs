using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.ExternalApis;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Retry;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Wati;

/// <summary>
/// Sends one WATI job: its queued messages, in batches of Wati:BatchSize, through
/// POST /{tenant}/api/v2/sendTemplateMessages.
///
/// THE ORDER OF WRITES IS WHAT MAKES IT RESTART-SAFE:
///   1. the batch row is committed (status Sending);
///   2. its messages are claimed with a conditional UPDATE (Queued -> Sending), so a cancellation
///      or a second dispatcher can never take the same messages;
///   3. only then does the HTTP request go out, carrying one client-generated localMessageId per
///      receiver;
///   4. the per-receiver answer is matched back by localMessageId and committed.
///
/// If the worker stops between 3 and 4, the batch is still "Sending" on the next run. It is then
/// treated as UNCERTAIN: each of its messages is looked up at WATI by its localMessageId, and only
/// a message WATI has no record of is reconsidered - never blindly sent again.
/// </summary>
public sealed class WatiJobSender(
    IIntegrationDbContext db,
    IWatiClient client,
    RetryExecutor retry,
    ILeaseStore leases,
    IJobJournal journal,
    IOptions<WatiOptions> options,
    IOptions<RetryOptions> retryOptions,
    TimeProvider time,
    ILogger<WatiJobSender> logger)
{
    /// <summary>WATI processes a batch asynchronously; give it this long before asking about an uncertain message.</summary>
    private static readonly TimeSpan UncertainLookupDelay = TimeSpan.FromSeconds(10);

    private enum BatchOutcome
    {
        /// <summary>The batch finished (accepted, rejected or resolved); carry on with the next one.</summary>
        Continue,

        /// <summary>WATI is temporarily unavailable; the messages went back to the queue for later.</summary>
        TryLater,

        /// <summary>A failure that every further batch would hit too (bad token, unknown template).</summary>
        StopJob,
    }

    private WatiOptions Settings => options.Value;

    public async Task SendAsync(Guid watiJobId, CancellationToken cancellationToken)
    {
        var watiJob = await db.WatiJobs.SingleOrDefaultAsync(w => w.Id == watiJobId, cancellationToken);
        if (watiJob is null || watiJob.IsTerminal)
        {
            return;
        }

        var jobLabel = watiJob.IntegrationJobId is { } integrationJobId
            ? await db.IntegrationJobs.Where(j => j.Id == integrationJobId).Select(j => j.JobNumber).FirstOrDefaultAsync(cancellationToken)
              ?? watiJob.Reference
            : watiJob.Reference;

        var now = time.GetUtcNow();
        if (watiJob.Status == WatiJobStatus.Queued)
        {
            watiJob.Status = WatiJobStatus.Sending;
            watiJob.StartedAtUtc ??= now;
        }

        watiJob.ProcessingAttempts++;
        watiJob.NextAttemptAtUtc = null;
        watiJob.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        if (!client.IsConfigured)
        {
            await FailRemainingAsync(watiJob, ErrorCodes.WatiNotConfigured,
                "WATI is not configured: set Wati__TenantId, Wati__ApiToken and Wati__ChannelNumber in the worker's .env file, then retry.",
                cancellationToken);
            await FinishAsync(watiJob, jobLabel, cancellationToken);
            return;
        }

        // ---- 1. Batches a previous run left in flight: verify before sending anything new -------
        var interrupted = await db.WatiBatches
            .Where(b => b.WatiJobId == watiJob.Id && (b.Status == WatiBatchStatus.Sending || b.Status == WatiBatchStatus.Uncertain))
            .OrderBy(b => b.BatchNumber)
            .ToListAsync(cancellationToken);

        foreach (var batch in interrupted)
        {
            logger.LogWarning("{JobLabel}: batch {Batch} of {WatiJob} was in flight when the worker stopped; verifying its messages at WATI.",
                jobLabel, batch.BatchNumber, watiJob.Reference);
            await ResolveUncertainBatchAsync(watiJob, batch, jobLabel, cancellationToken);
        }

        // ---- 2. Send whatever is queued, batch by batch -------------------------------------------
        var batchNumber = await db.WatiBatches
            .Where(b => b.WatiJobId == watiJob.Id)
            .MaxAsync(b => (int?)b.BatchNumber, cancellationToken) ?? 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nextIds = await db.WatiMessages
                .Where(m => m.WatiJobId == watiJob.Id && m.Status == WatiMessageStatus.Queued)
                .OrderBy(m => m.QueuedAtUtc)
                .ThenBy(m => m.Id)
                .Select(m => m.Id)
                .Take(Settings.BatchSize)
                .ToListAsync(cancellationToken);

            if (nextIds.Count == 0)
            {
                break;
            }

            batchNumber++;
            var outcome = await SendBatchAsync(watiJob, batchNumber, nextIds, jobLabel, cancellationToken);

            if (outcome == BatchOutcome.StopJob)
            {
                break;
            }

            if (outcome == BatchOutcome.TryLater)
            {
                var wait = retry.DelayBeforeRetry(retryOptions.Value.MaxAttempts + 1);
                watiJob.NextAttemptAtUtc = time.GetUtcNow().Add(wait);
                watiJob.UpdatedAtUtc = time.GetUtcNow();
                await db.SaveChangesAsync(CancellationToken.None);
                logger.LogWarning("{JobLabel}: WATI is unavailable; {WatiJob} continues in {Wait}.", jobLabel, watiJob.Reference, wait);
                return;
            }

            if (!await leases.RenewWatiJobAsync(watiJob.Id, cancellationToken))
            {
                logger.LogWarning("{JobLabel}: lost the lease on {WatiJob}; another instance will continue it.", jobLabel, watiJob.Reference);
                return;
            }

            await ReloadJobAsync(watiJob, cancellationToken);
            if (watiJob.IsTerminal)
            {
                // Finished from elsewhere meanwhile (a cancellation skipped what was left); that outcome stands.
                return;
            }

            if (Settings.DelayBetweenBatchesMilliseconds > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Settings.DelayBetweenBatchesMilliseconds), time, cancellationToken);
            }
        }

        await FinishAsync(watiJob, jobLabel, cancellationToken);
    }

    // =============================================================================== one batch

    private async Task<BatchOutcome> SendBatchAsync(
        WatiJob watiJob, int batchNumber, IReadOnlyCollection<Guid> messageIds, string jobLabel, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var batch = new WatiBatch
        {
            TenantId = watiJob.TenantId,
            WatiJobId = watiJob.Id,
            BatchNumber = batchNumber,
            Status = WatiBatchStatus.Sending,
            CreatedAtUtc = now,
            StartedAtUtc = now,
        };
        db.WatiBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        // Claim the messages for this batch. The status condition is the safety net: a message a
        // cancellation skipped a moment ago is not picked up.
        await db.WatiMessages
            .Where(m => messageIds.Contains(m.Id) && m.Status == WatiMessageStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, WatiMessageStatus.Sending)
                .SetProperty(m => m.WatiBatchId, batch.Id)
                .SetProperty(m => m.StartedAtUtc, now)
                .SetProperty(m => m.UpdatedAtUtc, now), cancellationToken);

        var messages = await db.WatiMessages
            .Where(m => m.WatiBatchId == batch.Id && m.Status == WatiMessageStatus.Sending)
            .OrderBy(m => m.QueuedAtUtc)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            db.WatiBatches.Remove(batch);
            await db.SaveChangesAsync(cancellationToken);
            return BatchOutcome.Continue;
        }

        var channel = string.IsNullOrWhiteSpace(watiJob.ChannelNumber) ? Settings.ChannelNumber : watiJob.ChannelNumber;
        var request = new WatiSendRequest(
            watiJob.TemplateName,
            watiJob.BroadcastName,
            channel,
            messages.Select(m => new WatiReceiver(m.WhatsAppNumber, m.LocalMessageId, TemplateParameter.FromJson(m.ParametersJson))).ToList());

        batch.RecipientCount = messages.Count;
        batch.RequestPayloadJson = JsonDefaults.Serialize(new
        {
            template_name = request.TemplateName,
            broadcast_name = request.BroadcastName,
            channel_number = request.ChannelNumber,
            receivers = request.Receivers.Select(r => new { whatsappNumber = r.WhatsAppNumber, localMessageId = r.LocalMessageId, customParams = r.CustomParams }),
        });
        await db.SaveChangesAsync(cancellationToken);

        var context = new ApiCallContext
        {
            TenantId = watiJob.TenantId,
            JobId = watiJob.IntegrationJobId,
            WatiJobId = watiJob.Id,
            RelatedEntityId = batch.Id,
            JobLabel = jobLabel,
            StepCode = StepCodes.WatiSendBatch,
        };
        var scope = new RetryScope(jobLabel, StepCodes.WatiSendBatch, watiJob.TenantId, watiJob.IntegrationJobId, watiJob.Id);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await retry.ExecuteAsync(
                scope,
                (attempt, token) =>
                {
                    batch.AttemptCount = attempt;
                    return client.SendTemplateMessagesAsync(request, context, attempt, token);
                },
                exception => exception.IsSafeToRetryWithSideEffects,
                cancellationToken);

            stopwatch.Stop();
            ApplyResult(batch, messages, result, stopwatch.ElapsedMilliseconds);
            await db.SaveChangesAsync(CancellationToken.None);

            var message = $"{jobLabel}: WATI batch {batchNumber} of {watiJob.Reference} ({watiJob.TemplateName}) - "
                          + $"{batch.AcceptedCount} accepted, {batch.RejectedCount} rejected (HTTP {result.HttpStatusCode}, {stopwatch.ElapsedMilliseconds} ms).";
            logger.LogInformation("{Message}", message);
            await journal.EventAsync(new JournalEvent
            {
                EventCode = EventCodes.WatiBatchSent,
                Level = batch.RejectedCount > 0 ? EventLevel.Warning : EventLevel.Information,
                Message = message,
                TenantId = watiJob.TenantId,
                JobId = watiJob.IntegrationJobId,
                WatiJobId = watiJob.Id,
                StepCode = StepCodes.WatiSendBatch,
                Data = new { batchNumber, batch.RecipientCount, batch.AcceptedCount, batch.RejectedCount, result.HttpStatusCode },
            }, CancellationToken.None);

            return BatchOutcome.Continue;
        }
        catch (ExternalApiException exception) when (exception.IsSafeToRetryWithSideEffects)
        {
            // 429 / 503 / connection refused, and the retries ran out: nothing was processed, so the
            // messages simply go back to the queue and the job is tried again later.
            stopwatch.Stop();
            var requeued = 0;
            var givenUp = 0;

            foreach (var message in messages)
            {
                message.RetryCount++;
                message.UpdatedAtUtc = time.GetUtcNow();

                if (message.RetryCount > retryOptions.Value.MaxAttempts)
                {
                    MarkFailed(message, ErrorCodes.WatiUnavailable,
                        $"WATI stayed unavailable after {message.RetryCount} attempts: {RetryExecutor.Describe(exception)}.", exception.StatusCode);
                    givenUp++;
                }
                else
                {
                    message.Status = WatiMessageStatus.Queued;
                    message.WatiBatchId = null;
                    message.RequestStatus = WatiRequestStatus.NotSent;
                    message.ErrorMessage = Text.Truncate(exception.Message, 2000);
                    requeued++;
                }
            }

            CloseBatch(batch, WatiBatchStatus.Failed, exception.StatusCode, exception.ResponseBody, exception.Message, stopwatch.ElapsedMilliseconds);
            await db.SaveChangesAsync(CancellationToken.None);

            await journal.EventAsync(new JournalEvent
            {
                EventCode = EventCodes.WatiBatchFailed,
                Level = EventLevel.Warning,
                Message = $"{jobLabel}: WATI batch {batchNumber} could not be sent ({RetryExecutor.Describe(exception)}); "
                          + $"{requeued} message(s) back in the queue, {givenUp} given up.",
                TenantId = watiJob.TenantId,
                JobId = watiJob.IntegrationJobId,
                WatiJobId = watiJob.Id,
                StepCode = StepCodes.WatiSendBatch,
            }, CancellationToken.None);

            return requeued > 0 ? BatchOutcome.TryLater : BatchOutcome.Continue;
        }
        catch (ExternalApiException exception) when (exception.OutcomeUncertain)
        {
            // Timeout / 500 / 502 / 504: WATI may or may not have queued the messages.
            stopwatch.Stop();
            CloseBatch(batch, WatiBatchStatus.Uncertain, exception.StatusCode, exception.ResponseBody, exception.Message, stopwatch.ElapsedMilliseconds);
            foreach (var message in messages)
            {
                message.RequestStatus = WatiRequestStatus.Uncertain;
                message.HttpStatusCode = exception.StatusCode;
            }

            await db.SaveChangesAsync(CancellationToken.None);

            await journal.EventAsync(new JournalEvent
            {
                EventCode = EventCodes.WatiBatchUncertain,
                Level = EventLevel.Warning,
                Message = $"{jobLabel}: WATI did not confirm batch {batchNumber} ({RetryExecutor.Describe(exception)}); "
                          + "checking each message by its localMessageId before deciding anything.",
                TenantId = watiJob.TenantId,
                JobId = watiJob.IntegrationJobId,
                WatiJobId = watiJob.Id,
                StepCode = StepCodes.WatiSendBatch,
            }, CancellationToken.None);

            await ResolveUncertainBatchAsync(watiJob, batch, jobLabel, cancellationToken);
            return BatchOutcome.Continue;
        }
        catch (ExternalApiException exception)
        {
            // 400 / 401 / 403 / 404: the same request would fail the same way. Every remaining
            // message would hit it too, so the whole job stops here with the reason on every row.
            stopwatch.Stop();
            var (code, reason) = exception.IsAuthenticationFailure
                ? (ErrorCodes.WatiUnauthorized,
                    $"WATI refused the API token (HTTP {exception.StatusCode}): it has expired or is wrong. An administrator must update the worker's WATI token (Wati__ApiToken); then retry.")
                : (ErrorCodes.WatiRejected, $"WATI refused the request (HTTP {exception.StatusCode}): {exception.Message}");

            foreach (var message in messages)
            {
                MarkFailed(message, code, reason, exception.StatusCode);
                message.RequestStatus = WatiRequestStatus.Rejected;
            }

            CloseBatch(batch, WatiBatchStatus.Failed, exception.StatusCode, exception.ResponseBody, reason, stopwatch.ElapsedMilliseconds);
            watiJob.ErrorCode = code;
            watiJob.ErrorMessage = reason;
            await db.SaveChangesAsync(CancellationToken.None);

            logger.LogError("{JobLabel}: {Reason}", jobLabel, reason);
            await journal.EventAsync(new JournalEvent
            {
                EventCode = EventCodes.WatiBatchFailed,
                Level = EventLevel.Error,
                Message = $"{jobLabel}: WATI batch {batchNumber} failed - {reason}",
                TenantId = watiJob.TenantId,
                JobId = watiJob.IntegrationJobId,
                WatiJobId = watiJob.Id,
                StepCode = StepCodes.WatiSendBatch,
                Data = new { batchNumber, exception.StatusCode, body = Text.Truncate(exception.ResponseBody, 500) },
            }, CancellationToken.None);

            await FailRemainingAsync(watiJob, code, reason, CancellationToken.None);
            return BatchOutcome.StopJob;
        }
    }

    /// <summary>Matches WATI's per-receiver answer to each message by localMessageId (or number).</summary>
    private void ApplyResult(WatiBatch batch, IReadOnlyList<WatiMessage> messages, WatiSendResult result, long durationMs)
    {
        var now = time.GetUtcNow();
        var byLocalId = result.Receivers
            .Where(r => !string.IsNullOrWhiteSpace(r.LocalMessageId))
            .GroupBy(r => r.LocalMessageId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byNumber = result.Receivers
            .Where(r => !string.IsNullOrWhiteSpace(r.WaId))
            .GroupBy(r => PhoneNumbers.PhoneNumberNormalizer.DigitsOnly(r.WaId), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var invalidNumbers = result.InvalidWhatsAppNumbers
            .Select(PhoneNumbers.PhoneNumberNormalizer.DigitsOnly)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var message in messages)
        {
            var receiver = byLocalId.GetValueOrDefault(message.LocalMessageId) ?? byNumber.GetValueOrDefault(message.WhatsAppNumber);

            message.HttpStatusCode = result.HttpStatusCode;
            message.CompletedAtUtc = now;
            message.DurationMs = durationMs;
            message.UpdatedAtUtc = now;
            message.WatiResponseJson = receiver?.RawJson ?? JsonDefaults.Serialize(new
            {
                result = result.Result,
                error = result.Error,
                invalidWhatsappNumbers = invalidNumbers.Contains(message.WhatsAppNumber) ? new[] { message.WhatsAppNumber } : Array.Empty<string>(),
            });

            if (!result.Result && result.Receivers.Count == 0)
            {
                MarkFailed(message, ErrorCodes.WatiRejected, result.Error ?? "WATI answered result=false without a reason.", result.HttpStatusCode);
                message.RequestStatus = WatiRequestStatus.Rejected;
            }
            // isValidWhatsAppNumber IS NOT A REFUSAL. WATI answers false for numbers it has not validated
            // itself - which, since WhatsApp stopped offering a contact check, is most of them - and
            // sends the message all the same. Seen on 10 Oct 2026: three receivers answered
            // isValidWhatsAppNumber=false with no errors; WATI sent all three, two were delivered and
            // read, and one failed later inside WhatsApp. So a receiver is refused only when WATI lists
            // errors for it or names it in the v1 invalidWhatsappNumbers list; what then happens to an
            // accepted message is read back by the status sync.
            else if (receiver is { Errors.Count: > 0 } || invalidNumbers.Contains(message.WhatsAppNumber))
            {
                var errors = receiver?.Errors is { Count: > 0 } list ? string.Join("; ", list) : "WATI says this is not a valid WhatsApp number.";
                MarkFailed(message, ErrorCodes.WatiInvalidNumber, errors, result.HttpStatusCode);
                message.IsValidWhatsAppNumber = receiver?.IsValidWhatsAppNumber ?? false;
                message.RequestStatus = WatiRequestStatus.Rejected;
            }
            else if (!result.Result)
            {
                MarkFailed(message, ErrorCodes.WatiRejected, result.Error ?? "WATI answered result=false.", result.HttpStatusCode);
                message.RequestStatus = WatiRequestStatus.Rejected;
            }
            else
            {
                message.Status = WatiMessageStatus.Pending;
                message.RequestStatus = WatiRequestStatus.Accepted;

                // Kept as WATI reported it (often false, see above); it does not decide anything.
                message.IsValidWhatsAppNumber = receiver?.IsValidWhatsAppNumber;
                message.ErrorMessage = null;
                message.NextStatusCheckAtUtc = now.AddSeconds(Settings.StatusSync.FirstCheckAfterSeconds);
            }
        }

        batch.AcceptedCount = messages.Count(m => m.RequestStatus == WatiRequestStatus.Accepted);
        batch.RejectedCount = messages.Count - batch.AcceptedCount;
        var status = batch.RejectedCount == 0
            ? WatiBatchStatus.Sent
            : batch.AcceptedCount > 0 ? WatiBatchStatus.PartiallyAccepted : WatiBatchStatus.Failed;
        CloseBatch(batch, status, result.HttpStatusCode, result.RawBody, batch.RejectedCount > 0 ? $"{batch.RejectedCount} receiver(s) rejected." : null, durationMs);
    }

    // =============================================================================== uncertainty

    /// <summary>
    /// Asks WATI about every message of a batch whose outcome is unknown. Found -> it was accepted
    /// (and its current status is taken). Not found -> requeued when Wati:ResendWhenDeliveryUnknown
    /// is on, otherwise failed with WATI_DELIVERY_UNCERTAIN for a person to decide.
    /// </summary>
    private async Task ResolveUncertainBatchAsync(WatiJob watiJob, WatiBatch batch, string jobLabel, CancellationToken cancellationToken)
    {
        var sinceSend = time.GetUtcNow() - (batch.StartedAtUtc ?? batch.CreatedAtUtc);
        if (sinceSend < UncertainLookupDelay)
        {
            await Task.Delay(UncertainLookupDelay - sinceSend, time, cancellationToken);
        }

        var messages = await db.WatiMessages
            .Where(m => m.WatiBatchId == batch.Id && m.Status == WatiMessageStatus.Sending)
            .ToListAsync(cancellationToken);

        int found = 0, requeued = 0, failed = 0;
        var context = new ApiCallContext
        {
            TenantId = watiJob.TenantId,
            JobId = watiJob.IntegrationJobId,
            WatiJobId = watiJob.Id,
            RelatedEntityId = batch.Id,
            JobLabel = jobLabel,
            StepCode = StepCodes.WatiSendBatch,
        };

        foreach (var message in messages)
        {
            WatiMessageLookup? lookup = null;
            try
            {
                lookup = await client.GetMessageAsync(message.LocalMessageId, context with { RelatedEntityId = message.Id }, cancellationToken);
            }
            catch (ExternalApiException exception)
            {
                logger.LogWarning("{JobLabel}: could not look up message {LocalMessageId}: {Reason}", jobLabel, message.LocalMessageId, exception.Message);
            }

            var now = time.GetUtcNow();
            message.UpdatedAtUtc = now;

            if (lookup is { Found: true })
            {
                message.Status = WatiMessageStatus.Pending;
                WatiStatusMapping.Apply(message, lookup, now);
                message.RequestStatus = WatiRequestStatus.Accepted;
                message.CompletedAtUtc ??= now;
                message.NextStatusCheckAtUtc = now.AddSeconds(Settings.StatusSync.CheckIntervalSeconds);
                found++;
            }
            else if (Settings.ResendWhenDeliveryUnknown && message.RetryCount < retryOptions.Value.MaxAttempts)
            {
                message.Status = WatiMessageStatus.Queued;
                message.WatiBatchId = null;
                message.RetryCount++;
                message.RequestStatus = WatiRequestStatus.NotSent;
                requeued++;
            }
            else
            {
                MarkFailed(message, ErrorCodes.WatiDeliveryUncertain,
                    "WATI did not confirm this message after the request failed, and has no record of it. It was not sent again "
                    + "automatically to avoid a duplicate message; retry the job to send it.", message.HttpStatusCode);
                message.RequestStatus = WatiRequestStatus.Uncertain;
                failed++;
            }
        }

        batch.AcceptedCount = found;
        batch.RejectedCount = failed;
        batch.Status = failed == 0 && requeued == 0
            ? WatiBatchStatus.Sent
            : found > 0 ? WatiBatchStatus.PartiallyAccepted : WatiBatchStatus.Failed;
        batch.CompletedAtUtc ??= time.GetUtcNow();
        batch.ErrorMessage = Text.Truncate(
            $"{batch.ErrorMessage} Verified by localMessageId: {found} found at WATI, {requeued} requeued, {failed} unconfirmed.".Trim(), 2000);
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation("{JobLabel}: uncertain batch {Batch} resolved - {Found} found at WATI, {Requeued} requeued, {Failed} unconfirmed.",
            jobLabel, batch.BatchNumber, found, requeued, failed);
    }

    // =============================================================================== finishing

    /// <summary>
    /// Renewing the lease is an UPDATE of the job's row from another connection, and every UPDATE
    /// gives a PostgreSQL row a new xmin - the job's concurrency token. Without reading the row
    /// again, the next save of the job (the finish) matched no row and failed the round. Reading it
    /// again takes the new xmin over, and brings in a change made meanwhile (a cancellation).
    /// </summary>
    private Task ReloadJobAsync(WatiJob watiJob, CancellationToken cancellationToken) =>
        db.ChangeTracker.Entries<WatiJob>().First(entry => ReferenceEquals(entry.Entity, watiJob)).ReloadAsync(cancellationToken);

    private async Task FailRemainingAsync(WatiJob watiJob, string code, string reason, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await db.WatiMessages
            .Where(m => m.WatiJobId == watiJob.Id && m.Status == WatiMessageStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, WatiMessageStatus.Failed)
                .SetProperty(m => m.FailedCode, code)
                .SetProperty(m => m.ErrorMessage, reason)
                .SetProperty(m => m.FailedAtUtc, now)
                .SetProperty(m => m.UpdatedAtUtc, now), cancellationToken);

        watiJob.ErrorCode = code;
        watiJob.ErrorMessage = reason;
        watiJob.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task FinishAsync(WatiJob watiJob, string jobLabel, CancellationToken cancellationToken)
    {
        await WatiJobCounters.RefreshAsync(db, watiJob, cancellationToken);

        if (watiJob.QueuedCount > 0)
        {
            // Something went back to the queue (or is still in flight): not finished yet.
            watiJob.NextAttemptAtUtc = time.GetUtcNow().AddSeconds(5);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = time.GetUtcNow();
        watiJob.Status = watiJob.AcceptedCount == 0 && watiJob.FailedCount > 0
            ? WatiJobStatus.Failed
            : watiJob.FailedCount > 0 ? WatiJobStatus.CompletedWithErrors : WatiJobStatus.Completed;
        watiJob.CompletedAtUtc = now;
        watiJob.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        var message = $"{jobLabel}: WATI job {watiJob.Reference} {watiJob.Status} - {watiJob.AcceptedCount} accepted, "
                      + $"{watiJob.FailedCount} failed, {watiJob.SkippedCount} skipped.";
        logger.LogInformation("{Message}", message);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.WatiJobCompleted,
            Level = watiJob.Status == WatiJobStatus.Completed ? EventLevel.Information : EventLevel.Warning,
            Message = message,
            TenantId = watiJob.TenantId,
            JobId = watiJob.IntegrationJobId,
            WatiJobId = watiJob.Id,
            StepCode = StepCodes.WatiSendBatch,
        }, cancellationToken);
    }

    private void MarkFailed(WatiMessage message, string code, string reason, int? httpStatusCode)
    {
        var now = time.GetUtcNow();
        message.Status = WatiMessageStatus.Failed;
        message.FailedCode = code;
        message.FailedReason = Text.Truncate(reason, 2000);
        message.ErrorMessage = Text.Truncate(reason, 2000);
        message.HttpStatusCode = httpStatusCode ?? message.HttpStatusCode;
        message.FailedAtUtc = now;
        message.CompletedAtUtc ??= now;
        message.NextStatusCheckAtUtc = null;
        message.UpdatedAtUtc = now;
    }

    private void CloseBatch(WatiBatch batch, WatiBatchStatus status, int? httpStatus, string? body, string? error, long durationMs)
    {
        batch.Status = status;
        batch.HttpStatusCode = httpStatus;
        batch.ResponseBody = Text.Truncate(body, 8000);
        batch.ErrorMessage = Text.Truncate(error, 2000);
        batch.CompletedAtUtc = time.GetUtcNow();
        batch.DurationMs = durationMs;
    }
}
