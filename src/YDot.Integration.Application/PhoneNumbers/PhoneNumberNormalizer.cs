using PhoneNumbers;
using YDot.Integration.Application.Abstractions.Persistence;

namespace YDot.Integration.Application.PhoneNumbers;

/// <summary>The outcome of normalising one digit run.</summary>
public readonly record struct NormaliseResult(bool Ok, string? E164, string? Reason)
{
    public static NormaliseResult Valid(string e164) => new(true, e164, null);

    public static NormaliseResult Invalid(string reason) => new(false, null, reason);
}

/// <summary>
/// Turns what people type into E.164, with EXACTLY the rules the upload page uses
/// (number-tools.ts, normaliseNumber), so the review screen and the worker agree:
///
///   9876543210         -> +919876543210   a bare 10-digit number is Indian
///   098765 43210       -> +919876543210   leading 0 dropped
///   919876543210       -> +919876543210   12 digits starting 91
///   0044 7700 900123   -> +447700900123   "00" is the international prefix
///   +44 7700 900123    -> +447700900123   explicit country code kept
///   +91 5876543210     -> refused         Indian mobiles start with 6, 7, 8 or 9
///
/// Separators (spaces, dots, dashes, brackets) are removed; the ORIGINAL text is never changed -
/// it is stored beside the result.
/// </summary>
public static class PhoneNumberNormalizer
{
    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

    /// <summary>Keeps ASCII digits only (JavaScript's value.replace(/\D/g, '')).</summary>
    public static string DigitsOnly(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : string.Concat(value.Where(char.IsAsciiDigit));

    /// <summary>One digit run (with or without a leading "+") to "+{country}{number}", or the reason it cannot be.</summary>
    public static NormaliseResult Normalise(string digitsRaw, bool hadPlus)
    {
        var digits = DigitsOnly(digitsRaw);
        var plus = hadPlus;

        // "00" is the international call prefix, the same thing as "+".
        if (!plus && digits.StartsWith("00", StringComparison.Ordinal) && digits.Length > 11)
        {
            digits = digits[2..];
            plus = true;
        }

        if (digits.Length < 8)
        {
            return NormaliseResult.Invalid($"Too short ({digits.Length} digits)");
        }

        if (digits.Length > 15)
        {
            return NormaliseResult.Invalid($"Too long ({digits.Length} digits)");
        }

        if (plus)
        {
            return digits.StartsWith("91", StringComparison.Ordinal)
                ? IndianMobile(digits[2..])
                : NormaliseResult.Valid("+" + digits);
        }

        // No "+" given. A bare 10-digit number, or one with a 0 / 91 in front, is read as Indian.
        if (digits.Length == 10)
        {
            return IndianMobile(digits);
        }

        if (digits.Length == 11 && digits.StartsWith('0'))
        {
            return IndianMobile(digits[1..]);
        }

        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
        {
            return IndianMobile(digits[2..]);
        }

        if (digits.Length < 10)
        {
            return NormaliseResult.Invalid($"Too short ({digits.Length} digits) and no country code");
        }

        // 11-15 digits with no "+": most likely carries its own country code, so it is kept with a "+".
        return NormaliseResult.Valid("+" + digits);
    }

    private static NormaliseResult IndianMobile(string national)
    {
        if (national.Length != 10)
        {
            return NormaliseResult.Invalid($"Indian number needs 10 digits after +91 (found {national.Length})");
        }

        if (national[0] is < '6' or > '9')
        {
            return NormaliseResult.Invalid("Indian mobile numbers start with 6, 7, 8 or 9");
        }

        return NormaliseResult.Valid("+91" + national);
    }

    /// <summary>
    /// Everything the registry stores about a normalised number: digits, calling code, the
    /// libphonenumber region (more precise than the dial-code table, e.g. US vs CA) and the
    /// display country the screens use.
    /// </summary>
    public static NumberInfo Describe(string e164)
    {
        var digits = DigitsOnly(e164);
        var country = CountryDirectory.Of(e164);
        string? callingCode = country.Code.Length > 0 ? country.Code : null;
        string? region = null;

        try
        {
            var parsed = PhoneUtil.Parse(e164, null);
            callingCode = parsed.CountryCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var regionCode = PhoneUtil.GetRegionCodeForNumber(parsed)
                             ?? PhoneUtil.GetRegionCodeForCountryCode(parsed.CountryCode);
            region = regionCode is null or "ZZ" ? null : regionCode;
        }
        catch (NumberParseException)
        {
            // Not parseable as E.164 (an unassigned country code): keep the dial-code table's view.
        }

        return new NumberInfo(e164, digits, callingCode ?? string.Empty, region, country.Iso, country.Name);
    }
}
