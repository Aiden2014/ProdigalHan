using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ProdigalHan;

/// <summary>
/// 字体和精灵管理器
/// 负责管理自定义字体的加载、缓存和 Sprite 生成
/// </summary>
public static class FontManager
{
    private static ManualLogSource Logger;
    private static Texture2D _customTexture;
    private static Dictionary<char, Sprite> _spriteCache = new Dictionary<char, Sprite>();
    private static string _charMapping = null;

    public static void Initialize(ManualLogSource logger, Texture2D customTexture)
    {
        Logger = logger;
        _customTexture = customTexture;
    }

    /// <summary>
    /// 获取或创建一个字符的自定义 Sprite
    /// </summary>
    public static bool TryGetCharacterSprite(char letter, CHAT_BOX.TEXT_LANGUAGE language, out Sprite result)
    {
        result = null;

        // 非自定义字符，使用原始字体
        if (language == CHAT_BOX.TEXT_LANGUAGE.ANCIENT || !IsCustomCharacter(letter))
        {
            return false;
        }

        try
        {
            // 尝试从缓存获取
            if (_spriteCache.TryGetValue(letter, out Sprite cachedSprite))
            {
                result = cachedSprite;
                return true;
            }

            // 从字体图集中获取字符索引
            int index = GetCharIndex(letter);
            if (index < 0)
            {
                Logger?.LogWarning($"[FontManager] Character '{letter}' (U+{(int)letter:X4}) not found in font atlas, falling back to original");
                return false;
            }

            // 根据索引计算 Sprite 的矩形区域
            Sprite newSprite = CreateSpriteFromAtlas(letter, index);
            _spriteCache[letter] = newSprite;
            result = newSprite;
            return true;
        }
        catch (System.Exception ex)
        {
            Logger?.LogError($"[FontManager] Exception for character '{letter}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从图集创建一个字符的 Sprite
    /// </summary>
    private static Sprite CreateSpriteFromAtlas(char letter, int index)
    {
        int col = index % LayoutConstants.CharsPerRow;
        int row = index / LayoutConstants.CharsPerRow;

        float x = LayoutConstants.FontMargin + col * (LayoutConstants.CharSize + LayoutConstants.FontGap);
        float y = LayoutConstants.FontMargin + row * (LayoutConstants.CharSize + LayoutConstants.FontGap);
        float unityY = _customTexture.height - y - LayoutConstants.CharSize;

        // 边界检查
        if (x < 0 || unityY < 0 || x + LayoutConstants.CharSize > _customTexture.width || unityY + LayoutConstants.CharSize > _customTexture.height)
        {
            Logger?.LogWarning($"[FontManager] Character '{letter}' coordinates out of bounds: x={x}, unityY={unityY}");
            return null;
        }

        Rect rect;
        Vector2 pivot = new Vector2(LayoutConstants.FontPivot, LayoutConstants.FontPivot);

        // ASCII 字符是半角，只取左半部分宽度，调整 pivot 让它们紧凑显示
        if (letter <= TextConstants.AsciiLimit)
        {
            rect = new Rect(x + 2, unityY, LayoutConstants.HalfWidth, LayoutConstants.CharSize);
        }
        else
        {
            // 全角字符使用完整的 9x9
            rect = new Rect(x, unityY, LayoutConstants.CharSize, LayoutConstants.CharSize);
        }

        Sprite newSprite = Sprite.Create(_customTexture, rect, pivot, LayoutConstants.FontPPU);
        newSprite.name = "CustomFont_" + letter;
        return newSprite;
    }

    /// <summary>
    /// 检查字符是否在自定义字体图集中
    /// </summary>
    public static bool IsCustomCharacter(char c)
    {
        if (_charMapping == null)
        {
            _charMapping = ResourceLoader.GetUniqueChineseChars();
        }
        return _charMapping.IndexOf(c) >= 0;
    }

    /// <summary>
    /// 根据字符返回它在字体图集中的序号
    /// </summary>
    public static int GetCharIndex(char c)
    {
        if (_charMapping == null)
        {
            _charMapping = ResourceLoader.GetUniqueChineseChars();
        }
        return _charMapping.IndexOf(c);
    }

    /// <summary>
    /// 清空 Sprite 缓存
    /// </summary>
    public static void ClearSpriteCache()
    {
        _spriteCache.Clear();
    }

    /// <summary>
    /// 获取缓存的 Sprite 数量（用于调试）
    /// </summary>
    public static int GetCachedSpriteCount()
    {
        return _spriteCache.Count;
    }

    /// <summary>
    /// 获取自定义纹理
    /// </summary>
    public static Texture2D GetCustomTexture()
    {
        return _customTexture;
    }
}
