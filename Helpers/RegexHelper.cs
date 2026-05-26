using System;
using System.Collections.Generic;
using System.Text;
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

    /// <summary>
    /// 按未转义的竖线拆分为多个子正则（用于多色高亮，与过滤用的 OR 写法一致）。
    /// </summary>
    public static IReadOnlyList<string> SplitAlternationPatterns(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return Array.Empty<string>();

        var parts = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                current.Append(c);
                current.Append(pattern[++i]);
                continue;
            }

            if (c == '|')
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        parts.Add(current.ToString());
        return parts;
    }
}
