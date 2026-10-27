namespace AiClientHunter.Api.Services;

public static class Normalizer
{
    // Normalizes phone numbers to standard format (+91XXXXXXXXXX for Indian, +... for international)
    public static string? Phone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Take the first number if multiple numbers are separated by comma, slash, or semicolon
        var first = raw.Split(';', ',', '/')[0].Trim();
        var hasPlus = first.StartsWith('+');
        var d = new string(first.Where(char.IsDigit).ToArray());

        if (d.Length == 12 && d.StartsWith("91"))
            d = d[2..];
        else if (d.Length == 11 && d.StartsWith("0"))
            d = d[1..];

        if (d.Length == 10)
            return "+91" + d;

        // Support international phone numbers when explicitly formatted with '+' (e.g. +1 555 123 4567, +44...)
        if (hasPlus && d.Length >= 8 && d.Length <= 15)
            return "+" + d;

        return null;
    }

    // Timezone conversion for India Standard Time (UTC+05:30)
    private static readonly TimeZoneInfo IstZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    public static DateTime ToIndianTime(DateTime utc)
    {
        var specified = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(specified, IstZone);
    }

    // Formats phone numbers with readable spaces so spreadsheet programs (like Microsoft Excel) do NOT convert them to scientific notation (e.g. 9.176E+11)
    public static string? FormatPhoneForDisplay(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var cleaned = phone.Trim();
        // Indian mobile phone e.g. "+917600066011" -> "+91 76000 66011"
        if (cleaned.StartsWith("+91") && cleaned.Length == 13)
        {
            return $"+91 {cleaned.Substring(3, 5)} {cleaned.Substring(8, 5)}";
        }
        // Indian mobile phone e.g. "917600066011" -> "+91 76000 66011"
        if (cleaned.StartsWith("91") && cleaned.Length == 12)
        {
            return $"+91 {cleaned.Substring(2, 5)} {cleaned.Substring(7, 5)}";
        }
        // Indian mobile phone e.g. "7600066011" -> "+91 76000 66011"
        if (cleaned.Length == 10 && char.IsDigit(cleaned[0]))
        {
            return $"+91 {cleaned.Substring(0, 5)} {cleaned.Substring(5, 5)}";
        }
        // Indian landline e.g. "07940041994" -> "079 4004 1994"
        if (cleaned.StartsWith("0") && cleaned.Length == 11)
        {
            return $"{cleaned.Substring(0, 3)} {cleaned.Substring(3, 4)} {cleaned.Substring(7, 4)}";
        }
        // International e.g. "+15551234567" -> "+1 5551234567"
        if (cleaned.StartsWith("+") && !cleaned.Contains(' '))
        {
            return cleaned.Length > 4 ? $"{cleaned.Substring(0, 3)} {cleaned.Substring(3)}" : cleaned;
        }

        return cleaned;
    }

    // Extracts email from unstructured text snippet
    public static string? ExtractEmailFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        return match.Success ? Email(match.Value) : null;
    }

    // Extracts phone number from unstructured text snippet
    public static string? ExtractPhoneFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(?:\+|00)[1-9]\d{0,2}[ -]?(?:\(?\d{1,4}\)?[ -]?)?\d{3,4}[ -]?\d{3,4}|\b[6-9]\d{9}\b");
        return match.Success ? Phone(match.Value) : null;
    }

    // Extracts website/domain from unstructured text snippet
    public static string? ExtractUrlFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"https?://[^\s""'>]+|(?:www\.)[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}[^\s""'>]*");
        return match.Success ? match.Value.TrimEnd('.', ',', ')') : null;
    }

    // Strips scheme, www prefix, and path to extract root canonical domain
    public static string? Domain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!url.Contains("://")) url = "http://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;

        var h = u.Host.ToLowerInvariant();
        return h.StartsWith("www.") ? h[4..] : h;
    }

    // Keeps letters, marks/matras, and digits in any Unicode language (Gujarati, Hindi, English) for reliable dedupe keys
    public static string NameKey(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{M}\p{N}]+", " ").Trim();

    // Normalizes and validates email addresses
    public static string? Email(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var first = raw.Split(';', ',', '/')[0].Trim();
        if (first.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            first = first[7..].Trim();

        first = first.ToLowerInvariant();

        var parts = first.Split('@');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            return null;

        if (!parts[1].Contains('.') || parts[0].Contains(' ') || parts[1].Contains(' '))
            return null;

        return first;
    }
}
