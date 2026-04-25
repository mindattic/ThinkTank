window.ThinkTank = window.ThinkTank || {};

window.ThinkTank.setTheme = (mode) => {
    const root = document.documentElement;
    root.setAttribute('data-theme', mode);
};

window.ThinkTank.setControlHeight = (px) => {
    document.documentElement.style.setProperty('--control-height', px + 'px');
};

window.ThinkTank.setGutter = (px) => {
    document.documentElement.style.setProperty('--gutter', px + 'px');
};

window.ThinkTank.setBorderRadius = (px) => {
    document.documentElement.style.setProperty('--radius', px + 'px');
};

window.ThinkTank.isNearBottom = (el, thresholdPx) => {
    if (!el) return true;
    const threshold = thresholdPx ?? 60;
    return (el.scrollTop + el.clientHeight) >= (el.scrollHeight - threshold);
};

window.ThinkTank.scrollToBottom = (el) => {
    if (!el) return;
    el.scrollTop = el.scrollHeight;
};

window.ThinkTank.blurActive = () => {
    if (document.activeElement) document.activeElement.blur();
};

window.ThinkTank.downloadFile = (filename, content) => {
    const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};
