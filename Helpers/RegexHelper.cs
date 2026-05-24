using System;
using System.Text.RegularExpressions;

namespace SerialPortTool.Helpers;

public static class RegexHelper
{
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(200);

    public static bool TryCreate(string pattern, out Regex? regex)
    {
        regex = null;
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant, DefaultMatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsMatch(Regex? regex, string input)
    {
        if (regex == null || string.IsNullOrEmpty(input))
            return false;

        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
