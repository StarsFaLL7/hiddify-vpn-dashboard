// Копирование текста в буфер обмена для админ-панели.
window.vpnCopy = async (text) => {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
};
