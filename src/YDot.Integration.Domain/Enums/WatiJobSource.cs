namespace YDot.Integration.Domain.Enums;

/// <summary>Why a WATI job exists.</summary>
public enum WatiJobSource
{
    /// <summary>Created by the pipeline after CheckNumber.ai verified the numbers of an uploaded file.</summary>
    Automatic,

    /// <summary>Requested from the Wati page (/app/whatsapp/wati) by a person choosing recipients and a template.</summary>
    Manual
}
