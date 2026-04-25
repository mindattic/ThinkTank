window.LLMThinkTank = window.LLMThinkTank || {};

window.LLMThinkTank.setTheme = (mode) => {
    const root = document.documentElement;
    root.setAttribute('data-theme', mode);
};

window.LLMThinkTank.setControlHeight = (px) => {
    document.documentElement.style.setProperty('--control-height', px + 'px');
};

window.LLMThinkTank.setGutter = (px) => {
    document.documentElement.style.setProperty('--gutter', px + 'px');
};

window.LLMThinkTank.setBorderRadius = (px) => {
    document.documentElement.style.setProperty('--radius', px + 'px');
};

window.LLMThinkTank.isNearBottom = (el, thresholdPx) => {
    if (!el) return true;
    const threshold = thresholdPx ?? 60;
    return (el.scrollTop + el.clientHeight) >= (el.scrollHeight - threshold);
};

window.LLMThinkTank.scrollToBottom = (el) => {
    if (!el) return;
    el.scrollTop = el.scrollHeight;
};

window.LLMThinkTank.blurActive = () => {
    if (document.activeElement) document.activeElement.blur();
};

window.LLMThinkTank.downloadFile = (filename, content) => {
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
