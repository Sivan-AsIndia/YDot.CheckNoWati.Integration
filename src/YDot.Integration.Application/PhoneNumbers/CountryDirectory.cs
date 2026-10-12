namespace YDot.Integration.Application.PhoneNumbers;

/// <summary>A country as the WhatsApp screens name it.</summary>
/// <param name="Iso">Two-letter code shown in the grids ("IN"); "--" when unknown.</param>
/// <param name="Name">Display name ("India", "United States / Canada", "International").</param>
/// <param name="Code">Calling code without "+", from the dial-code table ("" when unknown).</param>
public sealed record CountryInfo(string Iso, string Name, string Code);

/// <summary>
/// The dial-code table of the upload page (UI/YDots.UI/.../WhatsApp/number-tools.ts, DIAL_CODES),
/// copied entry for entry so a number shows the same country before upload (in the browser) and
/// after (from the database). Longest prefix wins; anything else is "International".
/// </summary>
public static class CountryDirectory
{
    public static readonly CountryInfo Unknown = new("--", "International", string.Empty);

    private static readonly CountryInfo[] DialCodes =
    [
        new("US", "United States / Canada", "1"),
        new("RU", "Russia / Kazakhstan", "7"),
        new("EG", "Egypt", "20"),
        new("ZA", "South Africa", "27"),
        new("GR", "Greece", "30"),
        new("NL", "Netherlands", "31"),
        new("BE", "Belgium", "32"),
        new("FR", "France", "33"),
        new("ES", "Spain", "34"),
        new("IT", "Italy", "39"),
        new("CH", "Switzerland", "41"),
        new("GB", "United Kingdom", "44"),
        new("SE", "Sweden", "46"),
        new("NO", "Norway", "47"),
        new("DE", "Germany", "49"),
        new("MX", "Mexico", "52"),
        new("BR", "Brazil", "55"),
        new("MY", "Malaysia", "60"),
        new("AU", "Australia", "61"),
        new("ID", "Indonesia", "62"),
        new("PH", "Philippines", "63"),
        new("NZ", "New Zealand", "64"),
        new("SG", "Singapore", "65"),
        new("TH", "Thailand", "66"),
        new("JP", "Japan", "81"),
        new("KR", "South Korea", "82"),
        new("VN", "Vietnam", "84"),
        new("CN", "China", "86"),
        new("TR", "Türkiye", "90"),
        new("IN", "India", "91"),
        new("PK", "Pakistan", "92"),
        new("AF", "Afghanistan", "93"),
        new("LK", "Sri Lanka", "94"),
        new("MM", "Myanmar", "95"),
        new("IR", "Iran", "98"),
        new("MA", "Morocco", "212"),
        new("NG", "Nigeria", "234"),
        new("KE", "Kenya", "254"),
        new("TZ", "Tanzania", "255"),
        new("UG", "Uganda", "256"),
        new("PT", "Portugal", "351"),
        new("IE", "Ireland", "353"),
        new("BD", "Bangladesh", "880"),
        new("MV", "Maldives", "960"),
        new("SA", "Saudi Arabia", "966"),
        new("AE", "United Arab Emirates", "971"),
        new("IL", "Israel", "972"),
        new("BH", "Bahrain", "973"),
        new("QA", "Qatar", "974"),
        new("KW", "Kuwait", "965"),
        new("OM", "Oman", "968"),
        new("NP", "Nepal", "977"),
    ];

    private static readonly Dictionary<string, CountryInfo> ByCode =
        DialCodes.ToDictionary(country => country.Code, StringComparer.Ordinal);

    /// <summary>The country of an E.164 number ("+919876543210" -> India).</summary>
    public static CountryInfo Of(string e164)
    {
        var digits = PhoneNumberNormalizer.DigitsOnly(e164);

        foreach (var length in (int[])[3, 2, 1])
        {
            if (digits.Length >= length && ByCode.TryGetValue(digits[..length], out var country))
            {
                return country;
            }
        }

        return Unknown;
    }
}
