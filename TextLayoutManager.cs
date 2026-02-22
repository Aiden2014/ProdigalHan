using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ProdigalHan;

/// <summary>
/// 处理文本布局和位置调整的管理器
/// 包括对话框、Tooltip 等 UI 中的文本位置计算
/// </summary>
public static class TextLayoutManager
{
    private static ManualLogSource Logger;

    public static void Initialize(ManualLogSource logger)
    {
        Logger = logger;
    }

    // ==================== 对话框名字位置调整 ====================

    /// <summary>
    /// 调整对话框中说话者名字的位置
    /// </summary>
    public static void AdjustNamePositions(CHAT_BOX chatBox, string speakerName)
    {
        if (chatBox.NAME == null || chatBox.NAME.Length == 0) return;

        if (string.IsNullOrEmpty(speakerName))
        {
            Logger?.LogWarning("[AdjustNamePositions] No cached name available");
            return;
        }

        Logger?.LogInfo($"[AdjustNamePositions] Adjusting positions for name: {speakerName}");

        float xOffset = 0f;
        float baseY = chatBox.NAME[0].transform.localPosition.y;
        float baseZ = chatBox.NAME[0].transform.localPosition.z;

        for (int i = 0; i < chatBox.NAME.Length && i < speakerName.Length; i++)
        {
            char c = speakerName[i];

            // 设置当前字符位置
            chatBox.NAME[i].transform.localPosition = new Vector3(LayoutConstants.NameBaseX + xOffset, baseY, baseZ);

            // 计算到下一个字符的间距：当前字符宽度的一半 + 下一个字符宽度的一半
            float currentHalfWidth = (c <= TextConstants.AsciiLimit) ? LayoutConstants.NameHalfwidthSpacing / 2f : LayoutConstants.NameFullwidthSpacing / 2f;
            float nextHalfWidth = LayoutConstants.NameHalfwidthSpacing / 2f; // 默认半角
            if (i + 1 < speakerName.Length)
            {
                char nextChar = speakerName[i + 1];
                nextHalfWidth = (nextChar <= TextConstants.AsciiLimit) ? LayoutConstants.NameHalfwidthSpacing / 2f : LayoutConstants.NameFullwidthSpacing / 2f;
            }
            xOffset += (float)Math.Ceiling(currentHalfWidth + nextHalfWidth);
        }
    }

    // ==================== 逐字显示时的位置调整 ====================

    // 保存每行的原始基准X位置（在Awake时初始化）
    private static float[] _lineBaseX = null;

    // 保存所有TEXT字符槽位的原始X位置（在Awake时初始化）
    private static float[] _originalTextX = null;

    // 用于追踪当前行的X偏移量
    private static float _currentLineXOffset = 0f;
    private static int _lastLineIndex = 0;

    /// <summary>
    /// 初始化行基准X位置数组
    /// </summary>
    public static void InitializeLineBaseX(CHAT_BOX chatBox)
    {
        _lineBaseX = new float[LayoutConstants.MaxLinesBuffer];
        _originalTextX = new float[LayoutConstants.MaxTextSlots];
        int slotIndex = 0;
        int lineIndex = 0;

        foreach (Transform child in chatBox.transform)
        {
            if (child.name.StartsWith("line1"))
            {
                foreach (Transform lineChild in child)
                {
                    // 保存每个字符槽位的原始X位置
                    if (slotIndex < _originalTextX.Length)
                    {
                        _originalTextX[slotIndex] = lineChild.localPosition.x;
                        slotIndex++;
                    }
                }

                // 保存每行第一个字符的原始X位置
                if (child.childCount > 0 && lineIndex < _lineBaseX.Length)
                {
                    _lineBaseX[lineIndex] = child.GetChild(0).localPosition.x;
                    Logger?.LogInfo($"Saved line {lineIndex} base X: {_lineBaseX[lineIndex]}");
                }
                lineIndex++;
            }
        }
        Logger?.LogInfo($"Saved {slotIndex} TEXT slot original X positions");
    }

    /// <summary>
    /// 获取行的基准X位置
    /// </summary>
    public static float GetLineBaseX(int lineIndex, CHAT_BOX chatBox, int lineStart)
    {
        if (_lineBaseX != null && lineIndex >= 0 && lineIndex < _lineBaseX.Length)
        {
            return _lineBaseX[lineIndex];
        }
        // 后备方案：使用行首字符的当前位置
        return chatBox.TEXT[lineStart].transform.localPosition.x;
    }

    /// <summary>
    /// 获取原始的文本槽位X位置
    /// </summary>
    public static float GetOriginalTextX(int slotIndex)
    {
        if (_originalTextX != null && slotIndex >= 0 && slotIndex < _originalTextX.Length)
        {
            return _originalTextX[slotIndex];
        }
        return 0f;
    }

    /// <summary>
    /// 重置行偏移追踪
    /// </summary>
    public static void ResetLineTracking()
    {
        _currentLineXOffset = 0f;
        _lastLineIndex = 0;
    }

    /// <summary>
    /// 检测换行或新对话开始
    /// </summary>
    public static void CheckLineChange(int currentLineIndex)
    {
        if (currentLineIndex != _lastLineIndex)
        {
            _currentLineXOffset = 0f;
            _lastLineIndex = currentLineIndex;
        }
    }

    /// <summary>
    /// 计算字符的半角宽度（用于间距计算）
    /// </summary>
    public static float GetCharHalfWidth(char c, CHAT_BOX.TEXT_LANGUAGE language)
    {
        if (language == CHAT_BOX.TEXT_LANGUAGE.ANCIENT)
        {
            return LayoutConstants.AncientwidthSpacing / 2f;
        }
        return (c <= TextConstants.AsciiLimit) ? LayoutConstants.HalfwidthSpacing / 2f : LayoutConstants.FullwidthSpacing / 2f;
    }

    /// <summary>
    /// 获取当前行的X偏移量
    /// </summary>
    public static float GetCurrentLineXOffset() => _currentLineXOffset;

    /// <summary>
    /// 更新行的X偏移量
    /// </summary>
    public static void UpdateLineXOffset(float currentHalfWidth, float nextHalfWidth)
    {
        _currentLineXOffset += (float)Math.Ceiling(currentHalfWidth + nextHalfWidth);
    }

    /// <summary>
    /// 获取原始TEXT的X位置数组（用于重置）
    /// </summary>
    public static float[] GetOriginalTextXArray() => _originalTextX;

    // ==================== 即时模式下的位置调整 ====================

    /// <summary>
    /// 通过扫描 TEXT 数组中已有 sprite 的槽位来调整位置。
    /// 用于即时模式（TXT=6）场景，在所有字符已被放置后统一调整。
    /// </summary>
    public static void AdjustAllTextPositionsByTextSlots(CHAT_BOX chatBox, int length, int lines)
    {
        if (chatBox.TEXT == null) return;

        Logger?.LogInfo($"[AdjustAllTextPositionsByTextSlots] Scanning TEXT slots, LENGTH={length}, LINES={lines}");

        for (int lineIdx = 0; lineIdx < lines; lineIdx++)
        {
            int lineStart = lineIdx * length;
            float lineXOffset = 0f;
            float baseX = GetLineBaseX(lineIdx, chatBox, lineStart);

            for (int slotInLine = 0; slotInLine < length; slotInLine++)
            {
                int slotIdx = lineStart + slotInLine;
                if (slotIdx >= chatBox.TEXT.Length) break;
                if (chatBox.TEXT[slotIdx].sprite == null) break; // 本行没有更多字符了

                float baseY = chatBox.TEXT[slotIdx].transform.localPosition.y;
                float baseZ = chatBox.TEXT[slotIdx].transform.localPosition.z;
                chatBox.TEXT[slotIdx].transform.localPosition = new Vector3(baseX + lineXOffset, baseY, baseZ);

                // 判断当前字符是全角还是半角
                // 通过 sprite name 来判断（自定义字体 sprite 名为 "CustomFont_X"）
                string spriteName = chatBox.TEXT[slotIdx].sprite.name;
                bool isFullWidth = IsFullWidthCharacterSprite(spriteName);

                float currentHalfWidth = isFullWidth ? LayoutConstants.FullwidthSpacing / 2f : LayoutConstants.HalfwidthSpacing / 2f;
                if (chatBox.LANGUAGE == CHAT_BOX.TEXT_LANGUAGE.ANCIENT)
                {
                    currentHalfWidth = LayoutConstants.AncientwidthSpacing / 2f;
                }
                float nextHalfWidth = LayoutConstants.HalfwidthSpacing / 2f; // 默认

                // 看下一个槽位的字符来决定间距
                int nextSlotIdx = slotIdx + 1;
                if (nextSlotIdx < chatBox.TEXT.Length && nextSlotIdx < lineStart + length && chatBox.TEXT[nextSlotIdx].sprite != null)
                {
                    string nextSpriteName = chatBox.TEXT[nextSlotIdx].sprite.name;
                    if (IsFullWidthCharacterSprite(nextSpriteName))
                    {
                        char nextSpriteChar = nextSpriteName[11];
                        nextHalfWidth = nextSpriteChar > TextConstants.AsciiLimit ? LayoutConstants.FullwidthSpacing / 2f : LayoutConstants.HalfwidthSpacing / 2f;
                    }
                }

                lineXOffset += (float)Math.Ceiling(currentHalfWidth + nextHalfWidth);
            }
        }

        Logger?.LogInfo($"[AdjustAllTextPositionsByTextSlots] Done.");
    }

    private static bool IsFullWidthCharacterSprite(string spriteName)
    {
        return spriteName != null
            && spriteName.StartsWith("CustomFont_")
            && spriteName.Length > 11
            && spriteName[11] > TextConstants.AsciiLimit;
    }

    // ==================== Tooltip 位置调整 ====================

    // 保存每行的原始Y坐标（只初始化一次）
    private static float[] _tooltipLineOriginalY = null;
    private static float _tooltipNameBaseX = 0f;
    private static float _tooltipNameBaseY = 0f;
    private static float _tooltipAboutBaseX = 0f;

    /// <summary>
    /// 初始化 Tooltip 的基准位置（只在第一次时初始化）
    /// </summary>
    public static void InitializeTooltipPositions(TooltipWindow tooltip)
    {
        // 只在第一次时初始化名字基准位置
        if (tooltip.Name != null && tooltip.Name.Length > 0 && _tooltipNameBaseX == 0f)
        {
            _tooltipNameBaseX = tooltip.Name[0].transform.localPosition.x + LayoutConstants.TooltipNameXOffset;
            _tooltipNameBaseY = tooltip.Name[0].transform.localPosition.y + LayoutConstants.TooltipNameYOffset;
        }

        // 只在第一次时初始化关于信息的基准位置和行Y坐标
        if (tooltip.About != null && tooltip.About.Length > 0 && _tooltipAboutBaseX == 0f)
        {
            _tooltipAboutBaseX = tooltip.About[0].transform.localPosition.x + LayoutConstants.TooltipAboutXOffset;

            int totalLines = tooltip.About.Length / LayoutConstants.TooltipLineSlots;
            _tooltipLineOriginalY = new float[totalLines];
            for (int line = 0; line < totalLines; line++)
            {
                _tooltipLineOriginalY[line] = tooltip.About[line * LayoutConstants.TooltipLineSlots].transform.localPosition.y;
            }
        }
    }

    /// <summary>
    /// 调整 Tooltip 名字的位置
    /// </summary>
    public static void AdjustTooltipNamePositions(TooltipWindow tooltip, string header)
    {
        if (tooltip.Name == null || tooltip.Name.Length == 0) return;

        float xOffset = 0f;
        float baseZ = tooltip.Name[0].transform.localPosition.z;

        for (int i = 0; i < tooltip.Name.Length && i < header.Length; i++)
        {
            char c = header[i];

            // 设置当前字符位置
            tooltip.Name[i].transform.localPosition = new Vector3(_tooltipNameBaseX + xOffset, _tooltipNameBaseY, baseZ);

            // 计算到下一个字符的间距
            float currentHalfWidth = (c <= TextConstants.AsciiLimit) ? LayoutConstants.TooltipHalfwidthSpacing / 2f : LayoutConstants.TooltipFullwidthSpacing / 2f;
            float nextHalfWidth = LayoutConstants.TooltipHalfwidthSpacing / 2f;
            if (i + 1 < header.Length)
            {
                char nextChar = header[i + 1];
                nextHalfWidth = (nextChar <= TextConstants.AsciiLimit) ? LayoutConstants.TooltipHalfwidthSpacing / 2f : LayoutConstants.TooltipFullwidthSpacing / 2f;
            }
            xOffset += (float)Math.Ceiling(currentHalfWidth + nextHalfWidth);
        }
    }

    /// <summary>
    /// 调整 Tooltip 描述的位置
    /// </summary>
    public static void AdjustTooltipAboutPositions(TooltipWindow tooltip, string about)
    {
        if (tooltip.About == null || tooltip.About.Length == 0) return;

        int currentLine = 0;
        int slotInLine = 0;
        float xOffset = 0f;
        float baseZ = tooltip.About[0].transform.localPosition.z;

        for (int textIndex = 0; textIndex < about.Length; textIndex++)
        {
            char c = about[textIndex];

            if (TextProcessing.IsIgnoredCharacter(about, textIndex))
            {
                continue;
            }

            // 检测换行符
            if (c == LayoutConstants.LineBreakChar)
            {
                currentLine++;
                slotInLine = 0;
                xOffset = 0f;
                continue;
            }

            // 计算实际的 sprite 索引
            int spriteIndex = currentLine * LayoutConstants.TooltipLineSlots + slotInLine;
            if (spriteIndex >= tooltip.About.Length)
            {
                break;
            }

            // 使用保存的原始Y坐标，每行额外向下偏移
            float lineBaseY = (_tooltipLineOriginalY != null && currentLine < _tooltipLineOriginalY.Length)
                ? _tooltipLineOriginalY[currentLine]
                : _tooltipLineOriginalY[0];
            lineBaseY -= (currentLine + 1) * LayoutConstants.TooltipAboutLineYOffset;

            // 设置当前字符位置
            tooltip.About[spriteIndex].transform.localPosition = new Vector3(_tooltipAboutBaseX + xOffset, lineBaseY, baseZ);

            // 计算到下一个字符的间距
            float currentHalfWidth = (c <= TextConstants.AsciiLimit) ? LayoutConstants.TooltipHalfwidthSpacing / 2f : LayoutConstants.TooltipFullwidthSpacing / 2f;
            float nextHalfWidth = LayoutConstants.TooltipHalfwidthSpacing / 2f;

            // 找下一个非换行符字符来计算间距
            for (int j = textIndex + 1; j < about.Length; j++)
            {
                char nextChar = about[j];
                if (TextProcessing.IsIgnoredCharacter(about, j))
                {
                    continue;
                }
                nextHalfWidth = (nextChar <= TextConstants.AsciiLimit) ? LayoutConstants.TooltipHalfwidthSpacing / 2f : LayoutConstants.TooltipFullwidthSpacing / 2f;
                break;
            }
            xOffset += (float)Math.Ceiling(currentHalfWidth + nextHalfWidth);
            Logger?.LogInfo($"Set ABOUT char '{c}' at index {textIndex} to position ({_tooltipAboutBaseX + xOffset}, {lineBaseY}) on sprite index {spriteIndex}");
            slotInLine++;
        }
    }
}
