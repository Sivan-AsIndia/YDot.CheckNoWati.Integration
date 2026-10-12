using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace YDot.Integration.Infrastructure.Persistence.Configurations;

/// <summary>Column conventions shared by every int_* table.</summary>
internal static class ConfigurationExtensions
{
    /// <summary>
    /// Enums are stored as their NAME (text), never as numbers: the rows stay readable in SQL and
    /// in support queries, and adding an enum member can never silently re-point stored values.
    /// </summary>
    public static PropertyBuilder<TEnum> AsText<TEnum>(this PropertyBuilder<TEnum> property, int maxLength = 40) =>
        property.HasConversion<string>().HasMaxLength(maxLength);

    public static PropertyBuilder<TEnum?> AsText<TEnum>(this PropertyBuilder<TEnum?> property, int maxLength = 40)
        where TEnum : struct, Enum =>
        property.HasConversion<string?>().HasMaxLength(maxLength);

    /// <summary>A JSON document column (PostgreSQL jsonb), queryable with -> and ->> operators.</summary>
    public static PropertyBuilder<T> AsJson<T>(this PropertyBuilder<T> property) => property.HasColumnType("jsonb");

    /// <summary>Money as numeric(18,6): CheckNumber.ai prices to six decimals ("0.049500").</summary>
    public static PropertyBuilder<decimal?> AsMoney(this PropertyBuilder<decimal?> property) => property.HasPrecision(18, 6);
}
