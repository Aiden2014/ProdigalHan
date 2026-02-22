#!/usr/bin/env python3
"""
Generate a font atlas image from unique Chinese characters.
Each character is 9x9 pixels, arranged in a grid with 1px gaps.
Font fallback chain: fusion-10px -> fusion-12px -> WenQuanYi 12px
"""

from PIL import Image, ImageDraw, ImageFont
import os

# Configuration
CHAR_WIDTH = 9
CHAR_HEIGHT = 9
CHARS_PER_ROW = 100
GAP = 1  # 1px gap between characters
MARGIN = 1  # 1px margin on top and left (first char starts at 1,1)

# Paths (relative to script location)
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(SCRIPT_DIR)
CHARS_FILE = os.path.join(PROJECT_DIR, "resources", "unique_chinese_chars.txt")
FONT_10_FILE = os.path.join(PROJECT_DIR, "resources", "fusion-pixel-10px-monospaced-zh_hans.ttf")
FONT_12_FILE = os.path.join(PROJECT_DIR, "resources", "fusion-pixel-12px-monospaced-zh_hans.ttf")
FONT_WQY_FILE = os.path.join(PROJECT_DIR, "resources", "WenQuanYi.Bitmap.Song.12px.ttf")
OUTPUT_FILE = os.path.join(PROJECT_DIR, "resources", "font.png")


def render_char_to_image(font, char, size=20):
    """Render a character to a small image and return the image."""
    temp = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(temp)
    d.text((0, 0), char, font=font, fill=(255, 255, 255, 255))
    return temp


def get_tofu_signature(font):
    """Get the pixel signature of the .notdef (tofu) glyph by rendering a known missing character."""
    missing_char = '\uFFFE'  # Noncharacter, guaranteed to be missing
    temp = render_char_to_image(font, missing_char)
    return list(temp.getdata())


def has_glyph(font, char, tofu_signature):
    """Check if the font has a glyph for the given character (not .notdef/tofu)."""
    temp = render_char_to_image(font, char)
    char_signature = list(temp.getdata())
    return char_signature != tofu_signature


def binarize_image(img, threshold=128):
    """Force all pixels to be either fully white or fully transparent (no anti-aliasing)."""
    pixels = img.load()
    w, h = img.size
    for py in range(h):
        for px in range(w):
            r, g, b, a = pixels[px, py]
            if a >= threshold:
                pixels[px, py] = (255, 255, 255, 255)
            else:
                pixels[px, py] = (0, 0, 0, 0)
    return img


def render_char_scaled(font, char, target_w=CHAR_WIDTH, target_h=CHAR_HEIGHT, no_aa=False):
    """Render a character with given font and scale down to target size."""
    temp = Image.new("RGBA", (20, 20), (0, 0, 0, 0))
    d = ImageDraw.Draw(temp)
    d.text((0, 0), char, font=font, fill=(255, 255, 255, 255))

    # Remove anti-aliasing if requested
    if no_aa:
        binarize_image(temp)

    bbox = temp.getbbox()
    if bbox is None:
        return None

    cropped = temp.crop(bbox)
    scaled = cropped.resize((target_w, target_h), Image.NEAREST)
    return scaled


def is_halfwidth_char(char):
    """Check if a character is half-width (ASCII, numbers, basic punctuation)."""
    code = ord(char)
    # ASCII printable characters (space to ~)
    return code <= 127


def render_char_centered(font, char, target_w=CHAR_WIDTH, target_h=CHAR_HEIGHT, y_offset=0):
    """Render a character and center it horizontally in the target area.

    y_offset: offset applied when pasting into final image (positive = down).
    """
    temp = Image.new("RGBA", (20, 20), (0, 0, 0, 0))
    d = ImageDraw.Draw(temp)
    d.text((0, 0), char, font=font, fill=(255, 255, 255, 255))

    bbox = temp.getbbox()
    if bbox is None:
        return None

    # Crop to actual content
    cropped = temp.crop(bbox)
    char_w, char_h = cropped.size

    # Remember the original top position (for baseline alignment)
    original_top = bbox[1]

    # Create target size image and paste centered
    result = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))

    # Center horizontally, keep original vertical position + y_offset
    paste_x = (target_w - char_w) // 2
    paste_y = original_top + y_offset

    # Make sure we don't go out of bounds
    if paste_x < 0:
        paste_x = 0
    if char_w > target_w:
        cropped = cropped.resize((target_w, char_h), Image.NEAREST)
        char_w = target_w
    if char_h > target_h:
        cropped = cropped.resize((char_w, target_h), Image.NEAREST)

    result.paste(cropped, (paste_x, paste_y), cropped)
    return result


def main():
    # Read characters from file
    with open(CHARS_FILE, "r", encoding="utf-8") as f:
        content = f.read()

    # Get all characters (excluding newlines)
    chars = [c for c in content if c != '\n' and c != '\r']
    total_chars = len(chars)
    print(f"Total characters: {total_chars}")

    # Calculate image dimensions
    num_rows = (total_chars + CHARS_PER_ROW - 1) // CHARS_PER_ROW

    image_width = MARGIN + CHARS_PER_ROW * (CHAR_WIDTH + GAP)
    image_height = MARGIN + num_rows * (CHAR_HEIGHT + GAP)

    print(f"Image size: {image_width} x {image_height}")
    print(f"Rows: {num_rows}")

    # Create image with transparent background
    image = Image.new("RGBA", (image_width, image_height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    # Load fonts
    font_10 = ImageFont.truetype(FONT_10_FILE, 10)
    font_12 = ImageFont.truetype(FONT_12_FILE, 12)
    font_wqy = ImageFont.truetype(FONT_WQY_FILE, 12)

    # Get tofu signatures for each font
    tofu_10 = get_tofu_signature(font_10)
    tofu_12 = get_tofu_signature(font_12)
    tofu_wqy = get_tofu_signature(font_wqy)
    print("Tofu signatures computed for glyph detection")

    fallback_12_count = 0
    fallback_wqy_count = 0
    missing_chars = []
    fallback_12_chars = []
    fallback_wqy_chars = []

    # Draw each character
    for i, char in enumerate(chars):
        col = i % CHARS_PER_ROW
        row = i // CHARS_PER_ROW

        # Calculate position
        x = MARGIN + col * (CHAR_WIDTH + GAP)
        y = MARGIN + row * (CHAR_HEIGHT + GAP)

        if has_glyph(font_10, char, tofu_10):
            if is_halfwidth_char(char):
                # ASCII characters: render centered in 9x9 cell, moved down 1 pixel
                centered = render_char_centered(font_10, char, CHAR_WIDTH, CHAR_HEIGHT, y_offset=-1)
                if centered is not None:
                    image.paste(centered, (x, y), centered)
            else:
                # Full-width characters: draw directly with y offset
                draw.text((x, y - 1), char, font=font_10, fill=(255, 255, 255, 255))
        elif has_glyph(font_12, char, tofu_12):
            # Fallback 1: fusion 12px, scale to 9x9
            fallback_12_count += 1
            fallback_12_chars.append(char)
            scaled = render_char_scaled(font_12, char)
            if scaled is not None:
                image.paste(scaled, (x, y), scaled)
        elif has_glyph(font_wqy, char, tofu_wqy):
            # Fallback 2: WenQuanYi 12px, scale to 9x9 (with anti-aliasing removed)
            fallback_wqy_count += 1
            fallback_wqy_chars.append(char)
            scaled = render_char_scaled(font_wqy, char, no_aa=True)
            if scaled is not None:
                image.paste(scaled, (x, y), scaled)
        else:
            missing_chars.append(char)
            print(f"  Warning: '{char}' (U+{ord(char):04X}) not found in any font")

    print(f"Characters using fusion-12px fallback: {fallback_12_count}")
    if fallback_12_chars:
        print(f"  Chars: {''.join(fallback_12_chars)}")
    print(f"Characters using WenQuanYi fallback: {fallback_wqy_count}")
    if fallback_wqy_chars:
        print(f"  Chars: {''.join(fallback_wqy_chars)}")
    if missing_chars:
        print(f"Characters missing from all fonts: {len(missing_chars)}")
        print(f"  Chars: {''.join(missing_chars)}")

    # Save the image
    image.save(OUTPUT_FILE, "PNG")
    print(f"Font atlas saved to: {OUTPUT_FILE}")


if __name__ == "__main__":
    main()
