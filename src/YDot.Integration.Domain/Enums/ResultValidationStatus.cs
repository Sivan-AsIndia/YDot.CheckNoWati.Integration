namespace YDot.Integration.Domain.Enums;

/// <summary>How a CheckNumber.ai result row relates to the number that was submitted.</summary>
public enum ResultValidationStatus
{
    /// <summary>CheckNumber.ai reported on exactly the submitted number.</summary>
    Checked,

    /// <summary>CheckNumber.ai put a country code in front of the number and checked a different number; the answer is not used.</summary>
    Rewritten,

    /// <summary>The submitted number was absent from the result files.</summary>
    Missing,

    /// <summary>The 'activated' value was neither yes nor no.</summary>
    Unrecognised
}
