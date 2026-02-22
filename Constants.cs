using UnityEngine;

namespace ProdigalHan;

/// <summary>
/// 全局常量定义
/// </summary>
public static class LayoutConstants
{
    // 名字位置相关常量
    public static readonly float NameBaseX = -49f;
    public const float NameFullwidthSpacing = 10f;
    public const float NameHalfwidthSpacing = 5f;

    // 对话框文本位置相关常量
    public const float FullwidthSpacing = 10f;  // 全角字符间距
    public const float HalfwidthSpacing = 5f;   // 半角字符间距
    public const float AncientwidthSpacing = 6f; // 古文字间距

    // 对话框背景调整常量
    public static readonly Vector3 NameOffset = new Vector3(-49f, -2f, 0f);
    public const float ChatBGHeightIncrease = 2f;
    public const float ChatBGYOffset = 1f;
    public const float NamePlateYOffset = 2f;
    public const float LineYOffset = 3f;

    // Tooltip 相关常量
    public const float TooltipFullwidthSpacing = 10f;
    public const float TooltipHalfwidthSpacing = 5f;
    public const int TooltipLineSlots = 24; // 每行24个槽位
    public const float TooltipNameXOffset = 4f;
    public const float TooltipNameYOffset = -1f;
    public const float TooltipAboutXOffset = 3f;
    public const float TooltipAboutLineYOffset = 3f;

    // 自定义字体相关常量
    public const int CharsPerRow = 100;
    public const int CharSize = 9;
    public const int FontGap = 1;
    public const int FontMargin = 1;
    public const int HalfWidth = 4;
    public const float FontPPU = 1.0f;
    public const float FontPivot = 0.5f;

    // 文本处理常量
    public const int MaxLinesBuffer = 4;
    public const int MaxTextSlots = 116; // 29 * 4
    public const char LineBreakChar = '*';
    public const char AncientTextIndicator = ']';
}

/// <summary>
/// 文本相关常量
/// </summary>
public static class TextConstants
{
    public const int AsciiLimit = 127; // ASCII 字符上限，> 127 为全角字符
    public const int MaxTooltipCharsPerLine = 130; // Tooltip 每行最大字符数
    public const int MaxChatCharsPerLine = 170; // 对话框每行最大字符数
}

/// <summary>
/// UI 相关常量
/// </summary>
public static class UIConstants
{
    public const float FixedSpriteRectPPU = 1.0f;
    public const float FixedSpritePivot = 0.5f;
}
