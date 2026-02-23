using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProdigalHan;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static ConfigEntry<bool> isAllch;
    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        isAllch = Config.Bind("General", "isAllch", false, "是否翻译中文名（默认关闭，开启后角色和地点都使用翻译后的名字）");
        if (isAllch.Value)
        {
            Logger.LogInfo("isAllch is enabled: All character names will be translated.");
            ResourceLoader.UseAllchPath();
        }
        // new StringDumper().DeepDumpStrings();
        TranslationManager.Initialize();

        // 初始化各个管理器
        TextLayoutManager.Initialize(Logger);
        TextProcessing.Initialize(Logger);
        FontManager.Initialize(Logger, ResourceLoader.GetCustomTexture("font.png"));

        Harmony.CreateAndPatchAll(typeof(Hooks));
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    private bool _hasScannedScene = false;
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 1. 修复底层贴图数据
        if (!_hasScannedScene)
        {
            _hasScannedScene = true;
            ScanAndReplace();
        }

        // 2. 修复具体的 Sprite 引用
        Hooks.ScanAndFixSprites();
    }
    private void ScanAndReplace()
    {
        // 查找内存中所有的 Texture2D
        Texture2D[] allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();
        foreach (var tex in allTextures)
        {
            ReplaceTexture(tex);
        }
    }

    private void ReplaceTexture(Texture2D target)
    {
        if (target == null) return;

        string[] targetNames = { "ProdUI", "ProdUIB", "ProdUIC", "UI", "CG" };

        // 更加鲁棒的名字判断
        bool isTarget = targetNames.Any(n => target.name.Equals(n, StringComparison.OrdinalIgnoreCase));

        if (isTarget)
        {
            try
            {
                byte[] data = ResourceLoader.LoadImage(target.name + ".png"); ;
                // LoadImage 是最暴力的替换，它会重置贴图尺寸和数据
                target.LoadImage(data);
                Logger.LogInfo($"[场景补丁] 成功替换: {target.name}");
            }
            catch (Exception e)
            {
                Logger.LogError($"替换出错: {e.Message}");
            }

        }
    }

    // 简单的CSV转义方法（委托到 StringDumper）
    public static string EscapeCsv(string input) => StringDumper.EscapeCsv(input);
}
public static class Hooks
{
    [HarmonyPatch(typeof(CHAT_BOX), nameof(CHAT_BOX.START_CHAT))]
    [HarmonyPrefix]
    public static void CHAT_BOX_START_CHAT_Prefix(List<GameMaster.Speech> C, ref int ___LINES, ref int ___LENGTH, ref CHAT_BOX __instance, List<char> ___KeysToPress)
    {
        // 重置位置追踪
        TextLayoutManager.ResetLineTracking();

        // 不再修改 LENGTH，保持游戏原始的 29x4 配置
        ___LINES = 3;
        // 这样英文文本可以正常显示，中文文本通过 APPLY_LETTER 动态调整位置
    }

    [HarmonyPatch(typeof(CHAT_BOX), nameof(CHAT_BOX.CLOSE))]
    [HarmonyPostfix]
    public static void CHAT_BOX_CLOSE_Postfix(CHAT_BOX __instance)
    {
        // 重置位置追踪状态
        TextLayoutManager.ResetLineTracking();

        // 如果之前的对话使用了自定义位置，需要重置TEXT字符的X位置到原始位置
        var originalTextX = TextLayoutManager.GetOriginalTextXArray();
        if (originalTextX != null && __instance.TEXT != null)
        {
            for (int i = 0; i < __instance.TEXT.Length && i < originalTextX.Length; i++)
            {
                if (__instance.TEXT[i] != null)
                {
                    Vector3 pos = __instance.TEXT[i].transform.localPosition;
                    // 重置X位置到该槽位的原始位置
                    __instance.TEXT[i].transform.localPosition = new Vector3(originalTextX[i], pos.y, pos.z);
                }
            }
        }
    }

    [HarmonyPatch(typeof(CHAT_BOX), nameof(CHAT_BOX.START_CHAT))]
    [HarmonyPostfix]
    public static void CHAT_BOX_START_CHAT_Postfix(List<GameMaster.Speech> C, CHAT_BOX __instance, List<char> ___KeysToPress)
    {
        // 在名字设置完成后，根据字符类型调整名字字符的位置
        TextLayoutManager.AdjustNamePositions(__instance, C[0].SpeakerName);
    }
    [HarmonyPatch(typeof(CHAT_BOX), nameof(CHAT_BOX.NEXT))]
    [HarmonyPostfix]
    public static void CHAT_BOX_NEXT_Postfix(CHAT_BOX __instance, int ___CHAT_SLOT, List<GameMaster.Speech> ___FULL_CHAT)
    {
        // Reset line tracking for the new dialog line
        TextLayoutManager.ResetLineTracking();
        TextLayoutManager.AdjustNamePositions(__instance, ___FULL_CHAT[___CHAT_SLOT].SpeakerName);
    }

    // 判断是否处于即时显示模式（TXT=6）
    private static bool IsInstantMode()
    {
        return 6 - GameMaster.GM.Save.PlayerOptions.TXT == 0;
    }

    [HarmonyPatch(typeof(CHAT_BOX), "APPLY_LETTER")]
    [HarmonyPrefix]
    public static void CHAT_BOX_APPLY_LETTER_Prefix(CHAT_BOX __instance, int ___T_SLOT, int ___CUR_LINE, int ___LENGTH, List<char> ___KeysToPress, int ___Key_ID)
    {
        // 即时模式下跳过逐字位置调整，由 AdjustAllTextPositionsByTextSlots 统一处理
        if (IsInstantMode()) return;
        // 检测换行或新对话开始
        int currentLineIndex = ___CUR_LINE;
        TextLayoutManager.CheckLineChange(currentLineIndex);
    }

    [HarmonyPatch(typeof(CHAT_BOX), "APPLY_LETTER")]
    [HarmonyPostfix]
    public static void CHAT_BOX_APPLY_LETTER_Postfix(CHAT_BOX __instance, int ___T_SLOT, int ___CUR_LINE, int ___LENGTH, List<char> ___KeysToPress, int ___Key_ID)
    {
        // 即时模式下跳过逐字位置调整，由 AdjustAllTextPositionsByTextSlots 统一处理
        if (IsInstantMode())
        {
            return;
        }

        // 获取当前处理的字符
        // 注意：Key_ID 在 APPLY_LETTER 结束后已经递增了
        int charKeyId = ___Key_ID - 1;
        if (charKeyId < 0 || charKeyId >= ___KeysToPress.Count)
        {
            return;
        }

        char currentChar = ___KeysToPress[charKeyId];

        // 检查是否是颜色标记的一部分，如果是则完全跳过
        // @C 开始颜色，@ 结束颜色
        // 这些字符不占用 TEXT 槽位，所以不应该设置位置或计算偏移量
        if (TextProcessing.IsIgnoredCharacter(___KeysToPress.ToString(), charKeyId))
        {
            return;
        }

        // 如果刚刚显示了一个字符（T_SLOT > 0 且有字符被显示）
        if (___T_SLOT > 0 && ___T_SLOT <= __instance.TEXT.Length)
        {

            // 检测换行或新对话开始
            int currentLineIndex = ___CUR_LINE;
            TextLayoutManager.CheckLineChange(currentLineIndex);
            int prevSlot = ___T_SLOT - 1;
            if (prevSlot >= 0 && prevSlot < __instance.TEXT.Length && __instance.TEXT[prevSlot].sprite != null)
            {
                // 获取当前行的起始位置
                int lineStart = (___CUR_LINE - 1) * ___LENGTH;
                int posInLine = prevSlot - lineStart;

                if (posInLine >= 0)
                {
                    // 使用保存的原始基准X位置
                    int lineIdx = ___CUR_LINE - 1;
                    float baseX = TextLayoutManager.GetLineBaseX(lineIdx, __instance, lineStart);
                    float baseY = __instance.TEXT[prevSlot].transform.localPosition.y;
                    float baseZ = __instance.TEXT[prevSlot].transform.localPosition.z;

                    // 设置当前字符位置
                    float currentXOffset = TextLayoutManager.GetCurrentLineXOffset();
                    __instance.TEXT[prevSlot].transform.localPosition = new Vector3(baseX + currentXOffset, baseY, baseZ);

                    // 计算到下一个字符的间距：当前字符宽度的一半 + 下一个字符宽度的一半
                    float currentHalfWidth = TextLayoutManager.GetCharHalfWidth(currentChar, __instance.LANGUAGE);
                    float nextHalfWidth = LayoutConstants.HalfwidthSpacing / 2f; // 默认半角

                    // 检查下一个可见字符（跳过颜色标记）
                    int nextCharId = charKeyId + 1;
                    while (nextCharId < ___KeysToPress.Count)
                    {
                        char nextChar = ___KeysToPress[nextCharId];

                        // 跳过 @ 字符
                        if (TextProcessing.IsIgnoredCharacter(___KeysToPress.ToString(), nextCharId))
                        {
                            nextCharId++;
                            continue;
                        }

                        // 找到下一个可见字符
                        nextHalfWidth = TextLayoutManager.GetCharHalfWidth(nextChar, __instance.LANGUAGE);
                        break;
                    }

                    TextLayoutManager.UpdateLineXOffset(currentHalfWidth, nextHalfWidth);
                    Plugin.Logger.LogInfo($"[CHAT_BOX.APPLY_LETTER] Updated X offset for next character: {TextLayoutManager.GetCurrentLineXOffset()}, current half-width: {currentHalfWidth}, next half-width: {nextHalfWidth}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(CHAT_BOX), "StartTyping")]
    [HarmonyPostfix]
    public static void CHAT_BOX_StartTyping_Postfix(CHAT_BOX __instance, int ___LENGTH, int ___LINES)
    {
        // 仅在即时模式下，StartTyping 完成后统一调整位置
        if (IsInstantMode())
        {
            TextLayoutManager.AdjustAllTextPositionsByTextSlots(__instance, ___LENGTH, ___LINES);
        }
    }

    [HarmonyPatch(typeof(CHAT_BOX), nameof(CHAT_BOX.NEXT))]
    [HarmonyPostfix]
    public static void CHAT_BOX_NEXT_Postfix_InstantMode(CHAT_BOX __instance, int ___LENGTH, int ___LINES, int ___Key_ID, List<char> ___KeysToPress)
    {
        // 在即时模式下，NEXT 处理后需要调整位置
        // 注意：NEXT COMPLETE 分支会调用 StartTyping()，已由上面的 Postfix 处理
        // 这里主要处理 NEXT WAITING 分支（ShiftUp + APPLY_LETTER）
        // 为了避免重复处理，检查是否是 WAITING 后的状态：
        // WAITING 后 Key_ID < KeysToPress.Count（还有剩余字符）
        // COMPLETE 后会调用 StartTyping 重置 Key_ID=0
        // 简单判断：如果 Key_ID > 0 就认为是从 WAITING 继续的
        if (IsInstantMode() && ___Key_ID > 0 && ___Key_ID <= ___KeysToPress.Count)
        {
            TextLayoutManager.AdjustAllTextPositionsByTextSlots(__instance, ___LENGTH, ___LINES);
        }
    }

    static (string label, Func<string, string, string> predicate)[] lineStrategies =
    [
        ("Exact Match", (name, line) => TranslationManager.SpeechTranslations.TryGetValue(name, out var nameTranslations) && nameTranslations.TryGetValue(line, out var result) ? result : null),
        ("Generic Info Match", (name, line) => TranslationManager.GenericInfoTranslations.TryGetValue(name, out var genericNameTranslations) && genericNameTranslations.TryGetValue(line, out var result) ? result : null),
        ("Guest Info Match", (name, line) => TranslationManager.GuestInfoTranslations.TryGetValue(name, out var guestTranslations) && guestTranslations.TryGetValue(line, out var result) ? result : null),
        ("Signpost Info Match", (name, line) => TranslationManager.SignpostInfoTranslations.TryGetValue(line, out var result) ? result : null),
        ("Name Placeholder Match", (name, line) => TranslationManager.SpeechTranslations.TryGetValue("[Name]", out var namePlaceholderTranslations) && namePlaceholderTranslations.TryGetValue(line, out var result) ? result : null),
        ("Template Match", (name, line) => TranslationManager.TryGetSpeechTranslationWithPlaceholders(name, line, out var result) ? result : null)
    ];

    [HarmonyPatch(typeof(GameMaster), nameof(GameMaster.CreateSpeech))]
    [HarmonyPrefix]
    public static void GameMaster_CreateSpeech_Prefix(int ID, int EXP, ref string LINE, ref string Name, int VOICE)
    {
        // DumpGenericInfo();
        // DumpSignpostInfo();
        // LoadAllScenesAndDumpSignposts();
        // DumpFakeNPCInfo();
        var stackTrace = new System.Diagnostics.StackTrace();
        string callerInfo = "UnknownCaller";

        for (int i = 0; i < stackTrace.FrameCount; i++)
        {
            var frame = stackTrace.GetFrame(i);
            var method = frame.GetMethod();
            if (method == null) continue;

            var declaringType = method.DeclaringType;
            string typeName = declaringType?.Name ?? "Global";
            string methodName = method.Name;

            // 1. 跳过 Harmony/MonoMod 内部方法
            // DMD 是 DynamicMethodDefinition，通常是 Patch 后的代理方法
            if (typeName.Contains("Harmony") || typeName.Contains("Hooks") ||
                typeName.Contains("Patch") || methodName.Contains("Prefix") ||
                methodName.Contains("DMD"))
            {
                continue;
            }
            // 2. 【关键点】跳过 CreateSpeech 自身
            // 无论是在 GameMaster 里，还是生成的闭包里，只要名字包含 CreateSpeech 就不算“外部调用者”
            if (methodName.Contains("CreateSpeech"))
            {
                continue;
            }
            // 3. 处理 Unity 协程或匿名函数 (可选优化)
            // 很多对话是在协程里触发的，名字会长成 <SomeMethod>d__12 这样
            if (declaringType != null && declaringType.IsNested && declaringType.Name.StartsWith("<"))
            {
                // 尝试获取父类名，这样能知道是哪个脚本调用的
                callerInfo = $"{declaringType.DeclaringType?.Name}.{methodName} (Coroutine/Async)";
            }
            else
            {
                callerInfo = $"{typeName}.{methodName}";
            }
            // 找到了第一个既不是 Harmony 也不是 CreateSpeech 自己的方法，那就是调用者
            break;
        }

        Plugin.Logger.LogInfo($"[CreateSpeech] Source:[{callerInfo}] ID:{ID} Name:{Name} Text:{LINE}");

        var isAncient = false;
        if (LINE.StartsWith("]"))
        {
            isAncient = true;
        }

        // 尝试翻译对话内容
        string translatedLine = null;
        string matchLabel = null;
        foreach (var (label, tryTranslate) in lineStrategies)
        {
            translatedLine = tryTranslate(Name, LINE);
            if (translatedLine != null)
            {
                matchLabel = label;
                break;
            }
        }

        if (translatedLine != null)
        {
            LINE = TextProcessing.InsertManualBreaks(translatedLine, isAncient);
            Plugin.Logger.LogInfo($"[CreateSpeech] LINE text modified ({matchLabel}) to: {LINE}");

            // template match 的特殊后处理
            if (matchLabel == "template match" && Name == "ZAEGUL")
            {
                foreach (var cur in TranslationManager.ZaegulWrongNameTranslation)
                {
                    if (LINE.Contains(cur.Key))
                    {
                        LINE = LINE.Replace(cur.Key, cur.Value);
                        Plugin.Logger.LogInfo($"[CreateSpeech] LINE text modified (Zaegul wrong name) to: {LINE}");
                        break;
                    }
                }
            }
        }
        else
        {
            bool hasName = TranslationManager.GenericInfoTranslations.ContainsKey(Name);
            Plugin.Logger.LogWarning($"[CreateSpeech] 翻译未匹配 - Name:{Name} (在GenericInfo中:{hasName}), LINE:{LINE}");
            if (hasName)
            {
                var availableLines = TranslationManager.GenericInfoTranslations[Name].Keys.Take(5);
                Plugin.Logger.LogWarning($"[CreateSpeech] {Name} 下可用的LINE keys (前5个): {string.Join(" | ", availableLines)}");
            }
        }

        // 尝试翻译说话者名字
        if (TranslationManager.SpeecherTranslations.TryGetValue(Name, out var translatedName))
        {
            Name = translatedName;
            Plugin.Logger.LogInfo("[CreateSpeech] Name modified to: " + Name);
        }
        else if (TranslationManager.GenericNameTranslations.TryGetValue(Name, out var genericTranslatedName))
        {
            Name = genericTranslatedName;
            Plugin.Logger.LogInfo("[CreateSpeech] Name modified (generic) to: " + Name);
        }
    }

    [HarmonyPatch(typeof(CHAT_BOX), "Awake")]
    [HarmonyPrefix]
    public static void CHAT_BOX_Awake_Prefix(CHAT_BOX __instance)
    {
        Vector3 nameOffset = LayoutConstants.NameOffset;
        for (int i = 0; i < __instance.NAME.Length; i++)
        {
            __instance.NAME[i].transform.localPosition = nameOffset + new Vector3(LayoutConstants.NameFullwidthSpacing * i, 0f, 0f);
        }

        Transform bgTransform = __instance.transform.Find("ChatBG");
        if (bgTransform == null) return;

        SpriteRenderer sr = bgTransform.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        Sprite oldSprite = sr.sprite;
        float ppu = oldSprite.pixelsPerUnit;

        // 向下延伸 2 像素
        float heightIncrease = LayoutConstants.ChatBGHeightIncrease;

        // 创建新的采样矩形
        // y 减少 2，height 增加 2
        Rect newRect = new Rect(
            oldSprite.rect.x,
            oldSprite.rect.y - heightIncrease,
            oldSprite.rect.width,
            oldSprite.rect.height + heightIncrease
        );

        // 创建新的 Sprite
        Sprite newSprite = Sprite.Create(
            ResourceLoader.GetCustomTexture("UI.png"),
            newRect,
            new Vector2(UIConstants.FixedSpritePivot, UIConstants.FixedSpritePivot),
            ppu,
            0,
            SpriteMeshType.FullRect,
            oldSprite.border
        );
        sr.sprite = newSprite;

        // 【关键补丁】修正位移
        // 因为图片向下增加了 2 像素，为了让"顶端"保持在原位，
        // 物体需要向下移动 (增加像素的一半 / PPU)
        // 如果 Y 轴缩放是 1，那么移动量就是 1 / PPU
        bgTransform.localPosition += new Vector3(0, LayoutConstants.ChatBGYOffset, 0);

        Transform namePlateTransform = __instance.transform.Find("NamePlate");
        namePlateTransform.localPosition += new Vector3(0, LayoutConstants.NamePlateYOffset, 0);

        // 调整 line
        var lineYOffset = 1;  // 第一行初始偏移为1，后续每行递增3

        // 初始化行基准X位置
        TextLayoutManager.InitializeLineBaseX(__instance);

        foreach (Transform child in __instance.transform)
        {
            if (child.name.StartsWith("line1"))
            {
                // 只调整Y位置，不修改X间距
                child.transform.localPosition -= new Vector3(0, lineYOffset, 0);
                lineYOffset += 3;
            }
        }
    }

    [HarmonyPatch(typeof(UI), nameof(UI.PULL_SPRITE))]
    [HarmonyPrefix]
    public static bool UI_PULL_SPRITE_Prefix(ref Sprite __result, char LETTER, CHAT_BOX.TEXT_LANGUAGE L)
    {
        if (FontManager.TryGetCharacterSprite(LETTER, L, out Sprite sprite))
        {
            __result = sprite;
            return false;
        }
        return true;
    }

    // [HarmonyPatch(typeof(ItemDatabase), nameof(ItemDatabase.BeginDatabase))]
    // [HarmonyPostfix]
    public static void ItemDatabase_BeginDatabase_Postfix(ItemDatabase __instance)
    {
        string itemPath = Path.Combine(Paths.PluginPath, "item.csv");
        string itemTooltipPath = Path.Combine(Paths.PluginPath, "item-tooltip.csv");
        using (StreamWriter writer = new StreamWriter(itemPath))
        {
            using (StreamWriter tooltipWriter = new StreamWriter(itemTooltipPath))
            {
                for (int i = 0; i < __instance.Database.Count; i++)
                {
                    var item = __instance.Database[i];
                    string escapedName = Plugin.EscapeCsv(item.Name);
                    string escapedTooltip = Plugin.EscapeCsv(item.TooltipText);
                    writer.WriteLine($"{i},{escapedName}");
                    tooltipWriter.WriteLine($"{i},{escapedTooltip}");
                }
            }

        }
    }

    // [HarmonyPatch(typeof(AchievementWindow), nameof(AchievementWindow.LaunchTooltip))]
    // [HarmonyPostfix]
    // public static void AchievementWindow_CloseTooltip_Postfix(AchievementWindow __instance, UIButton UB)
    // {
    //     StringDumper.DumpAchievementInfo(__instance);
    // }

    private static readonly (string label, Func<string, string, string> predicate)[] tooltipAboutStrategies =
    [
        ("Exact Match", (header, about) => TranslationManager.ToolAboutTranslations.TryGetValue(header, out var headerTranslations) ? headerTranslations : null),
        ("Item Tooltip Match", (header, about) => TranslationManager.ItemTooltipTranslations.TryGetValue(about, out var itemTooltipTranslations) ? itemTooltipTranslations : null),
        ("Achievement About Match", (header, about) => TranslationManager.AchievementAboutTranslations.TryGetValue(about, out var achievementAboutTranslations) ? achievementAboutTranslations : null),
        ("Achievement Hint Match", (header, about) => TranslationManager.AchievementHintTranslations.TryGetValue(about, out var achievementHintTranslations) ? achievementHintTranslations : null)
    ];

    private static (string label, Func<string, string, string> predicate)[] tooltipHeaderStrategies =
    [
        ("Exact Match", (header, about) => TranslationManager.ToolTranslations.TryGetValue(header, out var headerTranslations) ? headerTranslations : null),
        ("Item Match", (header, about) => TranslationManager.ItemTranslations.TryGetValue(header, out var itemTranslations) ? itemTranslations : null),
        ("Achievement Match", (header, about) => TranslationManager.AchievementTranslations.TryGetValue(header, out var achievementTranslations) ? achievementTranslations : null)
    ];

    [HarmonyPatch(typeof(TooltipWindow), nameof(TooltipWindow.OPEN))]
    [HarmonyPrefix]
    public static void TooltipWindow_About_Prefix(TooltipWindow __instance, ref string HEADER, ref string ABOUT, ref int TYPE, ref int ID, ref Sprite S)
    {
        foreach (var tooltip in tooltipAboutStrategies)
        {
            string translatedAbout = tooltip.predicate(HEADER, ABOUT);
            if (translatedAbout != null)
            {
                ABOUT = TextProcessing.InsertTooltipBreaks(translatedAbout);
                Plugin.Logger.LogInfo($"[TooltipWindow.About] ABOUT modified ({tooltip.label}) to: {ABOUT}");
                break;
            }
        }
        foreach (var tooltip in tooltipHeaderStrategies)
        {
            string translatedHeader = tooltip.predicate(HEADER, ABOUT);
            if (translatedHeader != null)
            {
                HEADER = translatedHeader;
                Plugin.Logger.LogInfo($"[TooltipWindow.About] HEADER modified ({tooltip.label}) to: {HEADER}");
                break;
            }
        }
        //tooltip 的 ABOUT 字段需要换行，每行16个字符
        ABOUT = TextProcessing.InsertTooltipBreaks(ABOUT);
    }

    [HarmonyPatch(typeof(TooltipWindow), nameof(TooltipWindow.OPEN))]
    [HarmonyPostfix]
    public static void TooltipWindow_About_Postfix(TooltipWindow __instance, string HEADER, string ABOUT)
    {
        // 初始化 Tooltip 的基准位置
        TextLayoutManager.InitializeTooltipPositions(__instance);

        // 调整 tooltip name 和 about 的位置
        TextLayoutManager.AdjustTooltipNamePositions(__instance, HEADER);
        TextLayoutManager.AdjustTooltipAboutPositions(__instance, ABOUT);
    }

    // 获取或创建修复后的 Sprite
    private static Sprite GetOrCreateFixedSprite(Sprite original)
    {
        Sprite fixedSprite = Sprite.Create(
                original.texture,
                original.rect,
                new Vector2(0.5f, 0.5f),
                1.0f,
                0,
                SpriteMeshType.FullRect
            );
        fixedSprite.name = original.name;
        return fixedSprite;
    }

    public static void ScanAndFixSprites()
    {
        SpriteRenderer[] allRenderers = GameObject.FindObjectsOfType<SpriteRenderer>(true);
        foreach (var sr in allRenderers)
        {
            if (sr.sprite != null && (sr.sprite.name == "CG_24" || sr.sprite.name == "CG_79"))
            {
                sr.sprite = GetOrCreateFixedSprite(sr.sprite);
                Plugin.Logger.LogInfo($"已强制修正对象 {sr.gameObject.name} 的 Sprite");
            }
        }
    }
}