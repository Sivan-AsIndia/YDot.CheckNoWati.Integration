namespace YDot.Integration.Domain.Enums;

/// <summary>How one occurrence of a number in the uploaded file was treated.</summary>
public enum NumberClassification
{
    /// <summary>First time this number appears in the file and it has no saved answer: it is sent to CheckNumber.ai.</summary>
    Fresh,

    /// <summary>First time in the file, but CheckNumber.ai already answered it earlier: the saved answer is reused and nothing is paid.</summary>
    AlreadyChecked,

    /// <summary>The number already appeared earlier in the same file: checked once, recorded again for audit.</summary>
    Duplicate,

    /// <summary>The line held no usable phone number.</summary>
    Invalid
}
