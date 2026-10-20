(function () {
    'use strict';

    // Ergänzt die Benutzer-Kindersicherung um die Movie-Code-Verwaltung.
    // Jellyfin 12 bietet dafür keinen offiziellen Plugin-Hook; das Skript wird
    // daher in den Web-Client eingebunden und blendet den Bereich nur für
    // Administratoren auf der Kindersicherungsseite ein.
    if (window.__movieCodeAdminPanelLoaded) {
        return;
    }

    window.__movieCodeAdminPanelLoaded = true;

    var panelId = 'movieCodeUserPanel';
    var styleId = 'movieCodeUserPanelStyles';
    var adminCache = null;
    var usersCache = null;
    var usersCacheAt = 0;
    var usersReloadAt = 0;
    var currentUser = null;
    var generated = null;
    var scheduled = false;

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
            data: body === undefined ? null : JSON.stringify(body),
            contentType: body === undefined ? undefined : 'application/json',
            dataType: 'json'
        });
    }

    function apiPostNoContent(path) {
        var api = getApi();
        return api.ajax({
            type: 'POST',
            url: api.getUrl(path)
        });
    }

    function byId(id) {
        return document.getElementById(id);
    }

    function create(tagName, className, text) {
        var element = document.createElement(tagName);
        if (className) {
            element.className = className;
        }

        if (text !== undefined && text !== null) {
            element.textContent = text;
        }

        return element;
    }

    function formatCode(code) {
        var digits = String(code || '').replace(/\D/g, '');
        return digits.length === 6 ? digits.slice(0, 3) + ' ' + digits.slice(3) : digits;
    }

    function formatDate(value) {
        if (!value) {
            return '';
        }

        var date = new Date(value);
        if (isNaN(date.getTime())) {
            return '';
        }

        return date.toLocaleString('de-DE', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit'
        });
    }

    function statusLabel(status) {
        switch (status) {
            case 'Pending':
                return 'Offen';
            case 'Active':
                return 'Aktiv';
            case 'Completed':
                return 'Verbraucht';
            case 'Expired':
                return 'Abgelaufen';
            default:
                return status;
        }
    }

    function statusClass(status) {
        switch (status) {
            case 'Active':
                return 'isActive';
            case 'Pending':
                return 'isPending';
            case 'Completed':
                return 'isCompleted';
            case 'Expired':
                return 'isExpired';
            default:
                return '';
        }
    }

    function countCodes(codes, status) {
        return codes.filter(function (code) { return code.status === status; }).length;
    }

    function getBatchStatus(codes) {
        if (codes.some(function (code) { return code.status === 'Active'; })) {
            return 'Active';
        }

        if (codes.some(function (code) { return code.status === 'Pending'; })) {
            return 'Pending';
        }

        if (codes.every(function (code) { return code.status === 'Expired'; })) {
            return 'Expired';
        }

        return 'Completed';
    }

    function groupBatches(grants) {
        var batches = {};

        grants.forEach(function (grant) {
            var batch = batches[grant.batchId];
            if (!batch) {
                batch = {
                    batchId: grant.batchId,
                    userName: grant.userName,
                    createdUtc: grant.createdUtc,
                    expiresUtc: grant.expiresUtc,
                    codes: []
                };
                batches[grant.batchId] = batch;
            }

            batch.codes.push(grant);
            if (!batch.expiresUtc && grant.expiresUtc) {
                batch.expiresUtc = grant.expiresUtc;
            }
        });

        return Object.keys(batches)
            .map(function (key) { return batches[key]; })
            .sort(function (a, b) { return new Date(b.createdUtc) - new Date(a.createdUtc); });
    }

    function formatCounts(codes) {
        var parts = [];
        var pending = countCodes(codes, 'Pending');
        var active = countCodes(codes, 'Active');
        var completed = countCodes(codes, 'Completed');
        var expired = countCodes(codes, 'Expired');

        if (pending) {
            parts.push(pending + ' offen');
        }
        if (active) {
            parts.push(active + ' aktiv');
        }
        if (completed) {
            parts.push(completed + ' verbraucht');
        }
        if (expired) {
            parts.push(expired + ' abgelaufen');
        }

        return codes.length + ' Einmalcodes' + (parts.length ? ' · ' + parts.join(' · ') : '');
    }

    function ensureStyles() {
        if (document.getElementById(styleId)) {
            return;
        }

        var style = document.createElement('style');
        style.id = styleId;
        style.textContent = [
            '#movieCodeUserPanel .mcAdminCodeList{font-family:Consolas,"Courier New",monospace;font-size:1.15em;line-height:1.65;letter-spacing:.08em;margin:.4em 0 .8em;padding-left:2.4em;column-count:2;column-gap:2em;}',
            '@media (max-width:700px){#movieCodeUserPanel .mcAdminCodeList{column-count:1;}}',
            '#movieCodeUserPanel .mcAdminCodeList li{break-inside:avoid;}',
            '#movieCodeUserPanel .mcAdminActions{display:flex;gap:.6em;flex-wrap:wrap;margin:.6em 0;}',
            '#movieCodeUserPanel .mcAdminCodeRows{display:flex;flex-direction:column;gap:.2em;margin-top:.5em;}',
            '#movieCodeUserPanel .mcAdminCodeRow{display:flex;flex-wrap:wrap;align-items:center;gap:.6em;font-size:.9em;}',
            '#movieCodeUserPanel .mcAdminCodeLabel{min-width:5.5em;font-weight:600;}',
            '#movieCodeUserPanel .mcAdminStatus{display:inline-block;padding:.1em .55em;border-radius:999px;font-size:.82em;line-height:1.5;border:1px solid currentColor;}',
            '#movieCodeUserPanel .mcAdminStatus.isPending{color:#d39e00;background:rgba(255,193,7,.14);}',
            '#movieCodeUserPanel .mcAdminStatus.isActive{color:#2ecc71;background:rgba(46,204,113,.14);}',
            '#movieCodeUserPanel .mcAdminStatus.isCompleted{color:#8a8a8a;background:rgba(128,128,128,.14);}',
            '#movieCodeUserPanel .mcAdminStatus.isExpired{color:#ff6b6b;background:rgba(255,107,107,.14);}',
            '#movieCodeUserPanel .mcAdminFeedback{min-height:1.4em;margin:.4em 0;}',
            '#movieCodeUserPanel .mcAdminFeedback.isError{color:#ff6b6b;}',
            '#movieCodeUserPanel .mcAdminBatch{margin-top:1em;padding:.85em 1em;}'
        ].join('');

        document.head.appendChild(style);
    }

    function isAdmin() {
        if (adminCache === true) {
            return Promise.resolve(true);
        }

        if (!isLoggedIn()) {
            return Promise.resolve(false);
        }

        var api = getApi();
        var promise;
        if (api && typeof api.getCurrentUser === 'function') {
            promise = api.getCurrentUser();
        } else {
            promise = apiGet('Users/' + api.getCurrentUserId());
        }

        return promise.then(function (user) {
            var value = !!(user && user.Policy && user.Policy.IsAdministrator);
            if (value) {
                adminCache = true;
            }

            return value;
        }).catch(function () {
            return false;
        });
    }

    function getPageTitle() {
        var page = document.getElementById('usersEditPage');
        if (!page) {
            return '';
        }

        var heading = page.querySelector('h1');
        return heading ? heading.textContent.trim() : '';
    }

    function pickUser(users, name) {
        if (!users || !name) {
            return null;
        }

        // Zuerst exakt über den Benutzernamen suchen, danach über den Anzeigenamen.
        for (var i = 0; i < users.length; i++) {
            if (users[i] && users[i].Name === name) {
                return users[i];
            }
        }

        for (var j = 0; j < users.length; j++) {
            var user = users[j];
            if (user && user.Configuration && user.Configuration.DisplayName === name) {
                return user;
            }
        }

        return null;
    }

    function loadUsers(force) {
        var now = Date.now();
        if (!force && usersCache && now - usersCacheAt < 5000) {
            return Promise.resolve(usersCache);
        }

        return apiGet('Users').then(function (users) {
            usersCache = users || [];
            usersCacheAt = Date.now();
            return usersCache;
        });
    }

    function resolveUser() {
        var title = getPageTitle();
        if (!title) {
            return Promise.resolve(null);
        }

        var cached = pickUser(usersCache, title);
        if (cached) {
            return Promise.resolve(cached);
        }

        // Bei einem neuen oder umbenannten Benutzer die Liste frisch laden,
        // aber höchstens alle fünf Sekunden, damit DOM-Aktualisierungen keine
        // Anfrageflut auslösen.
        var now = Date.now();
        if (now - usersReloadAt < 5000) {
            return Promise.resolve(null);
        }

        usersReloadAt = now;
        return loadUsers(true).then(function (users) {
            return pickUser(users, title);
        });
    }

    function schedulePanel() {
        if (scheduled) {
            return;
        }

        scheduled = true;
        window.setTimeout(function () {
            scheduled = false;
            trySetup();
        }, 150);
    }

    function removePanel() {
        var panel = byId(panelId);
        if (panel && panel.parentNode) {
            panel.parentNode.removeChild(panel);
        }

        currentUser = null;
    }

    function trySetup() {
        var page = document.getElementById('usersEditPage');
        var form = page ? page.querySelector('form.userParentalControlForm') : null;

        if (!form) {
            removePanel();
            return;
        }

        isAdmin().then(function (admin) {
            if (!admin) {
                removePanel();
                return;
            }

            resolveUser().then(function (user) {
                if (!user) {
                    return;
                }

                var existing = byId(panelId);
                if (existing && existing.dataset.userId === user.Id) {
                    return;
                }

                renderPanel(form, user);
            }).catch(function () {
                // Ohne Benutzerzuordnung bleibt der Bereich ausgeblendet.
            });
        }).catch(function () {
            // Keine Admin-Rechte oder Benutzer nicht ermittelbar.
        });
    }

    function renderPanel(form, user) {
        removePanel();
        ensureStyles();
        currentUser = user;
        generated = null;

        var panel = create('div', 'verticalSection');
        panel.id = panelId;
        panel.dataset.userId = user.Id;
        panel.style.marginTop = '2em';

        var header = create('div', 'detailSectionHeader sectionTitleContainer flex align-items-center');
        header.appendChild(create('h2', 'sectionTitle', 'Movie Code – Einmalcodes'));
        panel.appendChild(header);

        panel.appendChild(create('p', 'fieldDescription',
            'Jeder Code gilt genau einmal für einen Film. Ein neues Paket ersetzt alle noch offenen Codes dieses Benutzers. Die Liste wird nur direkt nach dem Erzeugen angezeigt.'));

        var createButton = create('button', 'emby-button raised');
        createButton.type = 'button';
        createButton.appendChild(create('span', '', '10 Einmalcodes erzeugen'));
        createButton.addEventListener('click', function () {
            createBatch(user, createButton);
        });
        panel.appendChild(createButton);

        var feedback = create('div', 'fieldDescription mcAdminFeedback');
        feedback.id = 'movieCodeUserFeedback';
        panel.appendChild(feedback);

        var codes = create('div');
        codes.id = 'movieCodeUserCodes';
        codes.style.display = 'none';
        panel.appendChild(codes);

        var grants = create('div');
        grants.id = 'movieCodeUserGrants';
        panel.appendChild(grants);

        // Vor dem Speichern-Button einsetzen, damit der Bereich im Formular bleibt.
        var saveButton = form.lastElementChild;
        if (saveButton) {
            form.insertBefore(panel, saveButton);
        } else {
            form.appendChild(panel);
        }

        loadGrants(user);
    }

    function setFeedback(message, isError) {
        var element = byId('movieCodeUserFeedback');
        if (!element) {
            return;
        }

        element.textContent = message || '';
        element.className = 'fieldDescription mcAdminFeedback' + (isError ? ' isError' : '');

        if (message) {
            window.clearTimeout(setFeedback.timer);
            setFeedback.timer = window.setTimeout(function () {
                if (element.textContent === message) {
                    element.textContent = '';
                    element.className = 'fieldDescription mcAdminFeedback';
                }
            }, 6000);
        }
    }

    function createBatch(user, button) {
        if (!window.confirm('Neue 10 Einmalcodes für „' + user.Name + '“ erzeugen? Alle noch offenen Codes dieses Benutzers werden ungültig.')) {
            return;
        }

        button.disabled = true;
        setFeedback('Einmalcodes werden erzeugt …', false);

        apiPost('MovieCode/Admin/Grants', { userId: user.Id }).then(function (result) {
            button.disabled = false;
            setFeedback('', false);
            generated = {
                codes: result.codes || [],
                userName: result.userName || user.Name,
                created: new Date().toISOString(),
                expiresUtc: result.expiresUtc || null
            };
            renderGeneratedCodes();
            loadGrants(user);
        }).catch(function (error) {
            button.disabled = false;
            var message = error && error.responseJSON && error.responseJSON.message
                ? error.responseJSON.message
                : 'Die Einmalcodes konnten nicht erzeugt werden.';
            setFeedback(message, true);
        });
    }

    function renderGeneratedCodes() {
        var box = byId('movieCodeUserCodes');
        if (!box) {
            return;
        }

        box.innerHTML = '';
        if (!generated || !generated.codes.length) {
            box.style.display = 'none';
            return;
        }

        box.appendChild(create('div', 'fieldDescription',
            'Einmalcodes für ' + generated.userName + ' – jetzt kopieren oder herunterladen:'));

        var list = create('ol', 'mcAdminCodeList');
        generated.codes.forEach(function (code) {
            list.appendChild(create('li', '', formatCode(code)));
        });
        box.appendChild(list);

        var meta = create('div', 'fieldDescription');
        meta.textContent = generated.codes.length + ' Codes · erstellt ' + formatDate(generated.created)
            + (generated.expiresUtc ? ' · gültig bis ' + formatDate(generated.expiresUtc) : ' · unbegrenzt gültig');
        box.appendChild(meta);

        var actions = create('div', 'mcAdminActions');
        var copy = create('button', 'emby-button raised');
        copy.type = 'button';
        copy.appendChild(create('span', '', 'Alle Codes kopieren'));
        copy.addEventListener('click', function () {
            copyText(buildCodeListText(), 'Alle Codes kopiert.');
        });

        var download = create('button', 'emby-button raised');
        download.type = 'button';
        download.appendChild(create('span', '', 'Liste als TXT herunterladen'));
        download.addEventListener('click', downloadCodes);

        actions.appendChild(copy);
        actions.appendChild(download);
        box.appendChild(actions);

        box.appendChild(create('div', 'fieldDescription',
            'Bewahre die Liste sicher auf. Sie kann später nicht erneut angezeigt werden.'));
        box.style.display = '';
    }

    function buildCodeListText() {
        if (!generated) {
            return '';
        }

        var lines = [
            'Movie Code – Einmalcodes für ' + generated.userName,
            'Erstellt: ' + formatDate(generated.created),
            'Jeder Code gilt genau einmal für einen Film.',
            ''
        ];

        generated.codes.forEach(function (code, index) {
            lines.push((index + 1) + '. ' + formatCode(code));
        });

        return lines.join('\r\n');
    }

    function copyText(text, successMessage) {
        if (!text) {
            return;
        }

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(function () {
                setFeedback(successMessage, false);
            }).catch(function () {
                fallbackCopy(text, successMessage);
            });
            return;
        }

        fallbackCopy(text, successMessage);
    }

    function fallbackCopy(text, successMessage) {
        var area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();

        try {
            document.execCommand('copy');
            setFeedback(successMessage, false);
        } catch (e) {
            setFeedback('Kopieren ist in diesem Browser nicht möglich.', true);
        }

        document.body.removeChild(area);
    }

    function downloadCodes() {
        if (!generated) {
            return;
        }

        var user = generated.userName || 'Benutzer';
        var safeUser = user.replace(/[^a-z0-9_-]/gi, '_');
        var blob = new Blob([buildCodeListText()], { type: 'text/plain;charset=utf-8' });
        var url = URL.createObjectURL(blob);
        var link = document.createElement('a');
        link.href = url;
        link.download = 'MovieCode_' + safeUser + '_' + new Date().toISOString().slice(0, 10) + '.txt';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    }

    function loadGrants(user) {
        apiGet('MovieCode/Admin/Grants').then(function (grants) {
            if (!currentUser || currentUser.Id !== user.Id) {
                return;
            }

            var mine = (grants || []).filter(function (grant) {
                return grant.userId === user.Id;
            });
            renderGrants(mine, user);
        }).catch(function () {
            setFeedback('Die Code-Pakete konnten nicht geladen werden.', true);
        });
    }

    function renderGrants(grants, user) {
        var container = byId('movieCodeUserGrants');
        if (!container) {
            return;
        }

        container.innerHTML = '';

        if (!grants.length) {
            container.appendChild(create('div', 'fieldDescription',
                'Noch keine Code-Pakete für diesen Benutzer.'));
            return;
        }

        groupBatches(grants).forEach(function (batch) {
            batch.codes.sort(function (a, b) { return a.batchIndex - b.batchIndex; });

            var status = getBatchStatus(batch.codes);
            var active = batch.codes.filter(function (code) { return code.status === 'Active'; })[0];
            var block = create('div', 'paperList mcAdminBatch');

            var summaryLine = create('div');
            summaryLine.style.display = 'flex';
            summaryLine.style.alignItems = 'center';
            summaryLine.style.gap = '0.6em';
            summaryLine.style.flexWrap = 'wrap';
            summaryLine.appendChild(create('span', '', formatCounts(batch.codes)));
            summaryLine.appendChild(create('span', 'mcAdminStatus ' + statusClass(status), statusLabel(status)));
            block.appendChild(summaryLine);
            block.appendChild(create('div', 'fieldDescription',
                'Erstellt ' + formatDate(batch.createdUtc)
                + (batch.expiresUtc ? ' · gültig bis ' + formatDate(batch.expiresUtc) : ' · unbegrenzt gültig')));

            if (active) {
                var activeLine = create('div');
                activeLine.style.fontWeight = '600';
                activeLine.textContent = 'Aktiv: ' + (active.itemName || 'Film noch nicht gewählt')
                    + ' (Code ' + active.batchIndex + ')';
                block.appendChild(activeLine);
            }

            var details = create('details');
            details.style.marginTop = '0.5em';
            details.appendChild(create('summary', 'fieldDescription', 'Codes anzeigen'));

            var codeList = create('div', 'mcAdminCodeRows');
            batch.codes.forEach(function (code) {
                var row = create('div', 'mcAdminCodeRow');
                row.appendChild(create('span', 'mcAdminCodeLabel', 'Code ' + code.batchIndex));

                var label = statusLabel(code.status);
                if (code.status === 'Active' && code.itemName) {
                    label += ' – ' + code.itemName;
                }

                row.appendChild(create('span', 'mcAdminStatus ' + statusClass(code.status), label));
                codeList.appendChild(row);
            });
            details.appendChild(codeList);
            block.appendChild(details);

            var revoke = create('button', 'emby-button raised');
            revoke.type = 'button';
            revoke.style.marginTop = '0.6em';
            revoke.appendChild(create('span', '', 'Paket aufheben'));
            revoke.addEventListener('click', function () {
                if (!window.confirm('Alle Codes für „' + batch.userName + '“ wirklich aufheben? Noch offene Codes werden ungültig.')) {
                    return;
                }

                revoke.disabled = true;
                apiPostNoContent('MovieCode/Admin/Grants/Batch/' + batch.batchId + '/Revoke').then(function () {
                    generated = null;
                    var codes = byId('movieCodeUserCodes');
                    if (codes) {
                        codes.style.display = 'none';
                    }

                    loadGrants(user);
                    setFeedback('Code-Paket aufgehoben.', false);
                }).catch(function () {
                    revoke.disabled = false;
                    setFeedback('Das Code-Paket konnte nicht aufgehoben werden.', true);
                });
            });
            block.appendChild(revoke);

            container.appendChild(block);
        });
    }

    var observer = new MutationObserver(function () {
        schedulePanel();
    });

    observer.observe(document.documentElement, {
        childList: true,
        subtree: true
    });

    window.addEventListener('hashchange', schedulePanel);
    window.addEventListener('pageshow', schedulePanel);
    document.addEventListener('DOMContentLoaded', schedulePanel);
    schedulePanel();
})();
