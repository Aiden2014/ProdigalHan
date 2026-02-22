
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProdigalHan;


/// <summary>
/// 管理所有翻译字典和翻译查找逻辑
/// </summary>
public static class TranslationManager
{
    private static Dictionary<string, string> translationMap = [];
    private static Dictionary<string, Dictionary<string, string>> speechTranslationMap = [];
    private static Dictionary<string, string> speecherTranslationMap = [];
    private static Dictionary<string, Dictionary<string, string>> genericInfoTranslationMap = [];
    private static Dictionary<string, string> genericNameTranslationMap = [];
    private static Dictionary<string, string> signpostInfoTranslationMap = [];
    private static Dictionary<string, string> toolTranslationMap = [];
    private static Dictionary<string, string> toolAboutTranslationMap = [];
    private static Dictionary<string, string> itemTranslationMap = [];
    private static Dictionary<string, string> itemTooltipTranslationMap = [];
    private static Dictionary<string, string> achievementTranslationMap = [];
    private static Dictionary<string, string> achievementHintTranslationMap = [];
    private static Dictionary<string, string> achievementAboutTranslationMap = [];
    private static Dictionary<string, Dictionary<string, string>> guestInfoTranslationMap = [];
    private static Dictionary<string, string> guestNameTranslationMap = [];
    private static Dictionary<string, string> zaegulWrongNameTranslationMap = [];

    private static bool isInitialized = false;

    public static Dictionary<string, Dictionary<string, string>> SpeechTranslations => speechTranslationMap;
    public static Dictionary<string, string> SpeecherTranslations => speecherTranslationMap;
    public static Dictionary<string, Dictionary<string, string>> GenericInfoTranslations => genericInfoTranslationMap;
    public static Dictionary<string, string> GenericNameTranslations => genericNameTranslationMap;
    public static Dictionary<string, string> SignpostInfoTranslations => signpostInfoTranslationMap;
    public static Dictionary<string, string> ToolTranslations => toolTranslationMap;
    public static Dictionary<string, string> ToolAboutTranslations => toolAboutTranslationMap;
    public static Dictionary<string, string> ItemTranslations => itemTranslationMap;
    public static Dictionary<string, string> ItemTooltipTranslations => itemTooltipTranslationMap;
    public static Dictionary<string, string> AchievementTranslations => achievementTranslationMap;
    public static Dictionary<string, string> AchievementHintTranslations => achievementHintTranslationMap;
    public static Dictionary<string, string> AchievementAboutTranslations => achievementAboutTranslationMap;
    public static Dictionary<string, Dictionary<string, string>> GuestInfoTranslations => guestInfoTranslationMap;
    public static Dictionary<string, string> GuestNameTranslations => guestNameTranslationMap;

    public static Dictionary<string, string> ZaegulWrongNameTranslation => zaegulWrongNameTranslationMap;

    public static void Initialize()
    {
        if (isInitialized) return;

        try
        {
            Plugin.Logger.LogInfo("Initializing translations...");
            
            speechTranslationMap = ResourceLoader.GetSpeechTranslations("speech.csv");
            speecherTranslationMap = ResourceLoader.GetTranslations("speecher.csv");
            genericInfoTranslationMap = ResourceLoader.GetGenericTranslations("generic-info.csv");
            genericNameTranslationMap = ResourceLoader.GetTranslations("generic-name.csv");
            signpostInfoTranslationMap = ResourceLoader.GetTranslations("signpost-info-bundle.csv");
            toolTranslationMap = ResourceLoader.GetTranslations("tool.csv");
            toolAboutTranslationMap = ResourceLoader.GetToolAboutTranslationMap("tool-about.csv");
            itemTranslationMap = ResourceLoader.GetTranslations("item.csv");
            itemTooltipTranslationMap = ResourceLoader.GetTranslations("item-tooltip.csv");
            achievementTranslationMap = ResourceLoader.GetTranslations("achievement-name.csv");
            achievementHintTranslationMap = ResourceLoader.GetTranslations("achievement-hint.csv");
            achievementAboutTranslationMap = ResourceLoader.GetTranslations("achievement-about.csv");
            guestInfoTranslationMap = ResourceLoader.GetGenericTranslations("guest-info.csv");
            guestNameTranslationMap = ResourceLoader.GetTranslations("guest-name.csv");
            zaegulWrongNameTranslationMap = ResourceLoader.GetTranslations("zaegul-wrong-name.csv");
            // 初始化占位符模板（需要在 speechTranslationMap 加载后调用）
            InitializeSpeechTemplates();

            isInitialized = true;
            Plugin.Logger.LogInfo("Translations initialized successfully");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Failed to initialize translations: {ex.Message}");
            isInitialized = true; // 避免重复尝试
        }
    }

    public static bool TryGetTranslation(string original, out string translation)
    {
        return translationMap.TryGetValue(original, out translation);
    }

    public static bool ShouldSkipString(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (text.StartsWith("D:/Archive/Code/Tools/TELEPLAY 2 For Unity")) return true;
        if (text.Equals("if exploded=1,down:ifxrangeless 18,explode:ifxrangeless 64,run:iplay idle:break:run@:ifnotplayerfacing notshot:ifxrangegreater 34,notshot:ifnotstate player,shootdown,notshot:goto quicksplode:notshot@:iplay attack:faceplayer:moveforward 20,1:break:explode@:iplay explode:let etimer=30:moveloop@:moveforward 30,1:decrease etimer,1:ifnotplayerfacing notshot2:ifxrangegreater 34,notshot2:ifnotstate player,shootdown,notshot2:goto quicksplode:notshot2@:if etimer>0,moveloop:let etimer=40:pauseloop@:wait 1:ifnotplayerfacing notshot3:ifxrangegreater 34,notshot3:ifnotstate player,shootdown,notshot3:goto quicksplode:notshot3@:add etimer,-1:if etimer>0,pauseloop:isound [headpop]:ifstate player,hide,nodmg:ifstate player,down,nodmg:ifxrangegreater 44,nodmg:state player,hit:nodmg@:add sane,10:add psycholog,[Avoided unnecessary headaches. (sane +10)]:collideoff:let exploded=1:particlehere [bloodspurtup],16,6,0,0:particlehere [blood],-6,-6,40,-120:wait 10:particlehere [blood],6,-18,40,-20:flippedparticlehere [bloodspurtup],-16,2,0,0:wait 10:particlehere [blood],0,-12,30,-40:wait 10:particlehere [blood],-6,-14,-20,-20:wait 10:particlehere [blood],6,-6,-40,-20:wait 10:particlehere [blood],0,-8,-30,-60:wait 10:particlehere [blood],-6,-6,20,-20:wait 10:particlehere [blood],6,-8,40,-40:wait 10:particlehere [blood],0,-12,30,-40:wait 10:particlehere [blood],-6,-14,-20,-70:wait 10:particlehere [blood],6,-16,-40,-40:wait 10:particlehere [blood],0,-18,-30,-20:wait 10:wait 30:iplay dead:wait 90:istate down:break:goto explode:quicksplode@:iplay quickexplode:isound [headpop]:ifstate player,hide,nodmg2:ifstate player,down,nodmg2:ifxrangegreater 29,nodmg2:state player,hit:nodmg2@:add psycho,10:add psycholog,[Killed a Bloodman. (psycho +30)]:collideoff:let exploded=1:particlehere [bloodspurtup],16,6,0,0:particlehere [blood],-6,-6,40,-120:wait 10:particlehere [blood],6,-18,40,-20:flippedparticlehere [bloodspurtup],-16,2,0,0:wait 10:particlehere [blood],0,-12,30,-40:wait 10:particlehere [blood],-6,-14,-20,-20:wait 10:particlehere [blood],6,-6,-40,-20:wait 10:particlehere [blood],0,-8,-30,-60:wait 10:particlehere [blood],-6,-6,20,-20:wait 10:particlehere [blood],6,-8,40,-40:wait 10:particlehere [blood],0,-12,30,-40:wait 10:particlehere [blood],-6,-14,-20,-70:wait 10:particlehere [blood],6,-16,-40,-40:wait 10:particlehere [blood],0,-18,-30,-20:wait 10:wait 30:iplay dead:wait 90:down@:istate down:"))
            return true;

        return false;
    }

    // 用于缓存已编译的正则表达式模板，key 是原始模板字符串
    private static Dictionary<string, Regex> templateRegexCache = [];
    // 用于存储模板对应的翻译，便于快速查找
    private static Dictionary<string, List<(string template, string translation)>> speechTemplateMap = [];
    private static bool templatesInitialized = false;

    /// <summary>
    /// 初始化带占位符的翻译模板
    /// 将含有 {0}, {1} 等占位符的模板预编译为正则表达式
    /// </summary>
    public static void InitializeSpeechTemplates()
    {
        if (templatesInitialized) return;

        foreach (var speakerKvp in speechTranslationMap)
        {
            string speakerName = speakerKvp.Key;
            var translations = speakerKvp.Value;

            foreach (var kvp in translations)
            {
                string originalText = kvp.Key;
                string translatedText = kvp.Value;

                // 检查是否包含占位符 {0}, {1}, {2} 等
                if (Regex.IsMatch(originalText, @"\{\d+\}"))
                {
                    // 为该说话者创建模板列表
                    if (!speechTemplateMap.ContainsKey(speakerName))
                    {
                        speechTemplateMap[speakerName] = [];
                    }

                    speechTemplateMap[speakerName].Add((originalText, translatedText));

                    // 预编译正则表达式
                    if (!templateRegexCache.ContainsKey(originalText))
                    {
                        string pattern = ConvertTemplateToRegex(originalText);
                        templateRegexCache[originalText] = new Regex(pattern, RegexOptions.Compiled);
                        Plugin.Logger.LogInfo($"[TranslationManager] Added template for '{speakerName}': '{originalText}' => regex: '{pattern}'");
                    }
                }
            }
        }

        // 按模板的固定文本长度降序排序（更具体的模板优先匹配）
        // 固定文本长度 = 模板总长度 - 占位符长度
        foreach (var speakerName in speechTemplateMap.Keys.ToList())
        {
            speechTemplateMap[speakerName] = speechTemplateMap[speakerName]
                .OrderByDescending(t => GetTemplateFixedTextLength(t.template))
                .ToList();
        }

        templatesInitialized = true;
        Plugin.Logger.LogInfo($"[TranslationManager] Initialized {templateRegexCache.Count} speech templates with placeholders");
    }

    /// <summary>
    /// 计算模板中固定文本的长度（排除占位符）
    /// </summary>
    private static int GetTemplateFixedTextLength(string template)
    {
        // 移除所有 {0}, {1}, {2} 等占位符后的长度
        return Regex.Replace(template, @"\{\d+\}", "").Length;
    }

    /// <summary>
    /// 将带占位符的模板转换为正则表达式
    /// 例如: "HELLO {0}.*HOW ARE YOU?" => "^HELLO (.+?)\.\*HOW ARE YOU\?$"
    /// </summary>
    private static string ConvertTemplateToRegex(string template)
    {
        // 先把占位符 {0}, {1} 等临时替换为一个不会被转义的占位符
        string placeholder = "\x00PLACEHOLDER\x00";
        string tempTemplate = Regex.Replace(template, @"\{\d+\}", placeholder);

        // 转义所有正则特殊字符
        string escaped = Regex.Escape(tempTemplate);

        // 将临时占位符替换为正则捕获组
        string pattern = escaped.Replace(Regex.Escape(placeholder), "(.+?)");

        return "^" + pattern + "$";
    }

    /// <summary>
    /// 尝试使用占位符模板匹配并翻译文本
    /// </summary>
    /// <param name="speakerName">说话者名字</param>
    /// <param name="actualText">游戏中实际的文本（占位符已被替换）</param>
    /// <param name="translatedText">输出：翻译后的文本</param>
    /// <returns>是否匹配成功</returns>
    public static bool TryGetSpeechTranslationWithPlaceholders(string speakerName, string actualText, out string translatedText)
    {
        translatedText = null;

        // 确保模板已初始化
        if (!templatesInitialized)
        {
            InitializeSpeechTemplates();
        }

        // 检查该说话者是否有模板
        if (!speechTemplateMap.TryGetValue(speakerName, out var templates) || !speechTemplateMap.TryGetValue("[Name]", out var nameTemplates))
        {
            Plugin.Logger.LogInfo($"[TranslationManager] No templates found for speaker: '{speakerName}'");
            return false;
        }

        Plugin.Logger.LogInfo($"[TranslationManager] Checking {templates.Count} templates for speaker: '{speakerName}', text: '{actualText}'");

        // 遍历该说话者的所有模板，尝试匹配
        foreach (var (template, translation) in templates)
        {
            if (!templateRegexCache.TryGetValue(template, out var regex))
            {
                continue;
            }

            Match match = regex.Match(actualText);
            if (match.Success)
            {
                // 提取捕获的值
                List<string> capturedValues = [];
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    capturedValues.Add(match.Groups[i].Value);
                }

                // 将捕获的值填入翻译模板
                translatedText = translation;
                for (int i = 0; i < capturedValues.Count; i++)
                {
                    translatedText = translatedText.Replace($"{{{i}}}", capturedValues[i]);
                }

                Plugin.Logger.LogInfo($"[TranslationManager] Template matched: '{template}' => '{translatedText}'");
                return true;
            }
        }

        return false;
    }
}