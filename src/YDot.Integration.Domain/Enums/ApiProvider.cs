namespace YDot.Integration.Domain.Enums;

/// <summary>The external systems the worker calls; recorded on every int_api_call_logs row.</summary>
public enum ApiProvider
{
    /// <summary>api.checknumber.ai</summary>
    CheckNumber,

    /// <summary>The pre-signed S3 link CheckNumber.ai returns as result_url.</summary>
    CheckNumberStorage,

    /// <summary>The WATI tenant API.</summary>
    Wati
}
