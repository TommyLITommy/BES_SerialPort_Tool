using System;
using System.Text;

namespace SerialPortTool.Helpers;

public static class HexInputHelper
{
    /// <summary>提取十六进制字符（大写），不含空格</summary>
    public static string ExtractHexDigits(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (IsHexChar(c))
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>输入过程中：每两个十六进制字符之间插入空格，末尾单字符不补 0</summary>
    public static string FormatForInput(string input)
    {
        string hex = ExtractHexDigits(input);
        if (hex.Length == 0) return "";

        var sb = new StringBuilder(hex.Length + hex.Length / 2);
        for (int i = 0; i < hex.Length; i++)
        {
            if (i > 0 && i % 2 == 0)
                sb.Append(' ');
            sb.Append(hex[i]);
        }
        return sb.ToString();
    }

    /// <summary>失焦或保存前：补齐奇数位并在每字节间加空格</summary>
    public static string FormatFinal(string input)
    {
        string hex = ExtractHexDigits(input);
        if (hex.Length == 0) return "";
        if (hex.Length % 2 != 0)
            hex = "0" + hex;

        var sb = new StringBuilder(hex.Length + hex.Length / 2);
        for (int i = 0; i < hex.Length; i += 2)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(hex, i, 2);
        }
        return sb.ToString();
    }

    public static bool IsHexChar(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
