#!/usr/bin/env python3
"""Check all shipped RimWorld keyed translations against the English format contract."""
from collections import Counter
from pathlib import Path
import re
import xml.etree.ElementTree as ET


LANGUAGES = {
    "ChineseSimplified", "ChineseTraditional", "English", "French", "German",
    "Japanese", "Korean", "Portuguese", "PortugueseBrazilian", "Russian",
    "Spanish", "SpanishLatin",
}
PLACEHOLDER = re.compile(r"\{\d+\}")


def read_language(folder: Path) -> dict[str, str]:
    strings: dict[str, str] = {}
    for path in sorted((folder / "Keyed").glob("*.xml")):
        root = ET.parse(path).getroot()
        if root.tag != "LanguageData":
            raise ValueError(f"{path}: expected LanguageData")
        for element in root:
            value = element.text or ""
            if element.tag in strings or len(element) or not value.strip():
                raise ValueError(f"{path}: duplicate, nested, or empty key {element.tag}")
            strings[element.tag] = value
    return strings


def read_def_injected(folder: Path) -> dict[str, str]:
    strings: dict[str, str] = {}
    def_dir = folder / "DefInjected"
    if not def_dir.is_dir():
        return strings
    for path in sorted(def_dir.rglob("*.xml")):
        root = ET.parse(path).getroot()
        if root.tag != "LanguageData":
            raise ValueError(f"{path}: expected LanguageData")
        rel = path.relative_to(def_dir).as_posix()
        for element in root:
            value = element.text or ""
            key = f"{rel}:{element.tag}"
            if key in strings or len(element) or not value.strip():
                raise ValueError(f"{path}: duplicate, nested, or empty key {element.tag}")
            strings[key] = value
    return strings


def check(languages: Path) -> None:
    actual = {folder.name for folder in languages.iterdir() if folder.is_dir()}
    if missing := LANGUAGES - actual:
        raise ValueError(f"Missing language folders: {', '.join(sorted(missing))}")
    english = read_language(languages / "English")
    english_defs = read_def_injected(languages / "English")
    reference_defs = english_defs or None
    checked_defs = len(english_defs)
    for name in sorted(actual):
        strings = read_language(languages / name)
        if strings.keys() != english.keys():
            missing = sorted(english.keys() - strings.keys())
            extra = sorted(strings.keys() - english.keys())
            raise ValueError(f"{name}: missing keys {missing}, extra keys {extra}")
        for key, value in strings.items():
            if Counter(PLACEHOLDER.findall(value)) != Counter(PLACEHOLDER.findall(english[key])):
                raise ValueError(f"{name}/{key}: format placeholders differ from English")
            if "▁" in value or "\ufffd" in value:
                raise ValueError(f"{name}/{key}: unexpected token or replacement character")
        if name == "English" and not english_defs:
            continue
        defs = read_def_injected(languages / name)
        if reference_defs is None:
            reference_defs = defs
            checked_defs = len(defs)
        elif defs.keys() != reference_defs.keys():
            missing = sorted(reference_defs.keys() - defs.keys())
            extra = sorted(defs.keys() - reference_defs.keys())
            raise ValueError(f"{name} DefInjected: missing {missing}, extra {extra}")
        for key, value in defs.items():
            if "▁" in value or "\ufffd" in value:
                raise ValueError(f"{name}/DefInjected/{key}: unexpected token or replacement character")
    print(f"{len(english)} translated keys and {checked_defs} DefInjected entries checked in {len(actual)} language folders")


if __name__ == "__main__":
    try:
        check(Path(__file__).resolve().parent.parent / "Languages")
    except (ET.ParseError, ValueError) as error:
        raise SystemExit(str(error)) from error
