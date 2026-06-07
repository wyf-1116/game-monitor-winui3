const state = {
  config: null,
  availableItems: [],
  settingsDirty: false,
  timer: null,
  draggingOption: null,
  dragStartOrder: "",
};

const elements = {
  cpuName: document.querySelector("#cpuName"),
  gpuName: document.querySelector("#gpuName"),
  cpuLogo: document.querySelector("#cpuLogo"),
  gpuLogo: document.querySelector("#gpuLogo"),
  fpsValue: document.querySelector("#fpsValue"),
  frameTime: document.querySelector("#frameTime"),
  lowFps: document.querySelector("#lowFps"),
  avgFps: document.querySelector("#avgFps"),
  sensorGrid: document.querySelector("#sensorGrid"),
  updatedAt: document.querySelector("#updatedAt"),
  sourceStatus: document.querySelector("#sourceStatus"),
  settingsButton: document.querySelector("#settingsButton"),
  settingsPanel: document.querySelector("#settingsPanel"),
  closeSettings: document.querySelector("#closeSettings"),
  settingsTitle: document.querySelector("#settingsTitle"),
  refreshLabel: document.querySelector("#refreshLabel"),
  refreshInterval: document.querySelector("#refreshInterval"),
  temperatureLabel: document.querySelector("#temperatureLabel"),
  temperatureUnit: document.querySelector("#temperatureUnit"),
  languageLabel: document.querySelector("#languageLabel"),
  languageSelect: document.querySelector("#languageSelect"),
  displayItemsLabel: document.querySelector("#displayItemsLabel"),
  sensorOptions: document.querySelector("#sensorOptions"),
  selectAllSensors: document.querySelector("#selectAllSensors"),
  saveSettings: document.querySelector("#saveSettings"),
};

const I18N = {
  zh: {
    settings: "设置",
    refresh: "刷新间隔（毫秒）",
    temperature: "温度单位",
    language: "语言",
    displayItems: "显示项目",
    selectAll: "全选",
    save: "保存",
    hidden: "已隐藏",
    hardware: "硬件信息",
    frameTime: "帧生成",
    average: "平均",
    connected: "正常",
    saveFailed: "保存设置失败",
    backendFailed: "连接后端失败",
  },
  en: {
    settings: "Settings",
    refresh: "Refresh interval (ms)",
    temperature: "Temperature unit",
    language: "Language",
    displayItems: "Display items",
    selectAll: "Select all",
    save: "Save",
    hidden: "Hidden",
    hardware: "Hardware",
    frameTime: "Frame Time",
    average: "Average",
    connected: "OK",
    saveFailed: "Save failed",
    backendFailed: "Backend connection failed",
  },
};

function language() {
  if (elements.settingsPanel?.classList.contains("open") && state.settingsDirty) {
    return elements.languageSelect?.value === "en" ? "en" : "zh";
  }
  return state.config?.language === "en" ? "en" : "zh";
}

function text(key) {
  return I18N[language()][key];
}

function renderStaticText() {
  elements.settingsTitle.textContent = text("settings");
  elements.refreshLabel.textContent = text("refresh");
  elements.temperatureLabel.textContent = text("temperature");
  elements.languageLabel.textContent = text("language");
  elements.displayItemsLabel.textContent = text("displayItems");
  elements.selectAllSensors.textContent = text("selectAll");
  elements.saveSettings.textContent = text("save");
}

function formatValue(value) {
  if (value === null || value === undefined || value === "") {
    return "--";
  }
  return String(value);
}

function renderLogo(element, src) {
  if (!src) {
    element.hidden = true;
    element.removeAttribute("src");
    return;
  }
  element.hidden = false;
  element.src = src;
}

function renderHardwareInfo(info) {
  const cpu = info?.cpu || {};
  const gpu = info?.gpu || {};
  elements.cpuName.textContent = cpu.name || "CPU";
  elements.gpuName.textContent = gpu.name || "GPU";
  renderLogo(elements.cpuLogo, cpu.logo);
  renderLogo(elements.gpuLogo, gpu.logo);
}

function renderFps(status) {
  const fps = status.fps;
  const enabled = new Set(status.config?.display_items || []);
  const showCurrent = enabled.has("fps_current");
  const showFrameTime = enabled.has("frame_time");
  const showLow = enabled.has("fps_1_low");
  const showAverage = enabled.has("fps_average");

  elements.fpsValue.closest(".fps-value").hidden = !showCurrent;
  elements.frameTime.hidden = !showFrameTime;
  elements.lowFps.hidden = !showLow;
  elements.avgFps.hidden = !showAverage;

  if (!fps) {
    elements.fpsValue.textContent = "--";
    elements.frameTime.textContent = `${text("frameTime")} --`;
    elements.lowFps.textContent = "1% Low --";
    elements.avgFps.textContent = `${text("average")} --`;
    return;
  }

  elements.fpsValue.textContent = fps.fps === null || fps.fps === undefined ? "--" : Math.round(fps.fps);
  elements.frameTime.textContent = `${text("frameTime")} ${formatValue(fps.frame_time_ms)} ms`;
  elements.lowFps.textContent = `1% Low ${formatValue(fps.low_1_percent_fps)} FPS`;
  elements.avgFps.textContent = `${text("average")} ${formatValue(fps.average_fps)} FPS`;
}

function renderSensors(sensors) {
  elements.sensorGrid.replaceChildren();

  if (!sensors.length) {
    const empty = document.createElement("article");
    empty.className = "sensor-card";
    empty.innerHTML = `<div class="sensor-label">${text("hardware")}</div><div class="sensor-value">${text("hidden")}</div>`;
    elements.sensorGrid.append(empty);
    return;
  }

  for (const sensor of sensors) {
    const card = document.createElement("article");
    card.className = "sensor-card";

    const label = document.createElement("div");
    label.className = "sensor-label";
    label.textContent = sensor.label;

    const value = document.createElement("div");
    value.className = "sensor-value";
    value.textContent = formatValue(sensor.value);

    card.append(label, value);
    elements.sensorGrid.append(card);
  }
}

function fitLayout() {
  const screen = document.querySelector(".screen");
  const primaryPanel = document.querySelector(".primary-panel");
  const fpsPanel = document.querySelector(".fps-panel");
  const visibleCards = elements.sensorGrid.querySelectorAll(".sensor-card").length;
  const root = document.documentElement;
  const isLandscape = window.innerWidth / window.innerHeight > 1;
  screen.dataset.layout = isLandscape ? "landscape" : "portrait";

  root.style.removeProperty("--ui-scale");
  root.style.removeProperty("--fps-scale");
  root.style.removeProperty("--card-scale");
  root.style.removeProperty("--device-name-scale");
  root.style.removeProperty("--sensor-label-scale");
  root.style.removeProperty("--card-row-height");

  const sensorWidth = elements.sensorGrid.clientWidth || window.innerWidth;
  const sensorHeight = elements.sensorGrid.clientHeight || window.innerHeight;
  const useThreeColumns = visibleCards >= 7 && sensorWidth >= (isLandscape ? 720 : 760);
  elements.sensorGrid.dataset.columns = useThreeColumns ? "3" : "2";

  const columnCount = useThreeColumns ? 3 : 2;
  const rowCount = Math.max(1, Math.ceil(visibleCards / columnCount));
  const clamp = (value, min, max) => Math.max(min, Math.min(max, value));
  const layoutWidth = isLandscape ? primaryPanel.clientWidth : window.innerWidth;
  const layoutHeight = isLandscape ? primaryPanel.clientHeight : window.innerHeight;
  const viewportScale = clamp(Math.min(layoutHeight / 1272, layoutWidth / 916), 0.34, 1.6);
  const availableCardHeight = Math.max(1, sensorHeight - Math.max(0, rowCount - 1) * 18);
  const preferredRowHeight = clamp(
    isLandscape ? availableCardHeight / rowCount : window.innerHeight * 0.1,
    92,
    220,
  );
  const preferredCardScale = clamp(preferredRowHeight / 128, 0.72, 1.45);
  const preferredFpsScale = clamp(layoutHeight / 1272, 0.52, 1.55);

  const setScale = (uiScale, fpsScale, cardScale, rowHeight) => {
    root.style.setProperty("--ui-scale", String(uiScale));
    root.style.setProperty("--fps-scale", String(fpsScale));
    root.style.setProperty("--card-scale", String(cardScale));
    root.style.setProperty("--card-row-height", `${Math.round(rowHeight)}px`);

    if (isLandscape) {
      root.style.setProperty("--device-name-scale", "1");
      root.style.setProperty("--sensor-label-scale", "1");

      const fitSingleLineGroup = (selector, property, minimumScale) => {
        const items = [...document.querySelectorAll(selector)];
        const requiredScale = items.reduce((scale, item) => {
          if (item.scrollWidth <= item.clientWidth) {
            return scale;
          }
          return Math.min(scale, (item.clientWidth / item.scrollWidth) * 0.98);
        }, 1);
        root.style.setProperty(property, String(clamp(requiredScale, minimumScale, 1)));
      };

      fitSingleLineGroup(".device-row h1", "--device-name-scale", 0.62);
      fitSingleLineGroup(".sensor-label", "--sensor-label-scale", 0.58);
    }
  };

  setScale(Math.min(1, viewportScale), preferredFpsScale, preferredCardScale, preferredRowHeight);

  const fits = () => {
    const labels = [...elements.sensorGrid.querySelectorAll(".sensor-label")];
    const values = [...elements.sensorGrid.querySelectorAll(".sensor-value")];
    const allLabelsVisible = labels.every((label) => label.getBoundingClientRect().height >= 10);
    const allValuesVisible = values.every((value) => value.getBoundingClientRect().height >= 18);
    return (
      allLabelsVisible &&
      allValuesVisible &&
      screen.scrollHeight <= window.innerHeight &&
      screen.scrollWidth <= window.innerWidth &&
      elements.sensorGrid.scrollHeight <= elements.sensorGrid.clientHeight + 1 &&
      fpsPanel.scrollHeight <= fpsPanel.clientHeight + 1 &&
      fpsPanel.clientHeight >= (isLandscape ? 90 : 120)
    );
  };

  const uiSteps = [Math.min(1, viewportScale), 1, 0.94, 0.88, 0.82, 0.76, 0.7, 0.64, 0.58, 0.52, 0.46, 0.4, 0.34]
    .filter((scale, index, values) => scale <= 1 && scale >= 0.34 && values.indexOf(scale) === index)
    .sort((a, b) => b - a);
  const rowSteps = [preferredRowHeight, 220, 200, 184, 168, 152, 136, 124, 112, 100, 92, 82, 72]
    .filter((height, index, values) => height >= 72 && height <= preferredRowHeight && values.indexOf(height) === index)
    .sort((a, b) => b - a);

  for (const uiScale of uiSteps) {
    for (const rowHeight of rowSteps) {
      const cardScale = clamp((rowHeight / 128) * uiScale, 0.62, preferredCardScale);
      for (const fpsScale of [preferredFpsScale, 1.3, 1.15, 1, 0.9, 0.8, 0.7, 0.6]) {
        setScale(uiScale, fpsScale, cardScale, rowHeight * uiScale);
        if (fits()) {
          return;
        }
      }
    }
  }

  setScale(0.34, isLandscape ? 0.48 : 0.6, 0.62, isLandscape ? 72 : 92);
  if (!fits() && visibleCards >= 9) {
    elements.sensorGrid.dataset.columns = "3";
    setScale(0.34, 0.52, 0.62, 92);
  }
}

function renderStatusLine(status) {
  const errors = [status.sensors_error, status.fps_error].filter(Boolean);
  elements.updatedAt.textContent = status.updated_at || "--";
  const source = status.sensor_source || "MSI Afterburner";
  const fpsSource = status.fps_source || "MSI Afterburner";
  elements.sourceStatus.textContent = errors.length ? errors.join(" | ") : `${source} / ${fpsSource} ${text("connected")}`;
}

function renderSensorOptions() {
  const selected = new Set(state.config?.display_items || []);
  elements.sensorOptions.replaceChildren();

  let currentGroup = "";
  for (const item of state.availableItems) {
    if (item.group !== currentGroup) {
      currentGroup = item.group;
      const heading = document.createElement("div");
      heading.className = "option-group";
      heading.textContent = currentGroup;
      elements.sensorOptions.append(heading);
    }

    const id = `display-${item.id}`;
    const label = document.createElement("label");
    label.className = "sensor-option";
    const isHardware = !["fps_current", "frame_time", "fps_1_low", "fps_average"].includes(item.id);
    label.dataset.itemId = item.id;
    label.dataset.kind = isHardware ? "hardware" : "fps";

    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.id = id;
    checkbox.value = item.id;
    checkbox.checked = selected.has(item.id);

    const dragHandle = document.createElement("span");
    dragHandle.className = "drag-handle";
    dragHandle.textContent = isHardware ? "↕" : "";

    const text = document.createElement("span");
    text.className = "option-text";
    text.textContent = item.label;

    label.append(checkbox, dragHandle, text);
    elements.sensorOptions.append(label);
  }
}

function markSettingsDirty() {
  state.settingsDirty = true;
}

function hardwareOrder() {
  return [...elements.sensorOptions.querySelectorAll(".sensor-option[data-kind='hardware']")]
    .map((option) => option.dataset.itemId)
    .join("|");
}

function beginSensorDrag(event) {
  const handle = event.target.closest(".drag-handle");
  const option = handle?.closest(".sensor-option[data-kind='hardware']");
  if (!option || event.button !== 0) {
    return;
  }

  event.preventDefault();
  state.draggingOption = option;
  state.dragStartOrder = hardwareOrder();
  state.settingsDirty = true;
  option.classList.add("dragging");
  elements.sensorOptions.classList.add("drag-active");
  handle.setPointerCapture(event.pointerId);
}

function moveSensorDrag(event) {
  if (!state.draggingOption) {
    return;
  }

  event.preventDefault();
  const targetY = event.clientY;
  const siblings = [...elements.sensorOptions.querySelectorAll(".sensor-option[data-kind='hardware']")].filter(
    (option) => option !== state.draggingOption,
  );
  const next = siblings.find((option) => targetY < option.getBoundingClientRect().top + option.offsetHeight / 2);

  if (next) {
    elements.sensorOptions.insertBefore(state.draggingOption, next);
  } else {
    elements.sensorOptions.append(state.draggingOption);
  }
}

function endSensorDrag(event) {
  if (!state.draggingOption) {
    return;
  }

  const handle = event.target.closest(".drag-handle");
  if (handle?.hasPointerCapture(event.pointerId)) {
    handle.releasePointerCapture(event.pointerId);
  }
  state.draggingOption.classList.remove("dragging");
  elements.sensorOptions.classList.remove("drag-active");
  if (hardwareOrder() !== state.dragStartOrder) {
    markSettingsDirty();
  }
  state.draggingOption = null;
  state.dragStartOrder = "";
}

async function fetchStatus() {
  const response = await fetch("/api/status", { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }
  return response.json();
}

function scheduleNextPoll() {
  clearTimeout(state.timer);
  const interval = Math.max(250, Number(state.config?.refresh_interval_ms || 1000));
  state.timer = setTimeout(update, interval);
}

async function update() {
  try {
    const status = await fetchStatus();
    state.config = status.config;
    state.availableItems = status.available_items || [];
    if (!elements.settingsPanel.classList.contains("open") || !state.settingsDirty) {
      elements.refreshInterval.value = state.config.refresh_interval_ms;
      elements.temperatureUnit.value = state.config.temperature_unit || "C";
      elements.languageSelect.value = state.config.language || "zh";
    }
    renderStaticText();
    renderHardwareInfo(status.hardware_info);
    renderFps(status);
    renderSensors(status.sensors || []);
    renderStatusLine(status);
    fitLayout();
    if (elements.settingsPanel.classList.contains("open") && !state.settingsDirty) {
      renderSensorOptions();
    }
  } catch (error) {
    elements.sourceStatus.textContent = `${text("backendFailed")}: ${error.message}`;
  } finally {
    scheduleNextPoll();
  }
}

async function saveSettings() {
  const selected = [...elements.sensorOptions.querySelectorAll("input:checked")].map((input) => input.value);
  const payload = {
    refresh_interval_ms: Number(elements.refreshInterval.value || 1000),
    temperature_unit: elements.temperatureUnit.value,
    language: elements.languageSelect.value,
    display_items: selected,
  };

  const response = await fetch("/api/config", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }

  state.config = await response.json();
  state.settingsDirty = false;
  elements.settingsPanel.classList.remove("open");
  await update();
}

elements.settingsButton.addEventListener("click", () => {
  elements.settingsPanel.classList.add("open");
  elements.settingsPanel.setAttribute("aria-hidden", "false");
  state.settingsDirty = false;
  elements.refreshInterval.value = state.config?.refresh_interval_ms || 1000;
  elements.temperatureUnit.value = state.config?.temperature_unit || "C";
  elements.languageSelect.value = state.config?.language || "zh";
  renderSensorOptions();
});

elements.closeSettings.addEventListener("click", () => {
  elements.settingsPanel.classList.remove("open");
  elements.settingsPanel.setAttribute("aria-hidden", "true");
});

elements.selectAllSensors.addEventListener("click", () => {
  for (const input of elements.sensorOptions.querySelectorAll("input")) {
    input.checked = true;
  }
  markSettingsDirty();
});

elements.refreshInterval.addEventListener("input", markSettingsDirty);
elements.temperatureUnit.addEventListener("change", markSettingsDirty);
elements.languageSelect.addEventListener("change", () => {
  markSettingsDirty();
  state.config = { ...state.config, language: elements.languageSelect.value };
  renderStaticText();
});
elements.languageSelect.addEventListener("input", () => {
  markSettingsDirty();
  state.config = { ...state.config, language: elements.languageSelect.value };
  renderStaticText();
});
elements.sensorOptions.addEventListener("change", markSettingsDirty);

elements.sensorOptions.addEventListener("pointerdown", beginSensorDrag);
elements.sensorOptions.addEventListener("pointermove", moveSensorDrag);
elements.sensorOptions.addEventListener("pointerup", endSensorDrag);
elements.sensorOptions.addEventListener("pointercancel", endSensorDrag);

elements.saveSettings.addEventListener("click", () => {
  saveSettings().catch((error) => {
    elements.sourceStatus.textContent = `${text("saveFailed")}: ${error.message}`;
  });
});

window.addEventListener("resize", fitLayout);

fitLayout();
update();
