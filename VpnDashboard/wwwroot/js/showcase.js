// Витрина: копирование ссылок в буфер обмена. QR генерируется на сервере.
(function () {
    function flash(button, text) {
        const label = button.querySelector('.sc-copy-label') || button;
        const original = label.textContent;
        label.textContent = text;
        button.classList.add('is-copied');
        setTimeout(function () {
            label.textContent = original;
            button.classList.remove('is-copied');
        }, 1600);
    }

    document.addEventListener('click', async function (e) {
        const button = e.target.closest('[data-copy]');
        if (!button) return;
        const text = button.getAttribute('data-copy');
        try {
            await navigator.clipboard.writeText(text);
            flash(button, 'Скопировано ✓');
        } catch {
            flash(button, 'Не удалось');
        }
    });
})();
