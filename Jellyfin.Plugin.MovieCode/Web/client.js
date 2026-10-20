(function () {
    'use strict';

    // Wird beim Laden des Jellyfin-Web-Clients eingebunden. Das Skript hängt sich
    // vor die PlaybackInfo-Anfrage und zeigt bei Bedarf das Code-Popup an.
    if (window.__movieCodeClientLoaded) {
        return;
    }

    window.__movieCodeClientLoaded = true;

    var pending = {};
    var stylesInjected = false;

    function getApi() {
        return window.ApiClient && typeof window.ApiClient.getUrl === 'function'
            ? window.ApiClient
            : null;
    }

    function isLoggedIn() {
        var api = getApi();
        return !!(api && typeof api.accessToken === 'function' && api.accessToken());
    }

    function apiGet(path, params) {
        var api = getApi();
        return api.ajax({
            type: 'GET',
            url: api.getUrl(path, params),
            dataType: 'json'
        });
    }

    function apiPost(path, body) {
        var api = getApi();
        return api.ajax({
            type: 'POST',
            url: api.getUrl(path),
            data: JSON.stringify(body),
            contentType: 'application/json',
            dataType: 'json'
        });
    }

    function ensureStyles() {
        if (stylesInjected || document.getElementById('movie-code-styles')) {
            return;
        }

        var style = document.createElement('style');
        style.id = 'movie-code-styles';
        style.textContent = [
            '.mc-overlay{position:fixed;inset:0;z-index:2147483647;display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,.72);padding:20px;box-sizing:border-box;font-family:inherit;}',
            '.mc-dialog{width:100%;max-width:430px;background:#1f1f23;color:#fff;border:1px solid rgba(255,255,255,.14);border-radius:14px;box-shadow:0 24px 80px rgba(0,0,0,.6);padding:26px;box-sizing:border-box;}',
            '.mc-title{font-size:1.35rem;font-weight:600;margin:0 0 .4rem;color:#fff;}',
            '.mc-text{font-size:.98rem;line-height:1.5;opacity:.86;margin:0 0 1.1rem;color:#fff;}',
            '.mc-input{width:100%;box-sizing:border-box;background:#2b2b31;color:#fff;border:1px solid #4a4a52;border-radius:10px;padding:.65rem .75rem;font-size:2rem;letter-spacing:.35em;text-align:center;font-family:Consolas,monospace;outline:none;}',
            '.mc-input:focus{border-color:#00a4dc;box-shadow:0 0 0 3px rgba(0,164,220,.22);}',
            '.mc-error{min-height:1.4rem;color:#ff7b72;font-size:.9rem;margin-top:.5rem;}',
            '.mc-actions{display:flex;gap:.7rem;justify-content:flex-end;margin-top:.9rem;flex-wrap:wrap;}',
            '.mc-btn{appearance:none;border:0;border-radius:9px;padding:.65rem 1.15rem;font-size:.95rem;font-weight:600;cursor:pointer;}',
            '.mc-btn-secondary{background:#3a3a42;color:#fff;}',
            '.mc-btn-primary{background:#00a4dc;color:#fff;}',
            '.mc-btn:disabled{opacity:.55;cursor:default;}',
            '.mc-toast{position:fixed;left:50%;bottom:28px;transform:translateX(-50%);z-index:2147483647;background:#2b2b31;color:#fff;border:1px solid rgba(255,255,255,.18);border-radius:10px;padding:.8rem 1.1rem;box-shadow:0 10px 30px rgba(0,0,0,.45);font-size:.95rem;max-width:90vw;text-align:center;}'
        ].join('');

        document.head.appendChild(style);
        stylesInjected = true;
    }

    function createElement(tagName, className, text) {
        var element = document.createElement(tagName);
        if (className) {
            element.className = className;
        }

        if (text !== undefined) {
            element.textContent = text;
        }

        return element;
    }

    function createOverlay() {
        ensureStyles();

        var previousOverflow = document.body.style.overflow;
        var overlay = createElement('div', 'mc-overlay');
        var dialog = createElement('div', 'mc-dialog');
        overlay.appendChild(dialog);
        document.body.appendChild(overlay);
        document.body.style.overflow = 'hidden';

        return {
            overlay: overlay,
            dialog: dialog,
            close: function () {
                if (overlay.parentNode) {
                    overlay.parentNode.removeChild(overlay);
                }

                document.body.style.overflow = previousOverflow;
            }
        };
    }

    function showToast(message) {
        ensureStyles();

        var toast = createElement('div', 'mc-toast', message);
        document.body.appendChild(toast);

        window.setTimeout(function () {
            toast.style.transition = 'opacity .35s ease';
            toast.style.opacity = '0';
            window.setTimeout(function () {
                if (toast.parentNode) {
                    toast.parentNode.removeChild(toast);
                }
            }, 400);
        }, 3200);
    }

    function showNotice(message) {
        var ui = createOverlay();

        ui.dialog.appendChild(createElement('div', 'mc-title', 'Film gesperrt'));
        ui.dialog.appendChild(createElement('div', 'mc-text', message));

        var actions = createElement('div', 'mc-actions');
        var button = createElement('button', 'mc-btn mc-btn-primary', 'OK');
        button.type = 'button';
        actions.appendChild(button);
        ui.dialog.appendChild(actions);

        function close() {
            ui.close();
        }

        button.addEventListener('click', close);
        ui.overlay.addEventListener('keydown', function (event) {
            if (event.key === 'Escape' || event.key === 'Enter') {
                event.preventDefault();
                close();
            }
        });

        button.focus();
    }

    function askForCode(status, itemId) {
        return new Promise(function (resolve) {
            var ui = createOverlay();
            var closed = false;

            var title = createElement('div', 'mc-title', 'Film-Freigabe');
            var text = createElement(
                'div',
                'mc-text',
                'Gib einen deiner 6-stelligen Einmalcodes ein, um „' + (status.itemName || 'diesen Film') + '“ zu starten.');

            var input = createElement('input', 'mc-input');
            input.type = 'text';
            input.inputMode = 'numeric';
            input.autocomplete = 'one-time-code';
            input.maxLength = 6;
            input.setAttribute('aria-label', '6-stelliger Einmalcode');

            var error = createElement('div', 'mc-error');
            var actions = createElement('div', 'mc-actions');
            var cancel = createElement('button', 'mc-btn mc-btn-secondary', 'Abbrechen');
            var submit = createElement('button', 'mc-btn mc-btn-primary', 'Starten');
            cancel.type = 'button';
            submit.type = 'button';
            submit.disabled = true;

            actions.appendChild(cancel);
            actions.appendChild(submit);
            ui.dialog.appendChild(title);
            ui.dialog.appendChild(text);
            ui.dialog.appendChild(input);
            ui.dialog.appendChild(error);
            ui.dialog.appendChild(actions);

            function finish(value) {
                if (closed) {
                    return;
                }

                closed = true;
                ui.close();
                resolve(value);
            }

            function setBusy(busy) {
                input.disabled = busy;
                cancel.disabled = busy;
                submit.disabled = busy || input.value.length !== 6;
                submit.textContent = busy ? 'Wird geprüft …' : 'Starten';
            }

            function send() {
                var code = input.value.replace(/\D/g, '');
                if (code.length !== 6) {
                    error.textContent = 'Bitte gib einen 6-stelligen Code ein.';
                    input.focus();
                    return;
                }

                error.textContent = '';
                setBusy(true);

                apiPost('MovieCode/Unlock', { code: code, itemId: itemId }).then(function (result) {
                    if (result && result.success) {
                        showToast('Code akzeptiert. Viel Spaß!');
                        finish(true);
                        return;
                    }

                    setBusy(false);
                    error.textContent = (result && result.message) || 'Der Code ist ungültig oder abgelaufen.';
                    input.select();
                }).catch(function (xhr) {
                    setBusy(false);
                    var message = xhr && xhr.responseJSON && xhr.responseJSON.message
                        ? xhr.responseJSON.message
                        : 'Der Code konnte nicht geprüft werden.';
                    error.textContent = message;
                    input.select();
                });
            }

            input.addEventListener('input', function () {
                var digits = input.value.replace(/\D/g, '').slice(0, 6);
                if (input.value !== digits) {
                    input.value = digits;
                }

                error.textContent = '';
                submit.disabled = digits.length !== 6;
            });

            input.addEventListener('keydown', function (event) {
                if (event.key === 'Enter') {
                    event.preventDefault();
                    send();
                } else if (event.key === 'Escape') {
                    event.preventDefault();
                    finish(false);
                }
            });

            cancel.addEventListener('click', function () {
                finish(false);
            });

            submit.addEventListener('click', send);

            window.setTimeout(function () {
                input.focus();
            }, 30);
        });
    }

    function ensureAccess(itemId) {
        if (!isLoggedIn()) {
            return Promise.resolve(true);
        }

        if (pending[itemId]) {
            return pending[itemId];
        }

        var promise = apiGet('MovieCode/Status', { itemId: itemId }).then(function (status) {
            if (!status || !status.restricted || status.allowed) {
                return true;
            }

            if (status.requiresCode) {
                return askForCode(status, itemId);
            }

            showNotice(status.message || 'Diese Wiedergabe ist nicht freigegeben.');
            return false;
        }).catch(function () {
            // Wenn der Status nicht abgefragt werden kann, entscheidet weiterhin
            // die serverseitige Wiedergabesperre.
            return true;
        }).then(function (allowed) {
            delete pending[itemId];
            return allowed;
        });

        pending[itemId] = promise;
        return promise;
    }

    function matchPlaybackInfo(url) {
        if (!url) {
            return null;
        }

        var match = String(url).match(/\/Items\/([0-9a-fA-F]{32})\/PlaybackInfo(?:[/?]|$)/);
        return match ? match[1].toLowerCase() : null;
    }

    // Jellyfin nutzt für die Wiedergabe XMLHttpRequest (axios). Die Anfrage wird
    // angehalten, bis der Code eingegeben wurde.
    var originalOpen = XMLHttpRequest.prototype.open;
    var originalSend = XMLHttpRequest.prototype.send;

    XMLHttpRequest.prototype.open = function (method, url) {
        try {
            this.__movieCodeUrl = typeof url === 'string' ? url : String(url);
        } catch (e) {
            this.__movieCodeUrl = '';
        }

        return originalOpen.apply(this, arguments);
    };

    XMLHttpRequest.prototype.send = function (body) {
        var xhr = this;
        var itemId = matchPlaybackInfo(xhr.__movieCodeUrl);

        if (!itemId || !isLoggedIn()) {
            return originalSend.call(xhr, body);
        }

        var args = arguments;
        ensureAccess(itemId).then(function (allowed) {
            if (allowed) {
                originalSend.apply(xhr, args);
                return;
            }

            // Senden und sofort abbrechen, damit axios die Wiedergabe als
            // abgebrochen erkennt und kein Player geöffnet wird.
            originalSend.apply(xhr, args);
            try {
                xhr.abort();
            } catch (e) {
                // ignorieren
            }
        }, function () {
            originalSend.apply(xhr, args);
        });
    };

    // Sicherheitsnetz für Clients, die PlaybackInfo über fetch anfordern.
    if (window.fetch) {
        var originalFetch = window.fetch;
        window.fetch = function (input, init) {
            var url = typeof input === 'string' ? input : (input && input.url) || '';
            var itemId = matchPlaybackInfo(url);

            if (!itemId || !isLoggedIn()) {
                return originalFetch.apply(this, arguments);
            }

            var args = arguments;
            return ensureAccess(itemId).then(function (allowed) {
                if (!allowed) {
                    throw new DOMException('Wiedergabe abgebrochen', 'AbortError');
                }

                return originalFetch.apply(window, args);
            });
        };
    }
})();
