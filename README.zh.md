# ProdigalHan

> [English Documentation](README.md)

游戏《Prodigal》的完整中文本地化补丁，通过运行时文本替换、动态字体扩展（支持 2000+ 汉字）和精确文本位置管理实现混合语言对话框的完美显示。

## 项目概述

ProdigalHan 为游戏《Prodigal》提供完整的简体中文本地化支持。本 mod 使用运行时字符串拦截和动态精灵生成，而非直接修改游戏资源文件，确保兼容性和非侵入式设计。自定义字体渲染系统解决了在游戏固定字符槽和对话框布局约束下显示中文文本的位置精度问题。

### 翻译版本

本地化提供两个版本，以满足不同玩家的偏好：

- **标准版本**（不启用 `allch`）- 保留原始英文角色名、地点名和专有术语，仅翻译对话和 UI 文本。这保持了原始命名规范，对于想要与英文版本或wiki交叉参考的玩家非常有用。**这是默认版本。**
- **完整汉化版本**（启用 `allch`）- 将所有角色名、地点名和专有术语翻译成中文。这为喜欢完全用中文阅读的玩家提供了完整的沉浸式体验。

版本选择取决于你的偏好：保留原始名字便于参考，或完全拥抱中文本地化以获得沉浸感。

**切换到完整汉化版本：**

编辑配置文件 `游戏根目录/BepInEx/config/com.aiden.prodigalhan.cfg` 并将：

```
isAllch = false
```

改为：

```
isAllch = true
```

然后重新启动游戏使更改生效。

核心组件：

1. **本地化插件** - 基于 BepInEx 的运行时翻译系统，具有多策略回退匹配
2. **字体管理** - 从自定义字体图集动态生成字符精灵
3. **布局引擎** - 对话框和工具提示的字符级位置管理系统
4. **资源补丁** - 自定义纹理和资源加载，无需修改原始游戏文件

技术特性：

- 使用 [Harmony](https://harmony.pardeike.net/) 库在运行时拦截游戏方法，动态替换对话、UI 文本、物品名称和成就
- 通过动态精灵生成支持 2000+ 汉字的自定义字体渲染
- 精确的文本布局管理，处理可变宽度字符和混合英文/中文文本
- 可配置行间距的工具提示文本换行
- 模块化架构，分为 9 个专门的管理器类
- 无需修改原始游戏资源—完全通过插件加载工作

## 技术亮点

### 带有布局精度的 C# 运行时补丁

- **[Harmony 补丁](https://harmony.pardeike.net/articles/patching.html)** - 使用 Prefix/Postfix 模式拦截游戏方法：
  - 钩取 `GameMaster.CreateSpeech()` 用于对话翻译和调用者追踪
  - 钩取 `CHAT_BOX` 方法用于文本布局调整和字符位置管理
  - 钩取 `UI.PULL_SPRITE()` 用于自定义字体字符精灵检索
  - 钩取 `TooltipWindow.OPEN()` 用于工具提示文本换行和位置管理

- **字符级位置管理** - 在字符级别跟踪文本布局，考虑因素包括：
  - 可变字符宽度（全角、半角、古文字间距）
  - 行换行和溢出检测
  - 游戏 TEXT 数组中的单个字符精灵位置
  - 基于相邻字符的动态偏移计算

- **翻译匹配策略** - 多级回退系统按顺序尝试翻译：
  - 精确匹配查询
  - 通用 NPC 信息翻译
  - 客人角色信息翻译
  - 路标信息查询
  - 名称占位符翻译
  - 包含变量替换的模板翻译

### 字体管理和精灵生成

- **动态精灵缓存** - 从自定义字体图集按需生成和缓存字符精灵
- **语言感知渲染** - 针对现代中文和古文字样式的不同字体处理
- **纹理替换** - 拦截纹理加载以提供自定义 UI 和字符图形

### 游戏特定的布局处理

- **行追踪系统** - 检测文本渲染期间的行变化，用于适当的布局重新计算
- **即时 vs. 渐进显示** - 即时文本显示与逐字显示的不同位置管理策略
- **精灵中心点修正** - 调整精灵中心点和背景尺寸以适应翻译文本
- **名字牌位置** - 根据字符宽度智能调整说话者名称位置

## 技术与库

### .NET / C#

- **[BepInEx 5.x](https://github.com/BepInEx/BepInEx)** - Unity 游戏修改框架，提供插件加载和日志系统
- **[HarmonyLib 2.x](https://github.com/pardeike/Harmony)** - 运行时方法补丁库，用于非侵入式代码注入
- **[XUnity.Common](https://github.com/bbepis/XUnity.Core)** - 扩展修改实用工具库

### 字体

- **[Fusion Pixel Font](https://github.com/TakWolf/fusion-pixel-font)** - 开源位图字体，支持简体和繁体中文字符
- **[文泉驿点阵宋体 TTF](https://github.com/AmusementClub/WenQuanYi-Bitmap-Song-TTF)** - 位图字体，提供像素完美的中文字符渲染

### 翻译协作

- **[ParaTranz](https://paratranz.cn/)** - 非营利本地化协作平台，用于众包翻译管理

## 项目结构

```
ProdigalHan/
├── bin/                    # 编译输出
├── resources/              # 翻译数据和自定义字体图集
│   └── unique_chinese_chars.txt    # 字体生成的字符列表
└── scripts/                # 编译和自动化脚本
```

### 关键目录

- **`resources/`** - 包含翻译 CSV 文件和自定义字体数据：
  - 演讲翻译（角色对话）
  - NPC 和客人角色翻译
  - 物品、成就和工具翻译
  - 路标和位置翻译
  - 中文字符的自定义字体图集

## 代码架构

代码分为 9 个专门的类，每个处理特定的功能域：

#### 1. **[Plugin.cs](Plugin.cs)** - Harmony 补丁协调器
仅负责拦截游戏方法并委托给处理程序类。协调所有管理器系统的初始化并注册 Harmony 补丁。

#### 2. **[TranslationManager.cs](TranslationManager.cs)** - 翻译管理
管理所有翻译字典并提供统一翻译查询，具有多策略回退匹配。

**关键方法：**
- `Initialize()` - 加载所有翻译 CSV 文件
- `TryGetSpeechTranslationWithPlaceholders()` - 匹配带变量替换的翻译
- `SpeechTranslations`, `ItemTranslations`, `AchievementTranslations` - 访问翻译字典

#### 3. **[TextLayoutManager.cs](TextLayoutManager.cs)** - 文本位置引擎
处理对话框和工具提示的字符级位置。跟踪行变化，根据字符宽度计算偏移，并在渐进文本显示期间管理位置同步。

**关键方法：**
- `CheckLineChange()` - 检测行换行并重置位置追踪
- `AdjustNamePositions()` - 根据宽度调整角色名称位置
- `GetCharHalfWidth()` - 获取字符渲染的宽度指标
- `UpdateLineXOffset()` - 计算到下一个字符的间距

#### 4. **[TextProcessing.cs](TextProcessing.cs)** - 文本转换
处理文本处理逻辑，包括手动换行插入、颜色代码解析和被忽略字符检测（用于不呈现的标记）。

**关键方法：**
- `InsertManualBreaks()` - 为对话文本添加换行
- `InsertTooltipBreaks()` - 用适当的行长格式化工具提示文本
- `IsIgnoredCharacter()` - 检查字符是否为标记（非可见文本）

#### 5. **[FontManager.cs](FontManager.cs)** - 自定义字体系统
管理从自定义字体图集的动态精灵生成。缓存生成的精灵并处理特定于语言的字体渲染。

**关键方法：**
- `TryGetCharacterSprite()` - 获取或创建字符精灵
- `Initialize()` - 加载自定义字体纹理和字符映射

#### 6. **[ResourceLoader.cs](ResourceLoader.cs)** - 资源加载
处理从插件目录加载翻译 CSV 文件和自定义纹理。

**关键方法：**
- `GetTranslations()` - 从 CSV 加载翻译字典
- `GetCustomTexture()` - 加载自定义纹理文件
- `UseAllchPath()` - 切换到完整中文字符名称路径

#### 7. **[ResourcePatcher.cs](ResourcePatcher.cs)** - 纹理补丁
拦截纹理加载并提供自定义纹理。处理内存中的纹理数据替换。

**关键方法：**
- `ReplaceTexture()` - 用自定义版本替换纹理数据
- `ScanAndReplace()` - 查找并补丁所有加载的纹理

#### 8. **[Constants.cs](Constants.cs)** - 配置
定义所有布局常量，包括字符间距、位置偏移和纹理参数。

#### 9. **[StringDumper.cs](StringDumper.cs)** - 调试实用工具（可选）
开发工具，用于分析游戏字符串和生成翻译源数据。

## 本地化实现

### 为什么选择运行时翻译？

游戏包含大量硬编码的字符串引用和位置数据。直接修改游戏文件会破坏：
- 字符串比较逻辑
- 特殊格式和颜色标记
- 物品和成就识别
- 结局判定逻辑

相反，运行时拦截允许动态翻译，无需修改游戏资源。

### 构建和部署工作流

完整的构建和部署本地化补丁的工作流：

1. **下载翻译数据**
   - 从 [ParaTranz](https://paratranz.cn/projects/17839) 下载 CSV 文件
   - 将所有 CSV 文件放入 `resources/` 文件夹

2. **提取中文字符**
   - 运行 [`scripts/extract_unique_chinese_chars.py`](scripts/extract_unique_chinese_chars.py)
   - 此脚本分析所有翻译 CSV 文件并提取字体图集所需的唯一中文字符
   - 输出：字体图集生成器使用的字符列表文件

3. **生成字体图集**
   - 运行 [`scripts/generate_font_atlas.py`](scripts/generate_font_atlas.py)
   - 此脚本将所有提取的中文字符渲染为位图字体图集图像
   - 生成的图集替换原始游戏字体并支持所有翻译内容

4. **构建和部署**
   - 运行 `dotnet build` 编译 C# 插件
   - 将编译的 DLL 文件从 `bin/Debug/` 复制到你的游戏 `BepInEx/plugins/` 文件夹
   - 启动游戏以加载本地化补丁

### 文本布局策略

游戏的对话框使用固定的 29 字符宽、4 行布局，每个字符槽有单个 TEXT 精灵对象。核心挑战是中文字符占用的视觉空间比 ASCII 字符大。

解决方案：

1. **字符位置** - 在渲染时跟踪每个字符的位置
2. **宽度指标** - 计算每个字符的视觉宽度（全角、半角、古文）
3. **偏移累积** - 在向行添加字符时构建累积 X 偏移
4. **精灵放置** - 根据计算的偏移定位每个字符精灵

### 字体扩展工作流

1. 从翻译 CSV 文件提取所有唯一的中文字符
2. 使用自定义字体图集渲染字符
3. 在运行时，FontManager 按需从图集生成 Sprite 对象
4. 精灵被缓存以避免重新生成

## 许可证

### 项目代码

在 **[GNU LGPL v2.1](LICENSE)** 许可证下发布。

### 第三方组件

- **BepInEx**: [LGPL-2.1](https://github.com/BepInEx/BepInEx/blob/master/LICENSE)
- **HarmonyLib**: [MIT](https://github.com/pardeike/Harmony/blob/master/LICENSE)

### 翻译内容

翻译内容来自 [ParaTranz 平台](https://paratranz.cn/projects/17839)，采用 **CC BY-NC 4.0** 许可证。

---

**注意**：本项目仅供学习交流使用，请支持正版游戏。
