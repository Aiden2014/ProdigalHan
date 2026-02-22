# ProdigalHan

> [中文文档](README.zh.md)

Complete Chinese localization for the video game *Prodigal*, featuring runtime text replacement, dynamic font expansion for 2000+ Chinese characters, and precise text layout management for mixed-language dialogue boxes.

## Overview

ProdigalHan provides full Simplified Chinese localization for *Prodigal*. The mod uses runtime string interception and dynamic sprite generation rather than modifying game assets directly, ensuring compatibility and non-invasiveness. A custom font rendering system handles the precise positioning challenges of displaying Chinese text within the game's fixed character slots and dialogue layout constraints.

### Translation Versions

The localization is available in two versions to accommodate different player preferences:

- **Standard Version** (without `allch` tag) - Preserves original English names for characters, locations, and specialized terminology while translating dialogue and UI text. This maintains the original naming conventions and is useful for players who want to cross-reference with the English version or wiki. **This is the default.**
- **Full Localization Version** (`allch` enabled) - Translates all character names, location names, and specialized terms into Chinese. This provides a complete immersive experience for players who prefer reading entirely in Chinese.

The choice between versions depends on your preference: preserve original names for reference, or fully embrace the Chinese localization for immersion.

**To switch to Full Localization Version:**

Edit the configuration file at `ProdigalGamePath/BepInEx/config/com.aiden.prodigalhan.cfg` and change:

```
isAllch = false
```

to:

```
isAllch = true
```

Then restart the game for the changes to take effect.

Core components:

1. **Localization Plugin** - BepInEx-based runtime translation system with multi-strategy fallback matching
2. **Font Management** - Dynamic character sprite generation from custom font atlas
3. **Layout Engine** - Character-by-character positioning system for dialogue boxes and tooltips
4. **Resource Patching** - Custom texture and resource loading without modifying original game files

Technical features:

- Uses [Harmony](https://harmony.pardeike.net/) library to intercept game methods at runtime and replace dialogue, UI text, item names, and achievements
- Custom font rendering with support for 2000+ Chinese characters through dynamic sprite generation
- Precise text layout management for dialogue boxes handling variable-width characters and mixed English/Chinese text
- Tooltip text wrapping with configurable line breaks
- Modular architecture split into 9 specialized manager classes
- No modification of original game assets—works entirely through plugin loading

## Technical Highlights

### C# Runtime Patching with Layout Precision

- **[Harmony Patching](https://harmony.pardeike.net/articles/patching.html)** - Prefix/Postfix patterns to intercept game methods:
  - Hooks `GameMaster.CreateSpeech()` for dialogue translation with caller tracking
  - Hooks `CHAT_BOX` methods for text layout adjustment and character positioning
  - Hooks `UI.PULL_SPRITE()` for custom font character sprite retrieval
  - Hooks `TooltipWindow.OPEN()` for tooltip text wrapping and positioning

- **Character-Level Positioning** - Tracks text layout at the character level, accounting for:
  - Variable character widths (fullwidth, halfwidth, ancient text spacing)
  - Line wrapping and overflow detection
  - Per-character sprite positioning in the game's TEXT array
  - Dynamic offset calculation based on surrounding characters

- **Translation Matching Strategies** - Multi-level fallback system trying translations in order:
  - Exact match lookup
  - Generic NPC info translation
  - Guest character info translation
  - Signpost information lookup
  - Name placeholder translation
  - Template-based translation with variable substitution

### Font Management and Sprite Generation

- **Dynamic Sprite Caching** - Generates and caches character sprites on-demand from custom font atlas
- **Language-Aware Rendering** - Different font handling for modern Chinese vs. ancient text styles
- **Texture Replacement** - Intercepts texture loading to provide custom UI and character graphics

### Game-Specific Layout Handling

- **Line Tracking System** - Detects line changes during text rendering for proper layout recalculation
- **Instant vs. Progressive Display** - Different positioning strategies for instant text display vs. character-by-character reveal
- **Sprite Pivot Correction** - Adjusts sprite pivot points and background dimensions to accommodate translated text
- **Name Plate Positioning** - Smart adjustment of speaker name positions based on character width

## Technologies and Libraries

### .NET / C#

- **[BepInEx 5.x](https://github.com/BepInEx/BepInEx)** - Unity game modding framework providing plugin loading and logging
- **[HarmonyLib 2.x](https://github.com/pardeike/Harmony)** - Runtime method patching library for non-invasive code injection
- **[XUnity.Common](https://github.com/bbepis/XUnity.Core)** - Extended modding utilities library

### Fonts

- **[Fusion Pixel Font](https://github.com/TakWolf/fusion-pixel-font)** - Open-source bitmap font supporting Simplified and Traditional Chinese characters
- **[WenQuanYi Bitmap Song](https://github.com/AmusementClub/WenQuanYi-Bitmap-Song-TTF)** - Bitmap font providing pixel-perfect Chinese character rendering

### Translation Collaboration

- **[ParaTranz](https://paratranz.cn/)** - Non-profit localization collaboration platform for crowdsourced translation management

## Project Structure

```
ProdigalHan/
├── bin/                    # Build output
├── resources/              # Translation data and custom font atlas
│   └── unique_chinese_chars.txt    # Character list for font generation
└── scripts/                # Build and automation scripts
```

### Key Directories

- **`resources/`** - Contains translation CSV files and custom font data:
  - Speech translations (character dialogue)
  - NPC and guest character translations
  - Item, achievement, and tool translations
  - Signpost and location translations
  - Custom font atlas for Chinese characters

## Code Architecture

Code is split into 9 specialized classes, each handling a specific functional domain:

#### 1. **[Plugin.cs](Plugin.cs)** - Harmony Patch Coordinator
Only responsible for intercepting game methods and delegating to handler classes. Coordinates initialization of all manager systems and registers Harmony patches.

#### 2. **[TranslationManager.cs](TranslationManager.cs)** - Translation Management
Manages all translation dictionaries and provides unified translation lookup with multi-strategy fallback matching.

**Key Methods:**
- `Initialize()` - Load all translation CSV files
- `TryGetSpeechTranslationWithPlaceholders()` - Match translations with variable substitution
- `SpeechTranslations`, `ItemTranslations`, `AchievementTranslations` - Access translation dictionaries

#### 3. **[TextLayoutManager.cs](TextLayoutManager.cs)** - Text Positioning Engine
Handles character-level positioning for dialogue boxes and tooltips. Tracks line changes, calculates offsets based on character widths, and manages position synchronization during progressive text reveal.

**Key Methods:**
- `CheckLineChange()` - Detect line wrapping and reset position tracking
- `AdjustNamePositions()` - Position character names based on width
- `GetCharHalfWidth()` - Get width metric for character rendering
- `UpdateLineXOffset()` - Calculate spacing to next character

#### 4. **[TextProcessing.cs](TextProcessing.cs)** - Text Transformation
Handles text processing logic including manual line break insertion, color code parsing, and ignored character detection (for markup that doesn't render).

**Key Methods:**
- `InsertManualBreaks()` - Add line breaks for dialogue text
- `InsertTooltipBreaks()` - Format tooltip text with proper line length
- `IsIgnoredCharacter()` - Check if character is markup, not visible text

#### 5. **[FontManager.cs](FontManager.cs)** - Custom Font System
Manages dynamic sprite generation from custom font atlas. Caches generated sprites and handles language-specific font rendering.

**Key Methods:**
- `TryGetCharacterSprite()` - Get or create sprite for a character
- `Initialize()` - Load custom font texture and character mapping

#### 6. **[ResourceLoader.cs](ResourceLoader.cs)** - Resource Loading
Handles loading of translation CSV files and custom textures from the plugin directory.

**Key Methods:**
- `GetTranslations()` - Load translation dictionary from CSV
- `GetCustomTexture()` - Load custom texture file
- `UseAllchPath()` - Switch to full Chinese character name paths

#### 7. **[ResourcePatcher.cs](ResourcePatcher.cs)** - Texture Patching
Intercepts texture loading and provides custom textures. Handles in-memory texture data replacement.

**Key Methods:**
- `ReplaceTexture()` - Replace texture data with custom version
- `ScanAndReplace()` - Find and patch all loaded textures

#### 8. **[Constants.cs](Constants.cs)** - Configuration
Defines all layout constants including character spacing, positioning offsets, and texture parameters.

#### 9. **[StringDumper.cs](StringDumper.cs)** - Debug Utilities (Optional)
Development tool for analyzing game strings and generating translation source data.

## Localization Implementation

### Why Runtime Translation?

The game contains hardcoded string references and position data. Directly modifying game files would break:
- String comparison logic
- Special formatting and color markers
- Item and achievement identification
- Ending determination logic

Instead, runtime interception allows dynamic translation without modifying game assets.

### Build and Deployment Workflow

The complete workflow for building and deploying the localization patch:

1. **Download Translation Data**
   - Download CSV files from [ParaTranz](https://paratranz.cn/projects/17839)
   - Place all CSV files in the `resources/` folder

2. **Extract Chinese Characters**
   - Run [`scripts/extract_unique_chinese_chars.py`](scripts/extract_unique_chinese_chars.py)
   - This script analyzes all translation CSV files and extracts unique Chinese characters needed for the font atlas
   - Output: Character list file used by the font atlas generator

3. **Generate Font Atlas**
   - Run [`scripts/generate_font_atlas.py`](scripts/generate_font_atlas.py)
   - This script renders all extracted Chinese characters into a bitmap font atlas image
   - The generated atlas replaces the original game font and supports all translated content

4. **Build and Deploy**
   - Run `dotnet build` to compile the C# plugin
   - Copy the compiled DLL files from `bin/Debug/` to your game's `BepInEx/plugins/` folder
   - Launch the game to load the localization patch

### Text Layout Strategy

The game's dialogue box uses a fixed 29-character-wide, 4-line layout with individual TEXT sprite objects for each character slot. The core challenge is that Chinese characters take more visual space than ASCII characters.

The solution:

1. **Character Positioning** - Track each character's position as it's rendered
2. **Width Metrics** - Calculate visual width for each character (fullwidth, halfwidth, ancient)
3. **Offset Accumulation** - Build cumulative X offset as characters are added to a line
4. **Sprite Placement** - Position each character sprite according to calculated offset

### Font Expansion Workflow

1. Extract all unique Chinese characters from translation CSV files
2. Render characters using custom font atlas
3. At runtime, FontManager generates Sprite objects on-demand from the atlas
4. Sprites are cached to avoid regeneration

## License

### Project Code

Released under **[GNU LGPL v2.1](LICENSE)** license.

### Third-Party Components

- **BepInEx**: [LGPL-2.1](https://github.com/BepInEx/BepInEx/blob/master/LICENSE)
- **HarmonyLib**: [MIT](https://github.com/pardeike/Harmony/blob/master/LICENSE)

### Translation Content

Translation content is from [ParaTranz Platform](https://paratranz.cn/projects/17839), licensed under **CC BY-NC 4.0**.

---

**Note**: This project is for educational purposes only. Please support the original game.
