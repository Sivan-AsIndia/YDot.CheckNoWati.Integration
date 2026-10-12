namespace YDot.Integration.Domain.Enums;

/// <summary>How widely a saved CheckNumber.ai answer is reused.</summary>
public enum ReuseScope
{
    /// <summary>An answer for a number is reused for every organisation (lowest cost).</summary>
    Global,

    /// <summary>An answer is reused only inside the organisation that paid for it.</summary>
    Tenant
}
