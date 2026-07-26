#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate report.md from the deep-research JSON results in ./results/."""

import json
import re
from pathlib import Path

import yaml

BASE_DIR = Path(__file__).parent
RESULTS_DIR = BASE_DIR / "results"
FIELDS_PATH = BASE_DIR / "fields.yaml"
OUTLINE_PATH = BASE_DIR / "outline.yaml"
OUTPUT_PATH = BASE_DIR / "report.md"

# Fields shown in the table of contents next to each item (user-selected).
SUMMARY_FIELDS = ["license", "language_platform", "bt_profile_type", "platform_support"]
SUMMARY_LABELS = {
    "license": "License",
    "language_platform": "Stack",
    "bt_profile_type": "Bluetooth",
    "platform_support": "Platforms",
}
SUMMARY_TRUNCATE = 70

# Bidirectional category-name mapping (RU/EN, snake_case/Title Case) so that
# fields.yaml category keys resolve against whatever nesting a JSON result used.
CATEGORY_MAPPING = {
    "basic_info": ["basic_info", "Basic Info", "Базовая информация"],
    "transport_layer": ["transport_layer", "Transport Layer", "Транспортный слой"],
    "bluetooth_specifics": ["bluetooth_specifics", "Bluetooth Specifics", "Bluetooth-специфика"],
    "streaming_protocol": ["streaming_protocol", "Streaming Protocol", "Протокол стриминга"],
    "jogging_vs_file_streaming": [
        "jogging_vs_file_streaming",
        "Jogging Vs File Streaming",
        "Jogging vs File Streaming",
    ],
    "status_state_machine": ["status_state_machine", "Status State Machine", "Статус и состояние"],
    "ui_architecture": ["ui_architecture", "UI Architecture", "Архитектура UI"],
    "error_handling_recovery": [
        "error_handling_recovery",
        "Error Handling Recovery",
        "Обработка ошибок",
    ],
    "visualization": ["visualization", "Visualization", "Визуализация"],
    "cross_platform": ["cross_platform", "Cross Platform", "Многоплатформенность"],
    "configuration_settings": ["configuration_settings", "Configuration Settings", "Настройки"],
    "firmware_dialect_support": [
        "firmware_dialect_support",
        "Firmware Dialect Support",
        "Диалекты прошивок",
    ],
    "probing_workflow": ["probing_workflow", "Probing Workflow", "Пробирование"],
}

CATEGORY_TITLES = {
    "basic_info": "Basic Info",
    "transport_layer": "Transport Layer",
    "bluetooth_specifics": "Bluetooth Specifics",
    "streaming_protocol": "Streaming Protocol",
    "jogging_vs_file_streaming": "Jogging vs File Streaming",
    "status_state_machine": "Status & State Machine",
    "ui_architecture": "UI Architecture",
    "error_handling_recovery": "Error Handling & Recovery",
    "visualization": "Visualization",
    "cross_platform": "Cross-Platform Support",
    "configuration_settings": "Configuration & Settings",
    "firmware_dialect_support": "Firmware Dialect Support",
    "probing_workflow": "Probing Workflow",
}

_ALWAYS_SKIP_EXTRA = {"_source_file", "uncertain", "name"}


def load_field_categories(path):
    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    categories = []
    for cat in data.get("field_categories", []):
        key = cat["category"]
        fields = [(f["name"], f.get("description", "")) for f in cat.get("fields", [])]
        categories.append((key, fields))
    return categories


def load_topic(path):
    if not path.exists():
        return "Research Report"
    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    return data.get("topic", "Research Report")


def find_field(data, field_name, category_key):
    """Lookup order: top level -> matching category alias -> any nested dict."""
    if field_name in data:
        return data[field_name]
    for alias in CATEGORY_MAPPING.get(category_key, [category_key]):
        node = data.get(alias)
        if isinstance(node, dict) and field_name in node:
            return node[field_name]
    stack = [v for v in data.values() if isinstance(v, dict)]
    while stack:
        node = stack.pop()
        if field_name in node:
            return node[field_name]
        stack.extend(v for v in node.values() if isinstance(v, dict))
    return None


def is_uncertain(field_name, value, uncertain_list):
    if value is None:
        return True
    if isinstance(value, str):
        if not value.strip():
            return True
        if "[uncertain]" in value.lower():
            return True
    for u in uncertain_list:
        u_name = u.split(" ")[0].split("(")[0].strip()
        if u_name == field_name:
            return True
    return False


def truncate(value, n=SUMMARY_TRUNCATE):
    text = str(value).splitlines()[0].strip()
    if "[uncertain]" in text.lower():
        return ""
    if len(text) <= n:
        return text
    cut = text[:n].rsplit(" ", 1)[0]
    return cut + "…"


def format_value(value):
    if isinstance(value, list):
        if not value:
            return ""
        if all(isinstance(v, dict) for v in value):
            lines = []
            for item in value:
                parts = [f"**{k}**: {v}" for k, v in item.items()]
                lines.append("- " + " | ".join(parts))
            return "\n".join(lines)
        str_items = [str(v) for v in value]
        joined = ", ".join(str_items)
        if len(joined) <= 100 and len(str_items) <= 5:
            return joined
        return "\n".join(f"- {v}" for v in str_items)
    if isinstance(value, dict):
        return "\n".join(f"- **{k}**: {format_value(v)}" for k, v in value.items())
    return str(value)


def format_field_block(name, description, value):
    formatted = format_value(value)
    header = f"- **{name}**" + (f" _{description}_" if description else "")
    if isinstance(value, str) and len(value) > 100:
        quoted = "\n".join(f"  > {line}" for line in formatted.splitlines())
        return f"{header}:\n{quoted}"
    if "\n" in formatted:
        indented = "\n".join(f"  {line}" for line in formatted.splitlines())
        return f"{header}:\n{indented}"
    return f"{header}: {formatted}"


def collect_extra(data, known_fields):
    known_aliases = set()
    for aliases in CATEGORY_MAPPING.values():
        known_aliases.update(aliases)
    skip = set(known_fields) | _ALWAYS_SKIP_EXTRA | known_aliases
    extra = {}
    for k, v in data.items():
        if k in skip or isinstance(v, dict):
            continue
        if v is None or (isinstance(v, str) and not v.strip()):
            continue
        extra[k] = v
    return extra


def slugify_anchor(name):
    s = name.lower()
    s = re.sub(r"[^\w\s-]", "", s)
    s = re.sub(r"\s+", "-", s).strip("-")
    return s


def main():
    categories = load_field_categories(FIELDS_PATH)
    known_field_names = [f for _, fields in categories for f, _ in fields]
    topic = load_topic(OUTLINE_PATH)

    items = []
    for jf in sorted(RESULTS_DIR.glob("*.json")):
        data = json.loads(jf.read_text(encoding="utf-8"))
        data["_source_file"] = jf.name
        items.append(data)
    items.sort(key=lambda d: (d.get("name") or d["_source_file"]).lower())

    toc_lines = []
    body_sections = []

    for idx, data in enumerate(items, 1):
        name = data.get("name") or data["_source_file"]
        anchor = slugify_anchor(name)
        uncertain_list = data.get("uncertain", []) or []

        summary_bits = []
        for f in SUMMARY_FIELDS:
            val = find_field(data, f, None)
            if val is None or is_uncertain(f, val, uncertain_list):
                continue
            short = truncate(val)
            if short:
                summary_bits.append(f"{SUMMARY_LABELS.get(f, f)}: {short}")
        summary_str = " | ".join(summary_bits)

        toc_line = f"{idx}. [{name}](#{anchor})"
        if summary_str:
            toc_line += f" — {summary_str}"
        toc_lines.append(toc_line)

        section = [f'## {idx}. {name} <a id="{anchor}"></a>', ""]
        for cat_key, fields in categories:
            cat_blocks = []
            for fname, fdesc in fields:
                val = find_field(data, fname, cat_key)
                if val is None or is_uncertain(fname, val, uncertain_list):
                    continue
                cat_blocks.append(format_field_block(fname, fdesc, val))
            if cat_blocks:
                section.append(f"### {CATEGORY_TITLES.get(cat_key, cat_key)}")
                section.extend(cat_blocks)
                section.append("")

        extra = collect_extra(data, known_field_names)
        if extra:
            section.append("### Прочая информация")
            for k, v in extra.items():
                section.append(f"- **{k}**: {format_value(v)}")
            section.append("")

        if uncertain_list:
            section.append("### Неопределённые поля (uncertain)")
            for u in uncertain_list:
                section.append(f"- {u}")
            section.append("")

        body_sections.append("\n".join(section))

    report = [f"# {topic}", "", f"_{len(items)} items researched._", "", "## Table of Contents", ""]
    report.extend(toc_lines)
    report.append("")
    report.append("---")
    report.append("")
    report.append("\n---\n\n".join(body_sections))

    OUTPUT_PATH.write_text("\n".join(report), encoding="utf-8")
    print(f"Report written to {OUTPUT_PATH} ({len(items)} items)")


if __name__ == "__main__":
    main()
