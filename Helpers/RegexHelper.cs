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
    /// 不拆分括号 ()、方括号 [] 内部的 |。
    /// </summary>
    public static IReadOnlyList<string> SplitAlternationPatterns(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return Array.Empty<string>();

        var parts = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        bool inBracket = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                current.Append(c);
                current.Append(pattern[++i]);
                continue;
            }

            if (c == '[' && !inBracket)
            {
                inBracket = true;
                current.Append(c);
                continue;
            }

            if (c == ']' && inBracket)
            {
                inBracket = false;
                current.Append(c);
                continue;
            }

            if (!inBracket)
            {
                if (c == '(')
                {
                    depth++;
                    current.Append(c);
                    continue;
                }

                if (c == ')')
                {
                    if (depth > 0)
                        depth--;
                    current.Append(c);
                    continue;
                }

                if (c == '|' && depth == 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }
            }

            current.Append(c);
        }

        parts.Add(current.ToString());
        return parts;
    }

    public static IEnumerable<Match> EnumerateMatches(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(text))
            return Array.Empty<Match>();

        if (!TryCreate(pattern, out var regex) || regex is null)
            return Array.Empty<Match>();

        var matches = new List<Match>();

        try
        {
            for (Match m = regex.Match(text); m.Success; m = m.NextMatch())
            {
                if (m.Length > 0)
                    matches.Add(m);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return matches;
        }
        catch (ArgumentException)
        {
            return matches;
        }

        return matches;
    }
}
