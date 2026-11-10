const STORAGE_KEY = "phylodivas.accessibility";
const DEFAULTS = {
    fontSize: 16,
    colorMode: "default",
    colorBlindMode: "none",
    reducedMotion: false,
    altTextMode: false,
};

const readSettings = () => {
    try {
        return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(STORAGE_KEY) || "{}") };
    } catch {
        return { ...DEFAULTS };
    }
};

let settings = readSettings();
const saveSettings = () => localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));

const setButtonState = (id, active, activeLabel, inactiveLabel) => {
    const button = document.getElementById(id);
    if (!button) return;
    button.setAttribute("aria-pressed", String(active));
    button.textContent = active ? activeLabel : inactiveLabel;
};

const applyAltTextMode = (enabled) => {
    if (enabled) {
        document.querySelectorAll("img[alt], canvas[aria-label], svg[aria-label]").forEach((element) => {
            if (element.dataset.altReplaced === "true") return;
            const description = element.getAttribute("alt") || element.getAttribute("aria-label");
            if (!description) return;
            const placeholder = document.createElement("div");
            placeholder.className = "alt-text-placeholder";
            placeholder.textContent = description;
            placeholder.dataset.altFor = "true";
            element.dataset.previousDisplay = element.style.display || "";
            element.style.display = "none";
            element.dataset.altReplaced = "true";
            element.insertAdjacentElement("afterend", placeholder);
        });
    } else {
        document.querySelectorAll('[data-alt-replaced="true"]').forEach((element) => {
            element.style.display = element.dataset.previousDisplay || "";
            delete element.dataset.previousDisplay;
            delete element.dataset.altReplaced;
            const next = element.nextElementSibling;
            if (next?.dataset.altFor === "true") next.remove();
        });
    }
};

const applySettings = () => {
    const body = document.body;
    document.documentElement.style.fontSize = `${settings.fontSize}px`;
    body.classList.toggle("dark-mode", settings.colorMode === "dark");
    body.classList.toggle("high-contrast", settings.colorMode === "contrast");
    body.classList.toggle("reduced-motion", settings.reducedMotion);
    body.classList.remove("cb-protanopia", "cb-deuteranopia", "cb-tritanopia");
    if (settings.colorBlindMode !== "none") body.classList.add(`cb-${settings.colorBlindMode}`);

    const select = document.getElementById("colorBlindSelect");
    if (select) select.value = settings.colorBlindMode;
    setButtonState("darkModeBtn", settings.colorMode === "dark", "Use Light Mode", "Use Dark Mode");
    setButtonState("contrastModeBtn", settings.colorMode === "contrast", "Disable High Contrast", "Enable High Contrast");
    setButtonState("motionModeBtn", settings.reducedMotion, "Restore Motion", "Reduce Motion");
    setButtonState("altTextModeBtn", settings.altTextMode, "Show Visuals", "Show Text Alternatives");
    applyAltTextMode(settings.altTextMode);
};

function hookAccessibilityButton() {
    const toggleButton = document.getElementById("accessibilityToggle");
    const panel = document.getElementById("accessibilityPanel");
    if (!toggleButton || !panel) {
        setTimeout(hookAccessibilityButton, 100);
        return;
    }
    toggleButton.onclick = () => {
        const open = panel.hidden;
        panel.hidden = !open;
        toggleButton.setAttribute("aria-expanded", String(open));
    };
    applySettings();
}

window.increaseFont = () => {
    settings.fontSize = Math.min(22, settings.fontSize + 1);
    saveSettings();
    applySettings();
};

window.decreaseFont = () => {
    settings.fontSize = Math.max(12, settings.fontSize - 1);
    saveSettings();
    applySettings();
};

window.toggleContrast = () => {
    settings.colorMode = settings.colorMode === "contrast" ? "default" : "contrast";
    saveSettings();
    applySettings();
};

window.toggleDarkMode = () => {
    settings.colorMode = settings.colorMode === "dark" ? "default" : "dark";
    saveSettings();
    applySettings();
};

window.applyColorBlindMode = () => {
    settings.colorBlindMode = document.getElementById("colorBlindSelect")?.value || "none";
    saveSettings();
    applySettings();
};

window.toggleReducedMotion = () => {
    settings.reducedMotion = !settings.reducedMotion;
    saveSettings();
    applySettings();
};

window.toggleAltTextMode = () => {
    settings.altTextMode = !settings.altTextMode;
    saveSettings();
    applySettings();
};

window.resetAccessibility = () => {
    settings = { ...DEFAULTS };
    saveSettings();
    applySettings();
};

hookAccessibilityButton();
