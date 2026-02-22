
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ResourceLoader
{
    private static string translationFilePath = "resources";
    public static void UseAllchPath()
    {
        translationFilePath = "resources_allch";
    }
    public static Texture2D GetCustomTexture(string photoName)
    {

        // 获取插件 DLL 所在目录下的 resources 文件夹
        string pluginDir = Path.GetDirectoryName(typeof(ResourceLoader).Assembly.Location);
        string path = Path.Combine(pluginDir, translationFilePath, photoName);

        if (File.Exists(path))
        {
            byte[] fileData = File.ReadAllBytes(path);
            // IL2CPP 环境下使用完整的构造函数：width, height, format, mipChain
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;

            // 使用 ImageConversion.LoadImage 加载字节数据
            if (ImageConversion.LoadImage(tex, fileData))
            {
                Texture2D customTexture;
                // 重要：设置与原始纹理相同的参数
                // m_FilterMode: 0 = Point（像素字体必须用点采样，否则会模糊）
                tex.name = photoName.Split('.')[0];
                tex.filterMode = FilterMode.Point;
                // m_WrapMode: 0 = Clamp
                tex.wrapMode = TextureWrapMode.Repeat;
                // 设置各向异性过滤级别
                tex.anisoLevel = 1;
                tex.mipMapBias = 0;
                customTexture = tex;
                // 防止切换场景时被销毁
                UnityEngine.Object.DontDestroyOnLoad(customTexture);
                customTexture.hideFlags = HideFlags.HideAndDontSave;
                return customTexture;
            }
        }
        ProdigalHan.Plugin.Logger.LogError("[GetCustomTexture] Unable to load texture: " + path);
        return null;
    }

    public static byte[] LoadImage(string photoName)
    {
        // 获取插件 DLL 所在目录下的 resources 文件夹
        string pluginDir = Path.GetDirectoryName(typeof(ResourceLoader).Assembly.Location);
        string path = Path.Combine(pluginDir, translationFilePath, photoName);

        if (File.Exists(path))
        {
            return File.ReadAllBytes(path);
        }
        ProdigalHan.Plugin.Logger.LogError("[LoadImage] Unable to load image: " + photoName);
        return null;
    }

    public static string GetUniqueChineseChars()
    {
        var assembly = typeof(ResourceLoader).Assembly;
        using (Stream stream = assembly.GetManifestResourceStream("ProdigalHan.resources.unique_chinese_chars.txt"))
        {
            if (stream == null)
            {
                ProdigalHan.Plugin.Logger.LogError("[GetUniqueChineseChars] Unable to load embedded resource unique_chinese_chars.txt");
                return string.Empty;
            }
            using (StreamReader reader = new StreamReader(stream))
            {
                string content = reader.ReadToEnd();
                return content;
            }
        }
    }


    private static string[] ParseCsvLine(string line)
    {
        List<string> fields = new List<string>();
        bool inQuotes = false;
        System.Text.StringBuilder currentField = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // 转义的引号 ""
                    currentField.Append('"');
                    i++; // 跳过下一个引号
                }
                else
                {
                    // 切换引号状态
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // 字段分隔符（不在引号内）
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        // 添加最后一个字段
        fields.Add(currentField.ToString());

        return fields.ToArray();
    }

    public static Dictionary<string, string> GetTranslations(string csvFileName)
    {
        Dictionary<string, string> translationMap = new Dictionary<string, string>();

        try
        {
            // 从plugin资源目录读取翻译文件
            string pluginDir = Path.GetDirectoryName(typeof(ResourceLoader).Assembly.Location);
            string translationPath = Path.Combine(pluginDir, translationFilePath, csvFileName);
            if (File.Exists(translationPath))
            {
                string[] lines = File.ReadAllLines(translationPath, System.Text.Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // 使用正确的CSV解析（处理引号内的逗号）
                    string[] parts = ParseCsvLine(line);
                    if (parts.Length >= 3)
                    {
                        // 第二列作为key，第三列作为value
                        string key = parts[1];
                        string value = parts[2].ToUpper();

                        if (!string.IsNullOrEmpty(key) && !translationMap.ContainsKey(key))
                        {
                            translationMap[key] = value;
                        }
                    }
                    else if (parts.Length == 2)
                    {
                        var key = parts[1];
                        if (!string.IsNullOrEmpty(key) && !translationMap.ContainsKey(key))
                        {
                            translationMap[key] = parts[1];
                        }
                    }
                }
            }
            else
            {
                ProdigalHan.Plugin.Logger.LogWarning($"[GetTranslations] Translation file does not exist: {translationPath}");
            }
        }
        catch (System.Exception ex)
        {
            ProdigalHan.Plugin.Logger.LogError($"[GetTranslations] Failed to load translation file: {ex.Message}");
        }

        return translationMap;
    }

    public static Dictionary<string, Dictionary<string, string>> GetSpeechTranslations(string csvFileName)
    {
        var translationMap = new Dictionary<string, Dictionary<string, string>>();

        try
        {
            string pluginDir = Path.GetDirectoryName(typeof(ResourceLoader).Assembly.Location);
            string translationPath = Path.Combine(pluginDir, translationFilePath, csvFileName);
            if (File.Exists(translationPath))
            {
                string[] lines = File.ReadAllLines(translationPath, System.Text.Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = ParseCsvLine(line);
                    if (parts.Length >= 3)
                    {
                        // 第一列格式: Pa-Event2-PA DARROW-DEAR PA
                        // 提取第二个到第三个横线之间的值作为外层key
                        string firstCol = parts[0];
                        string speakerKey = ExtractSpeakerKey(firstCol);

                        // 允许空字符串作为合法的 speaker key（用于系统消息等无说话者的文本）
                        if (speakerKey == null) continue;

                        // 第二列作为内层key，第三列作为value
                        string innerKey = parts[1];
                        string value = parts[2].ToUpper();

                        if (string.IsNullOrEmpty(innerKey)) continue;

                        if (!translationMap.ContainsKey(speakerKey))
                        {
                            translationMap[speakerKey] = new Dictionary<string, string>();
                        }

                        if (!translationMap[speakerKey].ContainsKey(innerKey))
                        {
                            translationMap[speakerKey][innerKey] = value;
                        }
                    }
                }
            }
            else
            {
                ProdigalHan.Plugin.Logger.LogWarning($"[GetSpeakerTranslations] Translation file does not exist: {translationPath}");
            }
        }
        catch (System.Exception ex)
        {
            ProdigalHan.Plugin.Logger.LogError($"[GetSpeakerTranslations] Failed to load translation file: {ex.Message}");
        }

        return translationMap;
    }

    public static Dictionary<string, Dictionary<string, string>> GetGenericTranslations(string csvFileName)
    {
        var translationMap = new Dictionary<string, Dictionary<string, string>>();

        try
        {
            string pluginDir = Path.GetDirectoryName(typeof(ResourceLoader).Assembly.Location);
            string translationPath = Path.Combine(pluginDir, translationFilePath, csvFileName);
            if (File.Exists(translationPath))
            {
                string[] lines = File.ReadAllLines(translationPath, System.Text.Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = ParseCsvLine(line);
                    if (parts.Length >= 3)
                    {
                        // 第一列格式: BUNNY-COME BY THE*crocasino*SOMETIME FOR SOME*FUN!
                        // 提取第一个横线之前的内容作为外层key
                        string firstCol = parts[0];
                        string genericName = ExtractGenericKey(firstCol);

                        if (string.IsNullOrEmpty(genericName)) continue;

                        // 第二列作为内层key，第三列作为value
                        string innerKey = parts[1];
                        string value = parts[2].ToUpper();

                        if (string.IsNullOrEmpty(innerKey)) continue;

                        if (!translationMap.ContainsKey(genericName))
                        {
                            translationMap[genericName] = new Dictionary<string, string>();
                        }

                        if (!translationMap[genericName].ContainsKey(innerKey))
                        {
                            translationMap[genericName][innerKey] = value;
                        }
                    }
                }
            }
            else
            {
                ProdigalHan.Plugin.Logger.LogWarning($"[GetGenericTranslations] Translation file does not exist: {translationPath}");
            }
        }
        catch (System.Exception ex)
        {
            ProdigalHan.Plugin.Logger.LogError($"[GetGenericTranslations] Failed to load translation file: {ex.Message}");
        }

        return translationMap;
    }

    public static Dictionary<string, string> GetToolAboutTranslationMap(string csvFileName)
    {
        var translationMap = new Dictionary<string, string>();

        try
        {
            string pluginDir = Path.GetDirectoryName(typeof(ResourceLoader).Assembly.Location);
            string translationPath = Path.Combine(pluginDir, translationFilePath, csvFileName);
            if (File.Exists(translationPath))
            {
                string[] lines = File.ReadAllLines(translationPath, System.Text.Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = ParseCsvLine(line);
                    if (parts.Length >= 3)
                    {
                        // 第一列格式: BLESSED PICK-SUPPOSEDLY STRIKING WITH RAEM'S LIGHT. . .
                        // 提取第一个横线之前的内容作为key
                        string firstCol = parts[0];
                        string toolKey = ExtractGenericKey(firstCol);

                        if (string.IsNullOrEmpty(toolKey)) continue;

                        // 第三列作为value
                        string value = parts[2];

                        if (!translationMap.ContainsKey(toolKey))
                        {
                            translationMap[toolKey] = value;
                        }
                    }
                }
            }
            else
            {
                ProdigalHan.Plugin.Logger.LogWarning($"[GetToolAboutTranslationMap] Translation file does not exist: {translationPath}");
            }
        }
        catch (System.Exception ex)
        {
            ProdigalHan.Plugin.Logger.LogError($"[GetToolAboutTranslationMap] Failed to load translation file: {ex.Message}");
        }

        return translationMap;
    }

    private static string ExtractSpeakerKey(string input)
    {
        // 格式: Pa-Event2-PA DARROW-DEAR PA
        // 需要提取第二个横线到第三个横线之间的内容: PA DARROW
        if (string.IsNullOrEmpty(input)) return null;

        int firstDash = input.IndexOf('-');
        if (firstDash < 0) return null;

        int secondDash = input.IndexOf('-', firstDash + 1);
        if (secondDash < 0) return null;

        int thirdDash = input.IndexOf('-', secondDash + 1);
        if (thirdDash < 0) return null;

        return input.Substring(secondDash + 1, thirdDash - secondDash - 1);
    }

    private static string ExtractGenericKey(string input)
    {
        // 格式: BUNNY-COME BY THE*crocasino*SOMETIME FOR SOME*FUN!
        // 需要提取第一个横线之前的内容: BUNNY
        if (string.IsNullOrEmpty(input)) return null;

        int firstDash = input.IndexOf('-');
        if (firstDash < 0) return null;

        return input.Substring(0, firstDash);
    }

}