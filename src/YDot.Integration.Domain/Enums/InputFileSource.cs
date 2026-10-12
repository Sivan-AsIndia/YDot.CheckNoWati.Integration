namespace YDot.Integration.Domain.Enums;

/// <summary>Where a number file came from.</summary>
public enum InputFileSource
{
    /// <summary>Uploaded from /app/whatsapp/check-number; the CAM API wrote it into the inbox with a manifest.</summary>
    WebUpload,

    /// <summary>Copied straight into the inbox folder by a person or another system, with no manifest.</summary>
    FolderDrop
}
