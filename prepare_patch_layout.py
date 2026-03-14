from __future__ import annotations

import csv
import shutil
from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent
GAME_DIR = Path(r"D:\SteamLibrary\steamapps\common\Minimalist Tower Defense")
RUNTIME_DUMP_DIR = GAME_DIR / "BepInEx" / "Translation" / "dump" / "MinimalistTowerDefense"
RUNTIME_TEMPLATE = RUNTIME_DUMP_DIR / "localization-ko-template.csv"
RUNTIME_ALL_LOCALES = RUNTIME_DUMP_DIR / "localization-all-locales.csv"
RUNTIME_LANGUAGES = RUNTIME_DUMP_DIR / "languages.txt"

OUTPUT_DIR = BASE_DIR / "output"
TRANSLATION_DIR = BASE_DIR / "translation" / "ko"
TARGET_CSV = TRANSLATION_DIR / "localization-ko.csv"
PATCH_CSV = BASE_DIR / "patch" / "BepInEx" / "Translation" / "ko" / "MinimalistTowerDefense" / "localization-ko.csv"


def ensure_dirs() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    TRANSLATION_DIR.mkdir(parents=True, exist_ok=True)
    PATCH_CSV.parent.mkdir(parents=True, exist_ok=True)


def copy_runtime_artifacts() -> None:
    if not RUNTIME_DUMP_DIR.exists():
        raise FileNotFoundError(
            "Runtime dump folder was not found. Run the game once with the plugin enabled first:\n"
            f"{RUNTIME_DUMP_DIR}"
        )

    if RUNTIME_ALL_LOCALES.exists():
        shutil.copy2(RUNTIME_ALL_LOCALES, OUTPUT_DIR / RUNTIME_ALL_LOCALES.name)
    if RUNTIME_LANGUAGES.exists():
        shutil.copy2(RUNTIME_LANGUAGES, OUTPUT_DIR / RUNTIME_LANGUAGES.name)


def build_translation_csv() -> int:
    if not RUNTIME_TEMPLATE.exists():
        raise FileNotFoundError(
            "Runtime template CSV was not found. Run the game once with the plugin enabled first:\n"
            f"{RUNTIME_TEMPLATE}"
        )

    with RUNTIME_TEMPLATE.open("r", encoding="utf-8-sig", newline="") as src:
        reader = csv.DictReader(src)
        rows = list(reader)

    with TARGET_CSV.open("w", encoding="utf-8-sig", newline="") as dst:
        fieldnames = ["term", "description", "english", "translation"]
        writer = csv.DictWriter(dst, fieldnames=fieldnames)
        writer.writeheader()
        for row in rows:
            writer.writerow(
                {
                    "term": row.get("term", ""),
                    "description": row.get("description", ""),
                    "english": row.get("english", ""),
                    "translation": "",
                }
            )

    shutil.copy2(TARGET_CSV, PATCH_CSV)
    return len(rows)


def main() -> None:
    ensure_dirs()
    copy_runtime_artifacts()
    row_count = build_translation_csv()
    print(f"Copied runtime dump from: {RUNTIME_DUMP_DIR}")
    print(f"Wrote translation CSV: {TARGET_CSV}")
    print(f"Mirrored patch CSV: {PATCH_CSV}")
    print(f"Rows: {row_count}")


if __name__ == "__main__":
    main()
