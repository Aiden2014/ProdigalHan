#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
复制资源文件到游戏目录的脚本

功能：
1. 将resources文件夹下的不带有-ALLCH后缀的csv文件和font.png文件复制到resources目录
2. 将resources文件夹下的所有csv文件和font.png文件复制到resources_allch目录，
   同时将带有-ALLCH后缀的文件重命名为不带有-ALLCH后缀的文件
"""

import os
import shutil
from pathlib import Path

# 定义路径
SCRIPT_DIR = Path(__file__).parent
PROJECT_ROOT = SCRIPT_DIR.parent
RESOURCES_SOURCE = PROJECT_ROOT / "resources"

# 游戏目录
GAME_PLUGINS_DIR = Path(r"D:\SteamLibrary\steamapps\common\Prodigal\BepInEx\plugins")
RESOURCES_DEST = GAME_PLUGINS_DIR / "resources"
RESOURCES_ALLCH_DEST = GAME_PLUGINS_DIR / "resources_allch"


def copy_non_allch_files():
    """
    任务1: 复制不带有-ALLCH后缀的csv文件和font.png文件到resources目录
    """
    print("=" * 60)
    print("任务1: 复制非ALLCH文件到 resources 目录")
    print("=" * 60)

    # 确保目标目录存在
    RESOURCES_DEST.mkdir(parents=True, exist_ok=True)

    # 复制font.png
    font_src = RESOURCES_SOURCE / "font.png"
    if font_src.exists():
        font_dest = RESOURCES_DEST / "font.png"
        shutil.copy2(font_src, font_dest)
        print(f"✓ 复制: font.png")

    # 获取所有CSV文件
    csv_files = list(RESOURCES_SOURCE.glob("*.csv"))

    # 过滤不带-ALLCH后缀的文件
    non_allch_files = [f for f in csv_files if not f.stem.endswith("-ALLCH")]

    for csv_file in non_allch_files:
        dest_file = RESOURCES_DEST / csv_file.name
        shutil.copy2(csv_file, dest_file)
        print(f"✓ 复制: {csv_file.name}")

    print(f"\n已复制 {len(non_allch_files)} 个非ALLCH CSV文件和font.png\n")


def copy_all_files_with_allch_rename():
    """
    任务2: 复制所有csv文件和font.png到resources_allch目录，
    同时将带有-ALLCH后缀的文件重命名为不带有-ALLCH后缀的文件
    """
    print("=" * 60)
    print("任务2: 复制所有文件到 resources_allch 目录")
    print("=" * 60)

    # 确保目标目录存在
    RESOURCES_ALLCH_DEST.mkdir(parents=True, exist_ok=True)

    # 复制font.png
    font_src = RESOURCES_SOURCE / "font.png"
    if font_src.exists():
        font_dest = RESOURCES_ALLCH_DEST / "font.png"
        shutil.copy2(font_src, font_dest)
        print(f"✓ 复制: font.png")

    # 获取所有CSV文件
    csv_files = list(RESOURCES_SOURCE.glob("*.csv"))

    # 分开处理：先处理非ALLCH文件，再处理ALLCH文件
    allch_files = [f for f in csv_files if f.stem.endswith("-ALLCH")]
    non_allch_files = [f for f in csv_files if not f.stem.endswith("-ALLCH")]

    copied_count = 0
    renamed_count = 0

    # 第一步：复制非ALLCH文件
    for csv_file in non_allch_files:
        dest_file = RESOURCES_ALLCH_DEST / csv_file.name
        shutil.copy2(csv_file, dest_file)
        print(f"✓ 复制: {csv_file.name}")
        copied_count += 1

    # 第二步：复制并重命名ALLCH文件（会覆盖第一步中相同名称的非ALLCH文件）
    for csv_file in allch_files:
        dest_file = RESOURCES_ALLCH_DEST / csv_file.name
        shutil.copy2(csv_file, dest_file)

        # 去掉-ALLCH后缀
        new_name = csv_file.stem[:-6] + ".csv"  # -6 是去掉"-ALLCH"
        new_dest = RESOURCES_ALLCH_DEST / new_name

        # 如果目标文件已存在，先删除
        if new_dest.exists():
            new_dest.unlink()

        # 重命名文件
        dest_file.rename(new_dest)
        print(f"✓ 复制并重命名: {csv_file.name} → {new_name}")
        renamed_count += 1
        copied_count += 1

    print(f"\n已复制 {copied_count} 个CSV文件（{renamed_count} 个已重命名）和font.png\n")


def main():
    print("\n")
    print("╔" + "=" * 58 + "╗")
    print("║" + " " * 58 + "║")
    print("║" + "  ProdigalHan 资源文件复制工具".center(58) + "║")
    print("║" + " " * 58 + "║")
    print("╚" + "=" * 58 + "╝")
    print()

    try:
        # 检查源目录
        if not RESOURCES_SOURCE.exists():
            print(f"❌ 错误: 源目录不存在: {RESOURCES_SOURCE}")
            return False

        # 检查目标目录的父目录
        if not GAME_PLUGINS_DIR.exists():
            print(f"❌ 错误: 游戏插件目录不存在: {GAME_PLUGINS_DIR}")
            print("请确保游戏已安装在指定位置")
            return False

        # 执行任务
        copy_non_allch_files()
        copy_all_files_with_allch_rename()

        print("=" * 60)
        print("✓ 所有任务完成！")
        print("=" * 60)
        return True

    except Exception as e:
        print(f"\n❌ 发生错误: {e}")
        import traceback
        traceback.print_exc()
        return False


if __name__ == "__main__":
    success = main()
    exit(0 if success else 1)
