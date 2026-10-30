window.tcsMountTelegramLogin = function (containerId, botUsername, authUrl) {
    var container = document.getElementById(containerId);
    if (!container || container.dataset.mounted === '1' || !botUsername) return;
    container.dataset.mounted = '1';
    var script = document.createElement('script');
    script.async = true;
    script.src = 'https://telegram.org/js/telegram-widget.js?22';
    script.setAttribute('data-telegram-login', botUsername);
    script.setAttribute('data-size', 'large');
    script.setAttribute('data-userpic', 'false');
    script.setAttribute('data-request-access', 'write');
    script.setAttribute('data-auth-url', authUrl);
    container.appendChild(script);
};
