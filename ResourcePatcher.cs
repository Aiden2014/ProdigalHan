using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 处理游戏资源的替换（字体、贴图等）
/// </summary>
public static class ResourcePatcher
{
    private const string FONT_PATH = "LoneSurvivor/fonts";
    private const string GRAPHICS_PATH = "LoneSurvivor/graphics";

    private static readonly Dictionary<string, Texture2D> customTextureCache = InitializeTextureCache();

    private static Dictionary<string, Texture2D> InitializeTextureCache()
    {
        var graphicsTextures = new[] {
            "UI"
        }
        .ToDictionary(
            name => $"{name}",
            name => ResourceLoader.GetCustomTexture($"{name}.png")
        );

        var fontTextures = new Dictionary<string, Texture2D>
        {
            // [$"{FONT_PATH}/font"] = ResourceLoader.GetCustomTexture("font-extend.png")
        };

        return graphicsTextures.Concat(fontTextures).ToDictionary(x => x.Key, x => x.Value);
    }

    public static bool TryGetCustomTexture(string path, out Texture2D texture)
    {
        return customTextureCache.TryGetValue(path, out texture) && texture != null;
    }
}
