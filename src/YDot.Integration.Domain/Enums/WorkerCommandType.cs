namespace YDot.Integration.Domain.Enums;

/// <summary>Requests the web application leaves in int_worker_commands for the worker to carry out.</summary>
public enum WorkerCommandType
{
    /// <summary>Resume a failed or cancelled integration job from where it stopped.</summary>
    RetryJob,

    /// <summary>Stop an integration job that is still running.</summary>
    CancelJob,

    /// <summary>Send a template to recipients chosen on the Wati page.</summary>
    SendWatiBroadcast,

    /// <summary>Refresh int_wati_templates from WATI now.</summary>
    SyncWatiTemplates,

    /// <summary>
    /// Put the messages of one finished send that failed for a reason a retry can fix (an expired
    /// token, an outage, an unconfirmed batch) back in the queue. Nothing that was accepted is sent
    /// again, and nobody who was not in the original send is added.
    /// </summary>
    RetryWatiJob
}
