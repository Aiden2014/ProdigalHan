using System.Collections.Generic;
using System.IO;
using BepInEx;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine;

namespace ProdigalHan;

/// <summary>
/// 从游戏程序集中提取字符串并导出到 CSV 文件的工具类。
/// 通过 IL 分析 Assembly-CSharp.dll 中的 CreateSpeech 和 TooltipWindow.OPEN 调用，
/// 提取对话文本、说话者名称、工具提示等信息。
/// </summary>
public class StringDumper
{
    private AssemblyDefinition _assembly;
    private Dictionary<string, string> _fieldInitValues = new Dictionary<string, string>();

    public void DeepDumpStrings()
    {
        string dllPath = Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll");
        _assembly = AssemblyDefinition.ReadAssembly(dllPath);
        string outputPath = Path.Combine(Paths.PluginPath, "speech.csv");
        string speecherOutputPath = Path.Combine(Paths.PluginPath, "speecher.csv");
        string toolOutputPath = Path.Combine(Paths.PluginPath, "tool.csv");
        string toolAboutOutputPath = Path.Combine(Paths.PluginPath, "tool-about.csv");
        HashSet<string> infoSet = new HashSet<string>();
        HashSet<string> nameSet = new HashSet<string>(); // 用于收集唯一的 Name
        HashSet<string> toolHeaderSet = new HashSet<string>(); // 用于收集唯一的 Tool Header
        HashSet<string> toolAboutSet = new HashSet<string>(); // 用于收集唯一的 Tool About（去重用）
        var toolAboutList = new List<(string header, string about)>(); // 用于存储 Header-About 组合

        // 预先扫描所有字段的初始值
        BuildFieldInitValueCache();

        using (StreamWriter writer = new StreamWriter(outputPath))
        {
            // 1. 遍历模块中所有的类型（包括嵌套类、隐藏类）
            foreach (var type in _assembly.MainModule.GetTypes())
            {
                // 2. 遍历类型中所有的方法（包括 private, static 等）
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;

                    var instructions = method.Body.Instructions;
                    for (int i = 0; i < instructions.Count; i++)
                    {
                        var inst = instructions[i];
                        if (inst.OpCode != OpCodes.Call && inst.OpCode != OpCodes.Callvirt)
                            continue;

                        string operandStr = inst.Operand.ToString();

                        // 3. 寻找 CreateSpeech 调用
                        if (operandStr.Contains("CreateSpeech"))
                        {
                            // 4. 从 CreateSpeech 调用中提取 LINE 和 Name 参数
                            var (lineParam, nameParam) = ExtractCreateSpeechParams(instructions, i);

                            if (!string.IsNullOrEmpty(lineParam))
                            {
                                // 收集 Name 到 nameSet（只收集有有效 LINE 的）
                                if (!string.IsNullOrEmpty(nameParam))
                                {
                                    nameSet.Add(nameParam);
                                }

                                string info = $"{type.FullName}-{method.Name}-{nameParam ?? "UNKNOWN"}-{lineParam}";
                                if (infoSet.Contains(info))
                                {
                                    continue;
                                }
                                infoSet.Add(info);
                                string escapedInfo = EscapeCsv(info);
                                string escapedText = EscapeCsv(lineParam);
                                writer.WriteLine($"{escapedInfo},{escapedText}");
                            }
                            else
                            {
                                // LINE 参数找不到时，打印对应的类和函数信息
                                Plugin.Logger.LogWarning($"[DeepDumpStrings] LINE 参数未找到 - 类: {type.FullName}, 方法: {method.Name}, Name参数: {nameParam ?? "UNKNOWN"}");
                            }
                        }
                        // 5. 寻找 TooltipWindow::OPEN 调用
                        else if (operandStr.Contains("TooltipWindow::OPEN"))
                        {
                            var (header, about) = ExtractTooltipOpenParams(instructions, i);

                            if (!string.IsNullOrEmpty(header))
                            {
                                toolHeaderSet.Add(header);
                            }

                            if (!string.IsNullOrEmpty(header) && !string.IsNullOrEmpty(about))
                            {
                                string key = $"{header}-{about}";
                                if (!toolAboutSet.Contains(key))
                                {
                                    toolAboutSet.Add(key);
                                    toolAboutList.Add((header, about));
                                }
                            }
                        }
                    }
                }
            }
        }

        // 写入 speecher.csv，每行两列相同的 Name
        using (StreamWriter speecherWriter = new StreamWriter(speecherOutputPath))
        {
            foreach (var name in nameSet)
            {
                string escapedName = EscapeCsv(name);
                speecherWriter.WriteLine($"{escapedName},{escapedName}");
            }
        }

        // 写入 tool.csv，每行两列相同的 Header
        using (StreamWriter toolWriter = new StreamWriter(toolOutputPath))
        {
            foreach (var header in toolHeaderSet)
            {
                string escapedHeader = EscapeCsv(header);
                toolWriter.WriteLine($"{escapedHeader},{escapedHeader}");
            }
        }

        // 写入 tool-about.csv，第一列是 Header-About，第二列是 About
        using (StreamWriter toolAboutWriter = new StreamWriter(toolAboutOutputPath))
        {
            foreach (var (header, about) in toolAboutList)
            {
                string key = $"{header}-{about}";
                string escapedKey = EscapeCsv(key);
                string escapedAbout = EscapeCsv(about);
                toolAboutWriter.WriteLine($"{escapedKey},{escapedAbout}");
            }
        }

        Plugin.Logger.LogInfo("全量提取完成！检查 speech.csv");
        Plugin.Logger.LogInfo($"已提取 {nameSet.Count} 个唯一说话者到 speecher.csv");
        Plugin.Logger.LogInfo($"已提取 {toolHeaderSet.Count} 个唯一工具标题到 tool.csv");
        Plugin.Logger.LogInfo($"已提取 {toolAboutList.Count} 条工具描述到 tool-about.csv");
    }

    /// <summary>
    /// 预先扫描所有类型的构造函数，提取字段初始值
    /// </summary>
    private void BuildFieldInitValueCache()
    {
        foreach (var type in _assembly.MainModule.GetTypes())
        {
            // 扫描实例构造函数 .ctor
            foreach (var method in type.Methods)
            {
                if (!method.IsConstructor || !method.HasBody) continue;

                var instructions = method.Body.Instructions;
                for (int i = 0; i < instructions.Count - 1; i++)
                {
                    // 查找模式: ldarg.0, ldstr "value", stfld FieldName
                    if (instructions[i].OpCode == OpCodes.Ldstr &&
                        i + 1 < instructions.Count &&
                        instructions[i + 1].OpCode == OpCodes.Stfld)
                    {
                        string value = instructions[i].Operand as string;
                        var field = instructions[i + 1].Operand as FieldReference;
                        if (field != null && value != null)
                        {
                            string key = $"{field.DeclaringType.FullName}.{field.Name}";
                            _fieldInitValues[key] = value;
                        }
                    }
                }
            }

            // 扫描静态构造函数 .cctor
            foreach (var method in type.Methods)
            {
                if (method.Name != ".cctor" || !method.HasBody) continue;

                var instructions = method.Body.Instructions;
                for (int i = 0; i < instructions.Count - 1; i++)
                {
                    if (instructions[i].OpCode == OpCodes.Ldstr &&
                        i + 1 < instructions.Count &&
                        instructions[i + 1].OpCode == OpCodes.Stsfld)
                    {
                        string value = instructions[i].Operand as string;
                        var field = instructions[i + 1].Operand as FieldReference;
                        if (field != null && value != null)
                        {
                            string key = $"{field.DeclaringType.FullName}.{field.Name}";
                            _fieldInitValues[key] = value;
                        }
                    }
                }
            }
        }

        Plugin.Logger.LogInfo($"已缓存 {_fieldInitValues.Count} 个字段初始值");
    }

    /// <summary>
    /// 从 CreateSpeech 调用中提取 LINE 和 Name 参数
    /// CreateSpeech(int ID, int EXP, string LINE, string Name, int VOICE)
    /// </summary>
    /// <returns>(LINE, Name) 元组</returns>
    private (string line, string name) ExtractCreateSpeechParams(Mono.Collections.Generic.Collection<Instruction> instructions, int callIndex)
    {
        // 参数推送顺序：ID(int), EXP(int), LINE(string), Name(string), VOICE(int)
        // VOICE 是 int，所以 Name 参数在 VOICE 之前
        // Name 可能是 Ldstr（字符串字面量）或 Ldfld（字段引用如 this.NameString）

        string name = null;
        string line = null;

        // 从 Call 指令向前搜索
        // 第5个参数 VOICE 是 int (ldc.i4 等)
        // 第4个参数 Name 是 string (Ldstr 或 Ldfld)
        // 第3个参数 LINE 是 string (Ldstr 或 String.Concat 结果)

        for (int i = callIndex - 1; i >= 0 && callIndex - i <= 20; i--)
        {
            var inst = instructions[i];

            // 跳过加载 int 的指令（VOICE 参数）
            if (name == null)
            {
                // 找到 Name 参数
                if (inst.OpCode == OpCodes.Ldstr)
                {
                    name = inst.Operand as string;
                }
                else if (inst.OpCode == OpCodes.Ldfld || inst.OpCode == OpCodes.Ldsfld)
                {
                    // 字段引用，查找字段的值
                    if (inst.Operand is FieldReference field && field.FieldType.FullName == "System.String")
                    {
                        // 1. 先在当前方法中向前搜索对该字段的赋值
                        string localValue = FindFieldAssignmentInMethod(instructions, i, field);
                        if (localValue != null)
                        {
                            name = localValue;
                        }
                        // 2. 再尝试从构造函数缓存中查找
                        else
                        {
                            string key = $"{field.DeclaringType.FullName}.{field.Name}";
                            if (_fieldInitValues.TryGetValue(key, out string initValue))
                            {
                                name = initValue;
                            }
                            else
                            {
                                name = $"[{field.Name}]"; // 找不到时用字段名
                            }
                        }
                    }
                }
                // 检查是否是 String.Concat 调用（Name 参数也可能是拼接的）
                else if ((inst.OpCode == OpCodes.Call || inst.OpCode == OpCodes.Callvirt) &&
                         inst.Operand.ToString().Contains("System.String::Concat"))
                {
                    name = ExtractConcatString(instructions, i);
                }

                if (name != null) continue;
            }
            else if (line == null)
            {
                // 找到 LINE 参数
                if (inst.OpCode == OpCodes.Ldstr)
                {
                    line = inst.Operand as string;
                    break;
                }
                // 检查是否是 String.Concat 调用（拼接字符串）
                else if ((inst.OpCode == OpCodes.Call || inst.OpCode == OpCodes.Callvirt) &&
                         inst.Operand.ToString().Contains("System.String::Concat"))
                {
                    line = ExtractConcatString(instructions, i);
                    break;
                }
            }
        }

        return (line, name);
    }

    /// <summary>
    /// 从 TooltipWindow.OPEN 调用中提取 HEADER 和 ABOUT 参数
    /// OPEN(string HEADER, string ABOUT, int TYPE, int ID, Sprite S)
    /// </summary>
    /// <returns>(HEADER, ABOUT) 元组</returns>
    private (string header, string about) ExtractTooltipOpenParams(Mono.Collections.Generic.Collection<Instruction> instructions, int callIndex)
    {
        // 参数推送顺序：HEADER(string), ABOUT(string), TYPE(int), ID(int), S(Sprite)
        // 从后往前：S(Sprite/null), ID(int), TYPE(int), ABOUT(string), HEADER(string)

        string header = null;
        string about = null;
        int stringParamsFound = 0;

        // 从 Call 指令向前搜索
        for (int i = callIndex - 1; i >= 0 && callIndex - i <= 30; i--)
        {
            var inst = instructions[i];

            // 找字符串参数
            if (inst.OpCode == OpCodes.Ldstr)
            {
                string value = inst.Operand as string;
                if (stringParamsFound == 0)
                {
                    about = value;
                    stringParamsFound++;
                }
                else if (stringParamsFound == 1)
                {
                    header = value;
                    break;
                }
            }
            // 检查是否是 String.Concat 调用（拼接字符串）
            else if ((inst.OpCode == OpCodes.Call || inst.OpCode == OpCodes.Callvirt) &&
                     inst.Operand.ToString().Contains("System.String::Concat"))
            {
                string value = ExtractConcatString(instructions, i);
                if (stringParamsFound == 0)
                {
                    about = value;
                    stringParamsFound++;
                }
                else if (stringParamsFound == 1)
                {
                    header = value;
                    break;
                }
            }
        }

        return (header, about);
    }

    /// <summary>
    /// 从 String.Concat 调用中提取拼接的字符串，用占位符替代可变部分
    /// </summary>
    private string ExtractConcatString(Mono.Collections.Generic.Collection<Instruction> instructions, int concatCallIndex)
    {
        if (instructions[concatCallIndex].Operand is not MethodReference methodRef) return null;

        int paramCount = methodRef.Parameters.Count;

        // 特殊处理：Concat(string[]) 使用数组
        if (paramCount == 1 && methodRef.Parameters[0].ParameterType.IsArray)
        {
            return ExtractConcatFromArray(instructions, concatCallIndex);
        }

        // 核心思路：从 Concat 调用向前，逆向用栈深度回溯找到每个参数的"顶层产出指令"
        // 每个参数在栈上贡献 1 个值
        // 从最后一个参数往前，每次找到"净产出 1 个值"的指令边界

        var paramTopInstructions = new List<int>(); // 每个参数的顶层产出指令索引
        int depth = 0; // 需要回溯的栈深度

        for (int p = 0; p < paramCount; p++)
        {
            depth = 1; // 每个参数需要找到净产出 1 个值的点
            int searchFrom = (p == 0) ? concatCallIndex - 1 :
                             (paramTopInstructions.Count > 0 ? paramTopInstructions[paramTopInstructions.Count - 1] - 1 : concatCallIndex - 1);

            for (int i = searchFrom; i >= 0 && concatCallIndex - i <= 40; i--)
            {
                var inst = instructions[i];
                int pushes = GetPushCount(inst);
                int pops = GetPopCount(inst);

                depth -= pushes;
                depth += pops;

                if (depth <= 0)
                {
                    paramTopInstructions.Add(i);
                    break;
                }
            }
        }

        if (paramTopInstructions.Count != paramCount)
            return null;

        // paramTopInstructions 是从后往前收集的，反转得到从前到后的顺序
        paramTopInstructions.Reverse();

        // 读取每个参数的值
        var parts = new List<string>();
        int placeholderIndex = 0;

        for (int p = 0; p < paramCount; p++)
        {
            int idx = paramTopInstructions[p];
            var inst = instructions[idx];

            if (inst.OpCode == OpCodes.Ldstr)
            {
                parts.Add(inst.Operand as string);
            }
            else
            {
                // 非字面量，用占位符
                parts.Add($"{{{placeholderIndex++}}}");
            }
        }

        return string.Concat(parts);
    }

    /// <summary>
    /// 获取一条指令向栈上推送的值数量
    /// </summary>
    private int GetPushCount(Instruction inst)
    {
        var code = inst.OpCode;

        // 无推送
        if (code.StackBehaviourPush == StackBehaviour.Push0)
            return 0;

        // 推送 1 个值
        if (code.StackBehaviourPush == StackBehaviour.Push1 ||
            code.StackBehaviourPush == StackBehaviour.Pushi ||
            code.StackBehaviourPush == StackBehaviour.Pushi8 ||
            code.StackBehaviourPush == StackBehaviour.Pushr4 ||
            code.StackBehaviourPush == StackBehaviour.Pushr8 ||
            code.StackBehaviourPush == StackBehaviour.Pushref)
            return 1;

        // 推送 2 个值 (dup 等情况)
        if (code.StackBehaviourPush == StackBehaviour.Push1_push1)
            return 2;

        // Call/Callvirt: 如果有返回值则推 1，否则推 0
        if (code.StackBehaviourPush == StackBehaviour.Varpush)
        {
            if (inst.Operand is MethodReference mr)
                return mr.ReturnType.FullName == "System.Void" ? 0 : 1;
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// 获取一条指令从栈上弹出的值数量
    /// </summary>
    private int GetPopCount(Instruction inst)
    {
        var code = inst.OpCode;

        if (code.StackBehaviourPop == StackBehaviour.Pop0)
            return 0;
        if (code.StackBehaviourPop == StackBehaviour.Pop1 ||
            code.StackBehaviourPop == StackBehaviour.Popi ||
            code.StackBehaviourPop == StackBehaviour.Popref)
            return 1;
        if (code.StackBehaviourPop == StackBehaviour.Pop1_pop1 ||
            code.StackBehaviourPop == StackBehaviour.Popi_pop1 ||
            code.StackBehaviourPop == StackBehaviour.Popi_popi ||
            code.StackBehaviourPop == StackBehaviour.Popi_popi8 ||
            code.StackBehaviourPop == StackBehaviour.Popi_popr4 ||
            code.StackBehaviourPop == StackBehaviour.Popi_popr8 ||
            code.StackBehaviourPop == StackBehaviour.Popref_pop1)
            return 2;
        if (code.StackBehaviourPop == StackBehaviour.Popi_popi_popi ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popi ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popi8 ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popr4 ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popr8 ||
            code.StackBehaviourPop == StackBehaviour.Popref_popi_popref)
            return 3;

        // Call/Callvirt: 弹出参数个数 + (实例方法还需要弹出 this)
        if (code.StackBehaviourPop == StackBehaviour.Varpop)
        {
            if (inst.Operand is MethodReference mr)
            {
                int count = mr.Parameters.Count;
                if (mr.HasThis) count++;
                return count;
            }
            return 0;
        }

        return 0;
    }

    /// <summary>
    /// 处理 String.Concat(string[]) 的情况
    /// </summary>
    private string ExtractConcatFromArray(Mono.Collections.Generic.Collection<Instruction> instructions, int concatCallIndex)
    {
        var parts = new List<string>();
        int placeholderIndex = 0;

        // 向前搜索数组元素的存储
        // 模式: newarr, (dup, ldc.i4, ldstr/其他, stelem) * n
        for (int i = concatCallIndex - 1; i >= 0 && concatCallIndex - i <= 50; i--)
        {
            var inst = instructions[i];

            // 找到 stelem.ref 指令，表示存储数组元素
            if (inst.OpCode == OpCodes.Stelem_Ref)
            {
                // 向前找值（ldstr 或其他）
                for (int j = i - 1; j >= 0 && i - j <= 5; j--)
                {
                    var valInst = instructions[j];
                    if (valInst.OpCode == OpCodes.Ldstr)
                    {
                        parts.Insert(0, valInst.Operand as string);
                        break;
                    }
                    else if (valInst.OpCode == OpCodes.Call || valInst.OpCode == OpCodes.Callvirt ||
                             valInst.OpCode == OpCodes.Ldfld || valInst.OpCode == OpCodes.Ldsfld)
                    {
                        parts.Insert(0, $"{{{placeholderIndex++}}}");
                        break;
                    }
                }
            }
            // 找到 newarr 指令，表示数组创建开始
            else if (inst.OpCode == OpCodes.Newarr)
            {
                break;
            }
        }

        return parts.Count > 0 ? string.Concat(parts) : null;
    }

    /// <summary>
    /// 在当前方法中向前搜索对指定字段的最近一次赋值
    /// 查找模式: ldstr "value", stfld field
    /// </summary>
    private string FindFieldAssignmentInMethod(Mono.Collections.Generic.Collection<Instruction> instructions, int currentIndex, FieldReference targetField)
    {
        // 从当前位置向前搜索
        for (int i = currentIndex - 1; i >= 0; i--)
        {
            var inst = instructions[i];

            // 查找 stfld 或 stsfld 指令
            if ((inst.OpCode == OpCodes.Stfld || inst.OpCode == OpCodes.Stsfld) &&
                inst.Operand is FieldReference field &&
                field.Name == targetField.Name &&
                field.DeclaringType.FullName == targetField.DeclaringType.FullName)
            {
                // 找到对目标字段的赋值，向前查找 ldstr
                for (int j = i - 1; j >= 0 && i - j <= 5; j--)
                {
                    if (instructions[j].OpCode == OpCodes.Ldstr)
                    {
                        return instructions[j].Operand as string;
                    }
                }
            }
        }

        return null;
    }

    // 简单的CSV转义方法
    public static string EscapeCsv(string input)
    {
        if (input == null) return "";
        if (input.Contains("\"") || input.Contains(",") || input.Contains("\n") || input.Contains("\r"))
        {
            return $"\"{input.Replace("\"", "\"\"")}\"";
        }
        return input;
    }

    private static void DumpGenericInfo()
    {
        Generic[] allGenerics = Resources.FindObjectsOfTypeAll<Generic>();
        string genericInfoPath = Path.Combine(Paths.PluginPath, "generic-info.csv");
        string genericNamePath = Path.Combine(Paths.PluginPath, "generic-name.csv");
        HashSet<string> genericNames = new HashSet<string>();
        using (StreamWriter infoWriter = new StreamWriter(genericInfoPath))
        {
            using (StreamWriter nameWriter = new StreamWriter(genericNamePath))
            {
                foreach (var generic in allGenerics)
                {
                    // generic.GenSetup(); // 确保 name 被填充
                    string escapedInfo = Plugin.EscapeCsv(string.Join(" | ", generic.Info));
                    var npcComp = generic.GetComponent<NPC>();
                    // 1. 如果名字是空的，尝试强制初始化
                    if (string.IsNullOrEmpty(npcComp.NameString))
                    {
                        // 尝试调用 NPC 的 Awake 方法（假设逻辑在那）
                        var awakeMethod = typeof(NPC).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (awakeMethod != null) awakeMethod.Invoke(npcComp, null);

                        // 或者：如果 dnSpy 里看到有 Setup 之类的方法，直接调用
                        // npcComp.Setup(); 
                    }
                    string escapedName = Plugin.EscapeCsv(npcComp.NameString);
                    infoWriter.WriteLine($"{Plugin.EscapeCsv(npcComp.NameString + "-" + string.Join(" | ", generic.Info))},{escapedInfo}");
                    if (!genericNames.Contains(npcComp.NameString))
                    {
                        nameWriter.WriteLine($"{escapedName},{escapedName}");
                        genericNames.Add(npcComp.NameString);
                    }
                }
            }
        }
    }

    private static void DumpFakeNPCInfo()
    {
        FakeNPC[] allFakeNPC = Resources.FindObjectsOfTypeAll<FakeNPC>();
        string fakeNPCInfoPath = Path.Combine(Paths.PluginPath, "fakenpc-info.csv");
        string fakeNPCNamePath = Path.Combine(Paths.PluginPath, "fakenpc-name.csv");
        HashSet<string> fakeNPCNames = new HashSet<string>();
        using (StreamWriter infoWriter = new StreamWriter(fakeNPCInfoPath))
        {
            using (StreamWriter nameWriter = new StreamWriter(fakeNPCNamePath))
            {
                foreach (var fakeNPC in allFakeNPC)
                {
                    // generic.GenSetup(); // 确保 name 被填充
                    string escapedInfo = Plugin.EscapeCsv(string.Join(" | ", fakeNPC.Info));
                    var npcComp = fakeNPC.GetComponent<NPC>();
                    // 1. 如果名字是空的，尝试强制初始化
                    if (string.IsNullOrEmpty(npcComp.NameString))
                    {
                        // 尝试调用 NPC 的 Awake 方法（假设逻辑在那）
                        var awakeMethod = typeof(NPC).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (awakeMethod != null) awakeMethod.Invoke(npcComp, null);

                        // 或者：如果 dnSpy 里看到有 Setup 之类的方法，直接调用
                        // npcComp.Setup(); 
                    }
                    string escapedName = Plugin.EscapeCsv(npcComp.NameString);
                    infoWriter.WriteLine($"{Plugin.EscapeCsv(npcComp.NameString + "-" + string.Join(" | ", fakeNPC.Info))},{escapedInfo}");
                    if (!fakeNPCNames.Contains(npcComp.NameString))
                    {
                        nameWriter.WriteLine($"{escapedName},{escapedName}");
                        fakeNPCNames.Add(npcComp.NameString);
                    }
                }
            }
        }
    }

    
    private static HashSet<string> _dumpedSignpostKeys = new HashSet<string>();
    private static bool _isLoadingAllScenes = false;
    private static int _scenesLoaded = 0;
    private static int _totalScenesToLoad = 0;

    private static void DumpSignpostInfo()
    {
        string signpostInfoPath = Path.Combine(Paths.PluginPath, "signpost-info-bundle.csv");

        // 使用 Resources.FindObjectsOfTypeAll 获取所有 Signpost，包括未加载场景中的
        Signpost[] allSignposts = Resources.FindObjectsOfTypeAll<Signpost>();

        int newCount = 0;
        using (StreamWriter writer = new StreamWriter(signpostInfoPath, append: true)) // 追加模式
        {
            foreach (var signpost in allSignposts)
            {
                if (signpost == null || signpost.Info == null || signpost.Info.Count == 0) continue;

                string info = string.Join(" | ", signpost.Info);
                string sceneName = signpost.gameObject.scene.name ?? "Unknown";
                string key = $"{sceneName}-{signpost.name}-{info}";

                if (_dumpedSignpostKeys.Contains(key)) continue;
                _dumpedSignpostKeys.Add(key);

                writer.WriteLine($"{Plugin.EscapeCsv(key)},{Plugin.EscapeCsv(info)}");
                newCount++;
            }
        }

        Plugin.Logger.LogInfo($"[DumpSignpost] Total unique signposts found: {_dumpedSignpostKeys.Count} (new: {newCount})");
    }

    // 调用此方法来加载所有场景并导出 Signpost
    public static void LoadAllScenesAndDumpSignposts()
    {
        if (_isLoadingAllScenes)
        {
            Plugin.Logger.LogWarning("[DumpSignpost] Already loading all scenes, please wait...");
            return;
        }

        // 清空之前的记录，重新开始
        _dumpedSignpostKeys.Clear();
        _dumpedGuestKeys.Clear();
        _dumpedGuestNames.Clear();
        string signpostInfoPath = Path.Combine(Paths.PluginPath, "signpost-info-bundle.csv");
        File.WriteAllText(signpostInfoPath, ""); // 清空文件
        string guestNamePath = Path.Combine(Paths.PluginPath, "guest-name.csv");
        string guestInfoPath = Path.Combine(Paths.PluginPath, "guest-info.csv");
        File.WriteAllText(guestNamePath, ""); // 清空文件
        File.WriteAllText(guestInfoPath, ""); // 清空文件

        // 获取 Build Settings 中的所有场景数量
        _totalScenesToLoad = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
        _scenesLoaded = 0;
        _isLoadingAllScenes = true;

        Plugin.Logger.LogInfo($"[DumpSignpost] Starting to load all {_totalScenesToLoad} scenes...");

        // 先导出当前已加载场景的 Signpost
        DumpSignpostInfo();
        // 先导出当前已加载场景的 SpecialGuest
        DumpSpecialGuestInfo();

        // 记录当前已加载的场景，避免重复加载
        HashSet<string> loadedSceneNames = new HashSet<string>();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (scene.isLoaded)
            {
                loadedSceneNames.Add(scene.name);
            }
        }

        // 开始加载所有场景
        for (int i = 0; i < _totalScenesToLoad; i++)
        {
            string scenePath = UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i);
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

            if (loadedSceneNames.Contains(sceneName))
            {
                _scenesLoaded++;
                Plugin.Logger.LogInfo($"[DumpSignpost] Scene '{sceneName}' already loaded, skipping...");
                continue;
            }

            try
            {
                var asyncOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(i, UnityEngine.SceneManagement.LoadSceneMode.Additive);
                if (asyncOp != null)
                {
                    asyncOp.completed += (op) => OnSceneLoaded(sceneName);
                }
                else
                {
                    _scenesLoaded++;
                    Plugin.Logger.LogWarning($"[DumpSignpost] Failed to start loading scene index {i}");
                }
            }
            catch (System.Exception ex)
            {
                _scenesLoaded++;
                Plugin.Logger.LogError($"[DumpSignpost] Error loading scene index {i}: {ex.Message}");
            }
        }
    }

    private static void OnSceneLoaded(string sceneName)
    {
        _scenesLoaded++;
        Plugin.Logger.LogInfo($"[DumpSignpost] Scene '{sceneName}' loaded ({_scenesLoaded}/{_totalScenesToLoad})");

        // 每个场景加载后都导出 Signpost
        DumpSignpostInfo();
        // 每个场景加载后都导出 SpecialGuest
        DumpSpecialGuestInfo();

        // 所有场景加载完毕
        if (_scenesLoaded >= _totalScenesToLoad)
        {
            _isLoadingAllScenes = false;
            Plugin.Logger.LogInfo($"[DumpSignpost] All scenes loaded! Total unique signposts: {_dumpedSignpostKeys.Count}, Total unique guests: {_dumpedGuestKeys.Count}");
        }
    }

    // SpecialGuest 导出相关
    private static HashSet<string> _dumpedGuestKeys = new HashSet<string>();
    private static HashSet<string> _dumpedGuestNames = new HashSet<string>();

    private static void DumpSpecialGuestInfo()
    {
        string guestNamePath = Path.Combine(Paths.PluginPath, "guest-name.csv");
        string guestInfoPath = Path.Combine(Paths.PluginPath, "guest-info.csv");

        SpecialGuest[] allGuests = Resources.FindObjectsOfTypeAll<SpecialGuest>();

        int newCount = 0;
        using (StreamWriter nameWriter = new StreamWriter(guestNamePath, append: true))
        using (StreamWriter infoWriter = new StreamWriter(guestInfoPath, append: true))
        {
            foreach (var guest in allGuests)
            {
                if (guest == null) continue;

                string guestName = guest.Name;
                string greeting = guest.Greeting;
                string loop = guest.Loop;

                // 写入 guest-name.csv（去重）
                if (!string.IsNullOrEmpty(guestName) && !_dumpedGuestNames.Contains(guestName))
                {
                    _dumpedGuestNames.Add(guestName);
                    nameWriter.WriteLine($"{Plugin.EscapeCsv(guestName)},{Plugin.EscapeCsv(guestName)}");
                }

                // 写入 guest-info.csv: Greeting 行
                if (!string.IsNullOrEmpty(greeting))
                {
                    string greetingKey = $"{guestName}-greeting-{greeting}";
                    if (!_dumpedGuestKeys.Contains(greetingKey))
                    {
                        _dumpedGuestKeys.Add(greetingKey);
                        infoWriter.WriteLine($"{Plugin.EscapeCsv(greetingKey)},{Plugin.EscapeCsv(greeting)}");
                        newCount++;
                    }
                }

                // 写入 guest-info.csv: Loop 行
                if (!string.IsNullOrEmpty(loop))
                {
                    string loopKey = $"{guestName}-loop-{loop}";
                    if (!_dumpedGuestKeys.Contains(loopKey))
                    {
                        _dumpedGuestKeys.Add(loopKey);
                        infoWriter.WriteLine($"{Plugin.EscapeCsv(loopKey)},{Plugin.EscapeCsv(loop)}");
                        newCount++;
                    }
                }
            }
        }

        Plugin.Logger.LogInfo($"[DumpGuest] Total unique guest entries found: {_dumpedGuestKeys.Count} (new: {newCount})");
    }

    public static void DumpAchievementInfo(AchievementWindow __instance)
    {
        Plugin.Logger.LogInfo($"AchievementWindow.CloseTooltip called. Object: {__instance.name}, Name length: {__instance.Name.Count}, Hint length: {__instance.Hint.Count}, About length: {__instance.About.Count}");
        string achievementPath = Path.Combine(Paths.PluginPath, "achievement-name.csv");
        string achievementHintPath = Path.Combine(Paths.PluginPath, "achievement-hint.csv");
        string achievementAboutPath = Path.Combine(Paths.PluginPath, "achievement-about.csv");
        using (StreamWriter nameWriter = new StreamWriter(achievementPath))
        {
            using (StreamWriter nameWriter2 = new StreamWriter(achievementHintPath))
            {
                using (StreamWriter aboutWriter = new StreamWriter(achievementAboutPath))
                {
                    for (int i = 0; i < __instance.Name.Count; i++)
                    {
                        string escapedName = Plugin.EscapeCsv(__instance.Name[i]);
                        string escapedHint = Plugin.EscapeCsv(__instance.Hint[i]);
                        string escapedAbout = Plugin.EscapeCsv(__instance.About[i]);
                        nameWriter.WriteLine($"{escapedName},{escapedName}");
                        nameWriter2.WriteLine($"{escapedName},{escapedHint}");
                        aboutWriter.WriteLine($"{escapedName},{escapedAbout}");
                    }
                }
            }
        }
        for (int i = 0; i < __instance.Name.Count; i++)
        {
            Plugin.Logger.LogInfo($"  Name[{i}]: {__instance.Name[i]}");
        }
    }
}
