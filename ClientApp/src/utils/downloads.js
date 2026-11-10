const clickBlob = (blob, filename) => {
    const url = window.URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.URL.revokeObjectURL(url);
};

export const downloadText = (content, filename, type = "text/plain;charset=utf-8") => {
    clickBlob(new Blob([content], { type }), filename);
};

export const downloadJson = (value, filename) => {
    downloadText(JSON.stringify(value, null, 2), filename, "application/json;charset=utf-8");
};

const serializedSvg = (svgElement) => {
    const clone = svgElement.cloneNode(true);
    clone.setAttribute("xmlns", "http://www.w3.org/2000/svg");
    clone.setAttribute("xmlns:xlink", "http://www.w3.org/1999/xlink");
    const styles = window.getComputedStyle(svgElement);
    clone.style.fontFamily = styles.fontFamily || "Arial, sans-serif";
    return new XMLSerializer().serializeToString(clone);
};

export const downloadSvg = (svgElement, filename) => {
    if (!svgElement) return;
    downloadText(serializedSvg(svgElement), filename, "image/svg+xml;charset=utf-8");
};

export const downloadSvgPng = (svgElement, filename, scale = 2) => {
    if (!svgElement) return;
    const viewBox = svgElement.viewBox?.baseVal;
    const width = Math.max(1, Math.round(viewBox?.width || svgElement.clientWidth || 1000));
    const height = Math.max(1, Math.round(viewBox?.height || svgElement.clientHeight || 600));
    const canvas = document.createElement("canvas");
    canvas.width = width * scale;
    canvas.height = height * scale;
    const context = canvas.getContext("2d");
    if (!context) return;
    context.scale(scale, scale);
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, width, height);
    const image = new Image();
    const svgBlob = new Blob([serializedSvg(svgElement)], { type: "image/svg+xml;charset=utf-8" });
    const url = window.URL.createObjectURL(svgBlob);
    image.onload = () => {
        context.drawImage(image, 0, 0, width, height);
        window.URL.revokeObjectURL(url);
        canvas.toBlob((blob) => {
            if (blob) clickBlob(blob, filename);
        }, "image/png");
    };
    image.onerror = () => window.URL.revokeObjectURL(url);
    image.src = url;
};

