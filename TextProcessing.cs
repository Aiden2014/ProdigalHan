using System;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ProdigalHan;

/// <summary>
/// 文本处理工具类
/// 包括换行、颜色标记处理等功能
/// </summary>
public static class TextProcessing
{
    private static ManualLogSource Logger;

    public static void Initialize(ManualLogSource logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// 检查字符是否是应被忽略的特殊标记（颜色标记等）
    /// </summary>
    public static bool IsIgnoredCharacter(string text, int index)
    {
        char c = text[index];
        if (c == '@')
        {
            return true;
        }
        if ((c == 'B' || c == 'C') && index >= 1 && text[index - 1] == '@')
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 检查字符是否是应被忽略的特殊标记（颜色标记等）- List&lt;char&gt; 版本
    /// </summary>
    public static bool IsIgnoredCharacter(System.Collections.Generic.List<char> chars, int index)
    {
        char c = chars[index];
        if (c == '@')
        {
            return true;
        }
        if ((c == 'B' || c == 'C') && index >= 1 && chars[index - 1] == '@')
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 根据是否为古文字来决定是否插入换行
    /// </summary>
    public static string InsertManualBreaks(string text, bool isAncient = false)
    {
        if (isAncient)
        {
            return text;
        }
        return InsertManualBreaksWithLimitLength(text, TextConstants.MaxChatCharsPerLine);
    }

    /// <summary>
    /// 为 Tooltip 插入换行
    /// </summary>
    public static string InsertTooltipBreaks(string text)
    {
        return InsertManualBreaksWithLimitLength(text, TextConstants.MaxTooltipCharsPerLine);
    }

    /// <summary>
    /// 根据最大字符数插入换行符
    /// 用于动态调整文本布局
    /// </summary>
    public static string InsertManualBreaksWithLimitLength(string text, int maxCharsPerLineLength)
    {
        StringBuilder sb = new StringBuilder();
        float currentX = 0;
        int newLineIndex = -1;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '\n' || c == '\r' || c == LayoutConstants.LineBreakChar)
            {
                currentX = 0;
                sb.Append(c);
                continue;
            }
            if (IsIgnoredCharacter(text, i))
            {
                // 颜色标记不占用字符位置，但需要被保留
                sb.Append(c);
                continue;
            }

            // 检查是否遇到了断行点（空格、标点等）
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9'))
            {
                newLineIndex = i;
            }

            if (currentX > maxCharsPerLineLength)
            {
                currentX = 0;
                if (newLineIndex != -1)
                {
                    int charsToRemove = i - newLineIndex;
                    sb.Length -= charsToRemove;
                    sb.Append(LayoutConstants.LineBreakChar);
                    i = newLineIndex - 1;
                    newLineIndex = -1;
                    continue;
                }
                sb.Append(LayoutConstants.LineBreakChar);
                newLineIndex = -1;
            }
            sb.Append(c);
            if (c <= TextConstants.AsciiLimit)
            {
                currentX += LayoutConstants.HalfwidthSpacing;
            }
            else
            {
                currentX += LayoutConstants.FullwidthSpacing;
            }
        }

        return sb.ToString();
    }
}
