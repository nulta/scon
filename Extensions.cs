using System;
using System.Diagnostics.CodeAnalysis;

namespace Scon;

public static class Extensions
{
    public static string ToBase64(this string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes);
    }

    public static string Cut(this string input, int maxLength)
    {
        if (input.IsEmpty() || maxLength <= 0)
        {
            return string.Empty;
        }

        return (input.Length <= maxLength) ? input : input[..maxLength];
    }

    public static string ConsoleSafe(this string input)
    {
        if (input.IsEmpty())
        {
            return string.Empty;
        }

        return input
            .Replace("\"", "'")
            .Replace(";", "")
            .Replace("\n", "")
            .Replace("\r", "");
    }

    public static bool IsEmpty([NotNullWhen(false)] this string? input)
    {
        return string.IsNullOrEmpty(input);
    }

    public static string OrDefault(this string? input, string defaultValue)
    {
        return input.IsEmpty() ? defaultValue : input;
    }

    public static T GetOr<T>(this T[] array, int index, T defaultValue)
    {
        if (array is null || index < 0 || index >= array.Length)
        {
            return defaultValue;
        }

        return array[index];
    }
}