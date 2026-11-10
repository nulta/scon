using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Scon;

public static class Extensions
{
    public static string ToBase64(this string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Cuts the string to at most <paramref name="maxLength"/> UTF-16 chars,
    /// without splitting a surrogate pair (e.g. emoji) in half.
    /// </summary>
    public static string Cut(this string input, int maxLength)
    {
        if (input.IsEmpty() || maxLength <= 0)
        {
            return string.Empty;
        }

        if (input.Length <= maxLength)
        {
            return input;
        }

        var end = maxLength;
        if (char.IsHighSurrogate(input[end - 1]))
        {
            end--;
        }

        return input[..end];
    }

    /// <summary>
    /// Makes a string safe to embed inside a quoted Source engine console argument.
    /// Double quotes become single quotes; semicolons and every control / line-separator
    /// character are removed (tabs become spaces).
    /// </summary>
    public static string ConsoleSafe(this string input)
    {
        if (input.IsEmpty())
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            switch (c)
            {
                case '"':
                    sb.Append('\'');
                    break;
                case ';':
                    break;
                case '\t':
                    sb.Append(' ');
                    break;
                default:
                    var category = char.GetUnicodeCategory(c);
                    // Control: \0, \n, \r, \x85, ... / LineSeparator: U+2028 / ParagraphSeparator: U+2029
                    if (category is UnicodeCategory.Control
                        or UnicodeCategory.LineSeparator
                        or UnicodeCategory.ParagraphSeparator)
                    {
                        break;
                    }
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    public static bool IsEmpty([NotNullWhen(false)] this string? input)
    {
        return string.IsNullOrEmpty(input);
    }

    public static string OrDefault(this string? input, string defaultValue)
    {
        return input.IsEmpty() ? defaultValue : input;
    }

    public static T GetOr<T>(this T[]? array, int index, T defaultValue)
    {
        if (array is null || index < 0 || index >= array.Length)
        {
            return defaultValue;
        }

        return array[index];
    }
}
