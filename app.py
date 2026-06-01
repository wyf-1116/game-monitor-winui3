from __future__ import annotations

import json
import math
import os
import re
import socketserver
import struct
import subprocess
import time
import urllib.parse
from dataclasses import dataclass
from http.server import SimpleHTTPRequestHandler
from pathlib import Path
from typing import Any

from shared_memory import SharedMemoryError, read_named_mapping


ROOT = Path(__file__).resolve().parent
CONFIG_PATH = ROOT / "config.json"
STATIC_ROOT = ROOT / "static"
FPS_HISTORY_SECONDS = 120
HARDWARE_INFO: dict[str, Any] | None = None


DISPLAY_ITEMS = [
    {"id": "fps_current", "label": {"zh": "实时帧率", "en": "Current FPS"}, "group": {"zh": "帧率", "en": "FPS"}},
    {"id": "frame_time", "label": {"zh": "帧生成时间", "en": "Frame Time"}, "group": {"zh": "帧率", "en": "FPS"}},
    {"id": "fps_1_low", "label": {"zh": "1% Low 帧", "en": "1% Low"}, "group": {"zh": "帧率", "en": "FPS"}},
    {"id": "fps_average", "label": {"zh": "平均帧率", "en": "Average FPS"}, "group": {"zh": "帧率", "en": "FPS"}},
    {"id": "cpu_frequency", "label": {"zh": "CPU频率", "en": "CPU Clock"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "cpu_usage", "label": {"zh": "CPU占用率", "en": "CPU Usage"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "cpu_temperature", "label": {"zh": "CPU温度", "en": "CPU Temp"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "cpu_power", "label": {"zh": "CPU功率", "en": "CPU Power"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "gpu_frequency", "label": {"zh": "显卡频率", "en": "GPU Clock"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "gpu_memory_frequency", "label": {"zh": "显存频率", "en": "VRAM Clock"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "gpu_usage", "label": {"zh": "显卡占用率", "en": "GPU Usage"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "gpu_temperature", "label": {"zh": "显卡温度", "en": "GPU Temp"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "gpu_power", "label": {"zh": "显卡功率", "en": "GPU Power"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "gpu_fan_speed", "label": {"zh": "显卡风扇转速", "en": "GPU Fan Speed"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "gpu_memory_usage", "label": {"zh": "显存占用", "en": "VRAM Usage"}, "group": {"zh": "硬件", "en": "Hardware"}},
    {"id": "memory_usage", "label": {"zh": "内存占用", "en": "Memory Usage"}, "group": {"zh": "硬件", "en": "Hardware"}},
]
DEFAULT_DISPLAY_ITEMS = [item["id"] for item in DISPLAY_ITEMS]
HARDWARE_ITEM_IDS = {item["id"] for item in DISPLAY_ITEMS if item["group"]["zh"] == "硬件"}
FPS_HISTORY: dict[int, list[tuple[float, float]]] = {}


SENSOR_RULES = {
    "cpu_frequency": [["cpu"], ["clock", "frequency", "频率"], ["mhz", "ghz"]],
    "cpu_usage": [["cpu"], ["usage", "load", "占用", "使用率"], ["%"]],
    "cpu_temperature": [["cpu"], ["temperature", "temp", "温度"], ["°c", "c", "度"]],
    "cpu_power": [["cpu"], ["power", "功耗", "功率"], ["w"]],
    "gpu_frequency": [["gpu", "core"], ["clock", "frequency", "频率"], ["mhz", "ghz"]],
    "gpu_memory_frequency": [["memory", "vram", "显存"], ["clock", "frequency", "频率"], ["mhz", "ghz"]],
    "gpu_usage": [["gpu"], ["usage", "load", "占用", "使用率"], ["%"]],
    "gpu_temperature": [["gpu"], ["temperature", "temp", "温度"], ["°c", "c", "度"]],
    "gpu_power": [["gpu"], ["power", "功耗", "功率"], ["w"]],
    "gpu_fan_speed": [["fan", "风扇"], ["speed", "tachometer", "转速"], ["rpm", "%"]],
    "gpu_memory_usage": [["memory", "vram", "显存"], ["usage", "used", "占用", "使用率"], ["mb", "gb", "%"]],
    "memory_usage": [["ram", "memory", "内存"], ["usage", "used", "占用", "使用"], ["mb", "gb", "%"]],
}

MAHM_SOURCE_IDS = {
    "gpu_temperature": {0x00000000},
    "gpu_frequency": {0x00000020},
    "gpu_memory_frequency": {0x00000022},
    "gpu_usage": {0x00000030},
    "gpu_power": {0x00000061},
    "gpu_fan_speed": {0x00000010},
    "gpu_memory_usage": {0x00000031},
    "cpu_temperature": {0x00000080},
    "cpu_usage": {0x00000090},
    "cpu_frequency": {0x000000A0},
    "cpu_power": {0x00000100},
    "memory_usage": {0x00000091},
}


def get_total_memory_mb() -> int | None:
    import ctypes

    class MemoryStatus(ctypes.Structure):
        _fields_ = [
            ("dwLength", ctypes.c_ulong),
            ("dwMemoryLoad", ctypes.c_ulong),
            ("ullTotalPhys", ctypes.c_ulonglong),
            ("ullAvailPhys", ctypes.c_ulonglong),
            ("ullTotalPageFile", ctypes.c_ulonglong),
            ("ullAvailPageFile", ctypes.c_ulonglong),
            ("ullTotalVirtual", ctypes.c_ulonglong),
            ("ullAvailVirtual", ctypes.c_ulonglong),
            ("sullAvailExtendedVirtual", ctypes.c_ulonglong),
        ]

    status = MemoryStatus()
    status.dwLength = ctypes.sizeof(status)
    if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
        return None
    return round(status.ullTotalPhys / 1024 / 1024)


def _read_registry_cpu_name() -> str:
    try:
        import winreg

        with winreg.OpenKey(
            winreg.HKEY_LOCAL_MACHINE,
            r"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
        ) as key:
            value, _ = winreg.QueryValueEx(key, "ProcessorNameString")
            return str(value).strip()
    except OSError:
        return ""


def _run_powershell_json(command: str) -> Any:
    try:
        output = subprocess.check_output(
            [
                "powershell",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                command,
            ],
            text=True,
            stderr=subprocess.DEVNULL,
            timeout=5,
        ).strip()
    except (OSError, subprocess.SubprocessError):
        return None

    if not output:
        return None
    try:
        return json.loads(output)
    except json.JSONDecodeError:
        return output


def _read_gpu_name() -> str:
    info = _read_gpu_info()
    return str(info.get("name") or "")


def _read_gpu_info() -> dict[str, Any]:
    payload = _run_powershell_json(
        "Get-CimInstance Win32_VideoController | "
        "Where-Object { $_.Name -and $_.AdapterCompatibility -notmatch 'Microsoft' } | "
        "Sort-Object AdapterRAM -Descending | "
        "Select-Object -First 1 Name,AdapterRAM | ConvertTo-Json -Compress"
    )
    if not isinstance(payload, dict):
        return {"name": "", "memory_total_mb": None}
    adapter_ram = payload.get("AdapterRAM")
    fallback_total_mb = round(adapter_ram / 1024 / 1024) if isinstance(adapter_ram, int) and adapter_ram > 0 else None
    name = str(payload.get("Name") or "").strip()
    total_mb = _read_nvidia_total_memory_mb() if _brand_from_name(name, "gpu") == "NVIDIA" else None
    return {"name": name, "memory_total_mb": total_mb or fallback_total_mb}


def _read_nvidia_total_memory_mb() -> int | None:
    commands = [
        ["nvidia-smi", "--query-gpu=memory.total", "--format=csv,noheader,nounits"],
        [
            r"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
            "--query-gpu=memory.total",
            "--format=csv,noheader,nounits",
        ],
    ]
    for command in commands:
        try:
            output = subprocess.check_output(command, text=True, stderr=subprocess.DEVNULL, timeout=5).strip()
        except (OSError, subprocess.SubprocessError):
            continue
        first_line = output.splitlines()[0].strip() if output else ""
        if first_line.isdigit():
            return int(first_line)
    return None


def _brand_from_name(name: str, kind: str) -> str:
    lower = name.lower()
    if "nvidia" in lower or "geforce" in lower or "rtx" in lower or "gtx" in lower:
        return "NVIDIA"
    if "amd" in lower or "radeon" in lower or "ryzen" in lower:
        return "AMD"
    if "intel" in lower or "core" in lower:
        return "Intel" if kind == "cpu" else "Intel"
    return ""


def _logo_url(kind: str, brand: str) -> str:
    if not brand:
        return ""
    for suffix in ("png", "jpg", "jpeg", "webp", "svg"):
        candidate = STATIC_ROOT / "logos" / kind / f"{brand}.{suffix}"
        if candidate.exists():
            version = int(candidate.stat().st_mtime)
            return f"/logos/{kind}/{brand}.{suffix}?v={version}"
    return ""


def get_hardware_info() -> dict[str, Any]:
    global HARDWARE_INFO
    if HARDWARE_INFO is not None:
        return HARDWARE_INFO

    cpu_name = _read_registry_cpu_name()
    gpu_info = _read_gpu_info()
    gpu_name = str(gpu_info.get("name") or "")
    cpu_brand = _brand_from_name(cpu_name, "cpu")
    gpu_brand = _brand_from_name(gpu_name, "gpu")
    HARDWARE_INFO = {
        "cpu": {
            "name": cpu_name or "CPU",
            "brand": cpu_brand,
            "logo": _logo_url("cpu", cpu_brand),
        },
        "gpu": {
            "name": gpu_name or "GPU",
            "brand": gpu_brand,
            "logo": _logo_url("gpu", gpu_brand),
            "memory_total_mb": gpu_info.get("memory_total_mb"),
        },
        "memory_total_mb": get_total_memory_mb(),
    }
    return HARDWARE_INFO


def format_mb(value_mb: float | int | None) -> str:
    if value_mb is None:
        return ""
    if value_mb >= 1024:
        return f"{value_mb / 1024:.1f} GB"
    return f"{round(value_mb)} MB"


def format_capacity_label(kind: str, total: str, language: str) -> str:
    if kind == "vram":
        return f"显存 {total} 占用" if language == "zh" else f"VRAM Usage {total}"
    return f"内存 {total} 占用" if language == "zh" else f"Memory Usage {total}"


def normalize_language(value: Any) -> str:
    return str(value).lower() if str(value).lower() in {"zh", "en"} else "zh"


def normalize_temperature_unit(value: Any) -> str:
    return str(value).upper() if str(value).upper() in {"C", "F"} else "C"


def localized_item(item: dict[str, Any], language: str) -> dict[str, str]:
    return {
        "id": item["id"],
        "label": item["label"].get(language, item["label"]["zh"]),
        "group": item["group"].get(language, item["group"]["zh"]),
    }


def ordered_display_items(config: dict[str, Any]) -> list[dict[str, Any]]:
    by_id = {item["id"]: item for item in DISPLAY_ITEMS}
    ordered: list[dict[str, Any]] = []
    for item_id in config.get("display_items", []):
        if item_id in by_id and by_id[item_id] not in ordered:
            ordered.append(by_id[item_id])
    for item in DISPLAY_ITEMS:
        if item not in ordered:
            ordered.append(item)
    return ordered


def format_temperature(celsius: float, unit: str) -> str:
    if unit == "F":
        return f"{celsius * 9 / 5 + 32:.0f} °F"
    return f"{celsius:.0f} °C"


def normalize_display_items(value: Any) -> list[str]:
    if value is None:
        return DEFAULT_DISPLAY_ITEMS.copy()
    if not isinstance(value, list):
        return DEFAULT_DISPLAY_ITEMS.copy()
    valid_ids = {item["id"] for item in DISPLAY_ITEMS}
    return [str(item) for item in value if str(item) in valid_ids]


def load_config() -> dict[str, Any]:
    defaults = {
        "host": "127.0.0.1",
        "port": 8765,
        "refresh_interval_ms": 1000,
        "fps_source": "afterburner",
        "fallback_to_rtss": False,
        "language": "zh",
        "temperature_unit": "C",
        "display_items": DEFAULT_DISPLAY_ITEMS,
        "sensor_labels": [],
    }
    if not CONFIG_PATH.exists():
        return defaults

    try:
        with CONFIG_PATH.open("r", encoding="utf-8") as file:
            loaded = json.load(file)
    except (OSError, json.JSONDecodeError):
        return defaults

    merged = {**defaults, **loaded}
    merged["language"] = normalize_language(merged.get("language"))
    merged["temperature_unit"] = normalize_temperature_unit(merged.get("temperature_unit"))
    if "display_items" in loaded:
        merged["display_items"] = normalize_display_items(loaded["display_items"])
    return merged


def save_config(config: dict[str, Any]) -> None:
    normalized = {
        "host": str(config.get("host") or "127.0.0.1"),
        "port": int(config.get("port") or 8765),
        "refresh_interval_ms": max(250, int(config.get("refresh_interval_ms") or 1000)),
        "fps_source": str(config.get("fps_source") or "afterburner"),
        "fallback_to_rtss": bool(config.get("fallback_to_rtss", False)),
        "language": normalize_language(config.get("language")),
        "temperature_unit": normalize_temperature_unit(config.get("temperature_unit")),
        "display_items": normalize_display_items(config["display_items"] if "display_items" in config else None),
        "sensor_labels": [str(label) for label in config.get("sensor_labels") or []],
    }
    with CONFIG_PATH.open("w", encoding="utf-8") as file:
        json.dump(normalized, file, indent=2, ensure_ascii=False)
        file.write("\n")


@dataclass
class Sensor:
    sensor_id: str
    label: str
    value: str
    raw_label: str = ""
    units: str = ""
    numeric_value: float | None = None


def _decode_c_string(data: bytes) -> str:
    return data.split(b"\x00", 1)[0].decode("mbcs", errors="ignore").strip()


def _format_afterburner_value(value: float, units: str, recommended_format: str) -> str:
    if not math.isfinite(value) or abs(value) >= 3.4028234663852886e38:
        return ""

    precision = 1
    match = re.search(r"%\.?(\d*)f", recommended_format)
    if match:
        precision = int(match.group(1) or "0")
    elif abs(value) >= 100:
        precision = 0

    formatted = f"{value:.{precision}f}"
    if "." in formatted:
        formatted = formatted.rstrip("0").rstrip(".")
    return f"{formatted} {units}".strip()


def read_afterburner_sensors() -> tuple[list[Sensor], str | None]:
    try:
        header = read_named_mapping("MAHMSharedMemory", 32)
    except SharedMemoryError as exc:
        return [], str(exc)

    if len(header) < 32:
        return [], "MSI Afterburner shared memory is too small"

    signature, version, header_size, entry_count, entry_size, updated_at = struct.unpack_from("<6I", header, 0)
    if signature == 0xDEAD:
        return [], "MSI Afterburner shared memory is marked as dead"
    if signature != 0x4D41484D:
        return [], "MSI Afterburner shared memory signature is invalid"
    if header_size < 24 or entry_size <= 0 or entry_count <= 0:
        return [], "MSI Afterburner hardware monitor table is empty"

    gpu_count = struct.unpack_from("<I", header, 24)[0] if len(header) >= 28 else 0
    gpu_entry_size = struct.unpack_from("<I", header, 28)[0] if len(header) >= 32 else 0
    mapping_size = header_size + entry_count * entry_size + gpu_count * gpu_entry_size
    try:
        raw = read_named_mapping("MAHMSharedMemory", mapping_size)
    except SharedMemoryError as exc:
        return [], str(exc)

    sensors: list[Sensor] = []
    max_path = 260
    minimum_entry_size = max_path * 5 + 4 * 6

    for index in range(entry_count):
        offset = header_size + index * entry_size
        if offset + min(entry_size, minimum_entry_size) > len(raw):
            break

        entry = raw[offset : offset + entry_size]
        src_name = _decode_c_string(entry[0:max_path])
        src_units = _decode_c_string(entry[max_path : max_path * 2])
        localized_name = _decode_c_string(entry[max_path * 2 : max_path * 3])
        localized_units = _decode_c_string(entry[max_path * 3 : max_path * 4])
        recommended_format = _decode_c_string(entry[max_path * 4 : max_path * 5])
        data_offset = max_path * 5

        if data_offset + 24 > len(entry):
            continue

        value = struct.unpack_from("<f", entry, data_offset)[0]
        flags = struct.unpack_from("<I", entry, data_offset + 12)[0]
        gpu_index = struct.unpack_from("<I", entry, data_offset + 16)[0]
        source_id = struct.unpack_from("<I", entry, data_offset + 20)[0]
        label = localized_name or src_name
        units = localized_units or src_units
        formatted_value = _format_afterburner_value(value, units, recommended_format)

        if label and formatted_value:
            sensor_id = f"mahm:{gpu_index}:{source_id}:{src_name or label}"
            raw_label = " ".join(part for part in (src_name, localized_name, units) if part)
            sensors.append(
                Sensor(
                    sensor_id=sensor_id,
                    label=label,
                    value=formatted_value,
                    raw_label=raw_label,
                    units=units,
                    numeric_value=value,
                )
            )

    if not sensors:
        return [], "No MSI Afterburner hardware monitor values were parsed"

    return sensors, None


def read_rtss_fps() -> tuple[dict[str, Any] | None, str | None]:
    try:
        header = read_named_mapping("RTSSSharedMemoryV2", 32)
    except SharedMemoryError as exc:
        return None, str(exc)

    if len(header) < 32:
        return None, "RTSS shared memory is too small"

    signature, version, app_entry_size, app_arr_offset, app_arr_size = struct.unpack_from("<5I", header, 0)
    if signature != 0x53535452:
        return None, "RTSS shared memory signature is invalid"
    if app_entry_size <= 0 or app_arr_offset <= 0 or app_arr_size <= 0:
        return None, "RTSS app table is empty"

    mapping_size = app_arr_offset + app_arr_size * app_entry_size
    try:
        raw = read_named_mapping("RTSSSharedMemoryV2", mapping_size)
    except SharedMemoryError as exc:
        return None, str(exc)

    best: dict[str, Any] | None = None
    now_ms = int(time.time() * 1000)

    for index in range(app_arr_size):
        offset = app_arr_offset + index * app_entry_size
        if offset + min(app_entry_size, 304) > len(raw):
            break

        process_id = struct.unpack_from("<I", raw, offset)[0]
        if process_id == 0:
            continue

        name = _decode_c_string(raw[offset + 4 : offset + 264])
        if not name:
            continue

        time_0, time_1, frames, frame_time = struct.unpack_from("<4I", raw, offset + 268)
        stat_time_0 = struct.unpack_from("<I", raw, offset + 288)[0] if offset + 292 <= len(raw) else 0
        stat_time_1 = struct.unpack_from("<I", raw, offset + 292)[0] if offset + 296 <= len(raw) else 0
        stat_frames = struct.unpack_from("<I", raw, offset + 296)[0] if offset + 300 <= len(raw) else 0

        fps = None
        if time_1 > time_0 and frames > 0:
            fps = round(frames * 1000.0 / (time_1 - time_0), 1)
        elif frame_time > 0:
            fps = round(1000000.0 / frame_time, 1)

        average_fps = None
        if stat_time_1 > stat_time_0 and stat_frames > 0:
            average_fps = round(stat_frames * 1000.0 / (stat_time_1 - stat_time_0), 1)

        low_1_percent_fps = None
        if fps is not None:
            history = FPS_HISTORY.setdefault(process_id, [])
            now = time.time()
            history.append((now, fps))
            cutoff = now - FPS_HISTORY_SECONDS
            FPS_HISTORY[process_id] = [(sample_time, sample_fps) for sample_time, sample_fps in history if sample_time >= cutoff]
            samples = [sample_fps for _, sample_fps in FPS_HISTORY[process_id]]
            if samples:
                low_count = max(1, math.ceil(len(samples) * 0.01))
                low_1_percent_fps = round(sum(sorted(samples)[:low_count]) / low_count, 1)
                if average_fps is None:
                    average_fps = round(sum(samples) / len(samples), 1)

        candidate = {
            "process_id": process_id,
            "name": os.path.basename(name),
            "fps": fps,
            "low_1_percent_fps": low_1_percent_fps,
            "average_fps": average_fps,
            "frame_time_ms": round(frame_time / 1000.0, 2) if frame_time else None,
            "updated_at_ms": time_1 or now_ms,
            "rtss_version": version,
        }
        if best is None or candidate["updated_at_ms"] > best["updated_at_ms"]:
            best = candidate

    if best is None:
        return None, "No active RTSS application was detected"

    return best, None


def _parse_mahm_ids(sensor: Sensor) -> tuple[int | None, int | None]:
    match = re.match(r"mahm:(\d+):(\d+):", sensor.sensor_id)
    if not match:
        return None, None
    return int(match.group(1)), int(match.group(2))


def _matches_rule(sensor: Sensor, metric_id: str, rule: list[list[str]]) -> bool:
    _, source_id = _parse_mahm_ids(sensor)
    if source_id in MAHM_SOURCE_IDS.get(metric_id, set()):
        return True

    haystack = f"{sensor.sensor_id} {sensor.label} {sensor.raw_label} {sensor.units}".lower()
    return all(any(token.lower() in haystack for token in group) for group in rule)


def _sensor_score(sensor: Sensor, metric_id: str) -> int:
    text = f"{sensor.sensor_id} {sensor.label} {sensor.raw_label}".lower()
    gpu_index, source_id = _parse_mahm_ids(sensor)
    score = 0
    if source_id in MAHM_SOURCE_IDS.get(metric_id, set()):
        score += 20
    if metric_id.startswith("cpu") and gpu_index == 0xFFFFFFFF:
        score += 12
    if metric_id.startswith("gpu") and gpu_index == 0:
        score += 6
    if "average" in text or "avg" in text:
        score -= 8
    if "per core" in text or re.search(r"\bcpu\s*\d+\b", text):
        score -= 4
    if "gpu memory" in text or "memory clock" in text:
        score -= 10
    if metric_id == "gpu_memory_frequency" and ("memory clock" in text or "显存频率" in text):
        score += 20
    if metric_id == "gpu_memory_usage" and ("memory usage" in text or "显存" in text or "vram" in text):
        score += 20
    if metric_id == "gpu_fan_speed":
        if "fan" in text or "风扇" in text:
            score += 10
        if "tachometer" in text or "rpm" in text or "转速" in text:
            score += 25
        if "%" in text and "rpm" not in text:
            score -= 4
        if "cpu" in text:
            score -= 20
    if metric_id == "memory_usage" and ("gpu" in text or "vram" in text or "显存" in text):
        score -= 20
    if metric_id.startswith("gpu") and ("gpu" in text or "core" in text):
        score += 3
    if metric_id.startswith("cpu") and "cpu" in text:
        score += 3
    if metric_id == "memory_usage" and ("ram" in text or "memory usage" in text):
        score += 3
    return score


def map_hardware_metrics(sensors: list[Sensor], config: dict[str, Any]) -> list[dict[str, Any]]:
    hardware_info = get_hardware_info()
    language = config["language"]
    temperature_unit = config["temperature_unit"]
    mapped: list[dict[str, Any]] = []
    for item in DISPLAY_ITEMS:
        metric_id = item["id"]
        if metric_id not in HARDWARE_ITEM_IDS:
            continue

        candidates = [sensor for sensor in sensors if _matches_rule(sensor, metric_id, SENSOR_RULES[metric_id])]
        candidates.sort(key=lambda sensor: _sensor_score(sensor, metric_id), reverse=True)
        best = candidates[0] if candidates else None
        label = item["label"].get(language, item["label"]["zh"])
        value = best.value if best else ""
        if metric_id in {"cpu_temperature", "gpu_temperature"} and best and best.numeric_value is not None:
            value = format_temperature(best.numeric_value, temperature_unit)
        if metric_id == "memory_usage" and best and best.numeric_value is not None:
            total_mb = hardware_info.get("memory_total_mb")
            used = format_mb(best.numeric_value)
            total = format_mb(total_mb)
            value = used
            if total:
                label = format_capacity_label("memory", total, language)
            else:
                label = item["label"].get(language, item["label"]["zh"])
        if metric_id == "gpu_memory_usage" and best and best.numeric_value is not None:
            total_mb = hardware_info.get("gpu", {}).get("memory_total_mb")
            used = format_mb(best.numeric_value)
            total = format_mb(total_mb)
            value = used
            if total:
                label = format_capacity_label("vram", total, language)
            else:
                label = item["label"].get(language, item["label"]["zh"])
        if metric_id not in {"memory_usage", "gpu_memory_usage"}:
            label = item["label"].get(language, item["label"]["zh"])

        mapped.append(
            {
                "id": metric_id,
                "label": label,
                "value": value,
                "source_label": best.label if best else "",
            }
        )
    return mapped


def _fps_sensor_text(sensor: Sensor) -> str:
    return f"{sensor.sensor_id} {sensor.label} {sensor.raw_label} {sensor.units}".lower()


def _is_frame_time_sensor(sensor: Sensor) -> bool:
    text = _fps_sensor_text(sensor)
    _, source_id = _parse_mahm_ids(sensor)
    return source_id == 0x51 or "frametime" in text or "frame time" in text or "帧生成" in text


def _is_fps_sensor(sensor: Sensor) -> bool:
    text = _fps_sensor_text(sensor)
    _, source_id = _parse_mahm_ids(sensor)
    if _is_frame_time_sensor(sensor):
        return False
    return source_id == 0x50 or "framerate" in text or "frame rate" in text or "fps" in text or "帧率" in text


def _pick_fps_sensor(sensors: list[Sensor], kind: str) -> Sensor | None:
    candidates = [sensor for sensor in sensors if _is_fps_sensor(sensor)]

    def score(sensor: Sensor) -> int:
        text = _fps_sensor_text(sensor)
        value = 0
        if kind == "current":
            value += 20
            if any(token in text for token in ("avg", "average", "平均", "1%", "low", "最低", "min", "max")):
                value -= 30
        elif kind == "average":
            if any(token in text for token in ("avg", "average", "平均")):
                value += 30
            if any(token in text for token in ("1%", "low", "最低", "min", "max")):
                value -= 20
        elif kind == "low_1_percent":
            if "1%" in text or "1 percent" in text or "1% low" in text:
                value += 35
            if "low" in text or "最低" in text:
                value += 20
            if "0.1" in text:
                value -= 10
            if any(token in text for token in ("avg", "average", "平均", "max")):
                value -= 20
        return value

    candidates.sort(key=score, reverse=True)
    return candidates[0] if candidates and score(candidates[0]) > 0 else None


def read_afterburner_fps(sensors: list[Sensor]) -> tuple[dict[str, Any] | None, str | None]:
    current = _pick_fps_sensor(sensors, "current")
    average = _pick_fps_sensor(sensors, "average")
    low_1_percent = _pick_fps_sensor(sensors, "low_1_percent")
    frame_time = next((sensor for sensor in sensors if _is_frame_time_sensor(sensor)), None)

    if not current and not average and not low_1_percent and not frame_time:
        return None, "MSI Afterburner framerate sensors are not available"

    fps_value = current.numeric_value if current else None
    average_value = average.numeric_value if average else None
    low_value = low_1_percent.numeric_value if low_1_percent else None
    frame_time_ms = frame_time.numeric_value if frame_time else None
    if (fps_value is None or fps_value <= 0) and frame_time_ms and frame_time_ms > 0:
        fps_value = 1000.0 / frame_time_ms

    if fps_value is not None and fps_value > 0:
        history = FPS_HISTORY.setdefault(0, [])
        now = time.time()
        history.append((now, fps_value))
        cutoff = now - FPS_HISTORY_SECONDS
        FPS_HISTORY[0] = [(sample_time, sample_fps) for sample_time, sample_fps in history if sample_time >= cutoff]
        samples = [sample_fps for _, sample_fps in FPS_HISTORY[0]]
        if samples:
            if average_value is None:
                average_value = sum(samples) / len(samples)
            if low_value is None:
                low_count = max(1, math.ceil(len(samples) * 0.01))
                low_value = sum(sorted(samples)[:low_count]) / low_count

    if fps_value is None and average_value is None and low_value is None and frame_time_ms is None:
        return None, "MSI Afterburner framerate sensors have no numeric values"

    return (
        {
            "name": "MSI Afterburner",
            "fps": round(fps_value, 1) if fps_value is not None else None,
            "low_1_percent_fps": round(low_value, 1) if low_value is not None else None,
            "average_fps": round(average_value, 1) if average_value is not None else None,
            "frame_time_ms": round(frame_time_ms, 2) if frame_time_ms is not None else None,
            "source": "MSI Afterburner",
            "source_labels": {
                "current": current.label if current else "",
                "low_1_percent": low_1_percent.label if low_1_percent else "",
                "average": average.label if average else "",
                "frame_time": frame_time.label if frame_time else "",
            },
        },
        None,
    )


def build_status() -> dict[str, Any]:
    config = load_config()
    sensors, sensors_error = read_afterburner_sensors()
    sensor_source = "MSI Afterburner"

    fps, fps_error = read_afterburner_fps(sensors)
    fps_source = "MSI Afterburner"
    if fps_error and config.get("fallback_to_rtss", False):
        fallback_fps, fallback_error = read_rtss_fps()
        if fallback_fps:
            fps = fallback_fps
            fps_error = None
            fps_source = "RTSS"
        else:
            fps_error = f"{fps_error}; RTSS fallback: {fallback_error}"

    enabled_items = set(config["display_items"] if "display_items" in config else DEFAULT_DISPLAY_ITEMS)
    all_hardware_metrics = map_hardware_metrics(sensors, config)
    metric_by_id = {sensor["id"]: sensor for sensor in all_hardware_metrics}
    visible_sensors = [
        metric_by_id[item_id]
        for item_id in config.get("display_items", DEFAULT_DISPLAY_ITEMS)
        if item_id in enabled_items and item_id in metric_by_id
    ]

    return {
        "updated_at": time.strftime("%Y-%m-%d %H:%M:%S"),
        "config": config,
        "available_items": [localized_item(item, config["language"]) for item in ordered_display_items(config)],
        "hardware_info": get_hardware_info(),
        "fps": fps,
        "fps_source": fps_source,
        "fps_error": fps_error,
        "sensors": visible_sensors,
        "hardware_metrics": all_hardware_metrics,
        "all_sensors": [sensor.__dict__ for sensor in sensors],
        "sensor_source": sensor_source,
        "sensors_error": sensors_error,
    }


class MonitorHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args: Any, **kwargs: Any) -> None:
        super().__init__(*args, directory=str(STATIC_ROOT), **kwargs)

    def log_message(self, format: str, *args: Any) -> None:
        print(f"{self.address_string()} - {format % args}")

    def _send_json(self, payload: Any, status: int = 200) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def end_headers(self) -> None:
        if not self.path.startswith("/api/"):
            self.send_header("Cache-Control", "no-store, max-age=0")
        super().end_headers()

    def do_GET(self) -> None:
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == "/api/status":
            self._send_json(build_status())
            return
        if parsed.path == "/api/config":
            self._send_json(load_config())
            return
        super().do_GET()

    def do_POST(self) -> None:
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path != "/api/config":
            self._send_json({"error": "Not found"}, 404)
            return

        try:
            length = int(self.headers.get("Content-Length", "0"))
            body = self.rfile.read(length)
            incoming = json.loads(body.decode("utf-8"))
            config = load_config()
            config.update(incoming)
            save_config(config)
        except (ValueError, OSError, json.JSONDecodeError) as exc:
            self._send_json({"error": str(exc)}, 400)
            return

        self._send_json(load_config())


def main() -> None:
    config = load_config()
    host = str(config.get("host") or "127.0.0.1")
    port = int(config.get("port") or 8765)
    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer((host, port), MonitorHandler) as httpd:
        print(f"Game Monitor running at http://{host}:{port}")
        httpd.serve_forever()


if __name__ == "__main__":
    main()
