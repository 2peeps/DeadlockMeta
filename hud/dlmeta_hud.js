// Deadlock Meta HUD: event timers under the minimap, a reload countdown in the crosshair, the zipline boost
// cooldown and a frameless minimap. Runs in the game's panorama UI from base_hud; Deadlock Meta fills in CONFIG
// when it builds the mod. Only reads what the HUD already shows (the match clock, the game's own timers and
// progress rings) and never changes how the game plays.
(function () {
    'use strict';

    var CONFIG = /*CONFIG*/{};
    var schedule = CONFIG.schedule || { events: [] };
    var ctx = $.GetContextPanel();
    var TAG = '[DeadlockMeta] ';

    // A reload of the HUD (map change) runs the script again on the same panel; the old loop stops.
    if (ctx.dlmetaStop) ctx.dlmetaStop();
    var stopped = false;
    ctx.dlmetaStop = function () { stopped = true; };

    var COLOR = { text: '#FFEFD7', muted: '#FFEFD799', soon: '#FFD36B', alert: '#FF6B5B' };

    function valid(p) { return !!(p && p.IsValid && p.IsValid()); }
    function now() { return Date.now() / 1000; }

    var said = {};
    function once(key, msg) {
        if (said[key]) return;
        said[key] = true;
        $.Msg(TAG + msg);
    }

    // ---- finding the game's panels ----
    // Panels are looked up by id once and cached; a missing one is searched again at most every 2 seconds,
    // because a full search of the HUD is the expensive part.

    var hud = null, cache = {}, retry = {};
    function getHud() {
        if (valid(hud)) return hud;
        hud = ctx.FindChildTraverse('Hud');
        cache = {};
        return valid(hud) ? hud : null;
    }
    function find(id) {
        var p = cache[id];
        if (valid(p)) return p;
        var h = getHud();
        if (!h || (retry[id] || 0) > now()) return null;
        p = h.FindChildTraverse(id);
        if (valid(p)) { cache[id] = p; return p; }
        retry[id] = now() + 2;
        return null;
    }
    function findIn(parent, id) {
        if (!valid(parent)) return null;
        var kids = parent.Children() || [];
        for (var i = 0; i < kids.length; i++) if (valid(kids[i]) && kids[i].id === id) return kids[i];
        return null;
    }
    function shown(p) { return valid(p) && (p.IsVisible ? p.IsVisible() : true); }
    function show(panel, visible) {
        if (!valid(panel)) return;
        var v = visible ? 'visible' : 'collapse';
        if (panel.dlmetaVis !== v) { panel.style.visibility = v; panel.dlmetaVis = v; }
    }
    function setText(label, text) {
        if (valid(label) && label.dlmetaText !== text) { label.text = text; label.dlmetaText = text; }
    }
    function setColor(label, color) {
        if (valid(label) && label.dlmetaColor !== color) { label.style.color = color; label.dlmetaColor = color; }
    }

    // ---- the match clock ----

    function clock() {
        var label = find('GameTime');
        if (!label) return null;
        var text = String(label.text || '').trim();
        if (!text) return null;
        var negative = text.charAt(0) === '-';
        var parts = text.replace('-', '').split(':');
        var total = 0;
        for (var i = 0; i < parts.length; i++) {
            var n = parseInt(parts[i], 10);
            if (isNaN(n)) return null;
            total = total * 60 + n;
        }
        return negative ? -total : total;
    }

    function inMatch() {
        var h = getHud();
        if (!h) return false;
        return !(h.BHasClass('connectedToHideout') || h.BHasClass('InHideout') || h.BHasClass('gamemode_streetbrawl'));
    }

    function fmt(seconds) {
        seconds = Math.max(0, Math.ceil(seconds));
        var m = Math.floor(seconds / 60), s = seconds % 60;
        return m + ':' + (s < 10 ? '0' : '') + s;
    }

    // ---- event timers ----
    // A quiet column beside the minimap's base, like the game's own markers: an icon and the time left, one
    // timer under another.
    // Below it the zipline boost cooldown, only while it is cooling down.

    var BOSS_ICON = 's2r://panorama/images/minimap/super_neutral_marker_psd.vtex';
    var timersPanel = null, slots = {}, zip = null;

    function makeItem(row, id, icon, label) {
        var item = $.CreatePanel('Panel', row, 'DLMetaItem_' + id, { hittest: 'false' });
        item.style.flowChildren = 'right';
        item.style.margin = '0px 0px 2px 0px';
        item.style.visibility = 'collapse';
        if (icon) {
            var img = $.CreatePanel('Panel', item, 'DLMetaIcon_' + id, { hittest: 'false' });
            img.style.width = '22px';
            img.style.height = '22px';
            img.style.marginRight = '5px';
            img.style.verticalAlign = 'center';
            img.style.backgroundImage = 'url("' + icon + '")';
            img.style.backgroundSize = 'contain';
            img.style.backgroundRepeat = 'no-repeat';
            img.style.backgroundPosition = 'center';
        } else {
            var word = $.CreatePanel('Label', item, 'DLMetaWord_' + id, { hittest: 'false' });
            word.text = label || id;
            word.style.fontSize = '11px';
            word.style.fontWeight = 'bold';
            word.style.color = COLOR.muted;
            word.style.marginRight = '5px';
            word.style.verticalAlign = 'center';
            word.style.textShadow = '0px 1px 3px 2.0 #000000';
        }
        var time = $.CreatePanel('Label', item, 'DLMetaTime_' + id, { hittest: 'false' });
        time.style.fontSize = '20px';
        time.style.fontWeight = 'bold';
        time.style.color = COLOR.text;
        time.style.verticalAlign = 'center';
        time.style.textShadow = '0px 1px 4px 3.0 #000000';
        slots[id] = { panel: item, time: time };
    }

    function makeLabel(parent, id, size, color) {
        var l = $.CreatePanel('Label', parent, id, { hittest: 'false' });
        l.style.fontSize = size;
        l.style.fontWeight = 'bold';
        l.style.color = color;
        l.style.horizontalAlign = 'left';
        l.style.textShadow = '0px 1px 3px 2.0 #000000';
        return l;
    }

    function ensureTimers() {
        // Our panel dies with the game's minimap panel, so a valid one is still in the right place.
        if (valid(timersPanel)) return timersPanel;
        var persp = find('minimap_persp');
        if (!persp) return null;
        timersPanel = findIn(persp, 'DLMetaTimers') || $.CreatePanel('Panel', persp, 'DLMetaTimers', { hittest: 'false', hittestchildren: 'false' });
        // Left of the team base at the bottom of the map, where the map has empty space: the bottom edge belongs
        // to the game's district name and network line.
        var s = timersPanel.style;
        s.horizontalAlign = 'left';
        s.verticalAlign = 'bottom';
        s.marginLeft = '20px';
        s.marginBottom = '36px';
        s.flowChildren = 'down';
        s.width = 'fit-children';
        s.height = 'fit-children';
        s.zIndex = '10';
        timersPanel.RemoveAndDeleteChildren();
        slots = {};

        var row = $.CreatePanel('Panel', timersPanel, 'DLMetaRow', { hittest: 'false' });
        row.style.flowChildren = 'down';
        row.style.horizontalAlign = 'left';
        makeItem(row, 'boss', BOSS_ICON);
        var events = schedule.events || [];
        for (var i = 0; i < events.length; i++) makeItem(row, events[i].id, events[i].icon, events[i].label);

        var box = $.CreatePanel('Panel', timersPanel, 'DLMetaZip', { hittest: 'false' });
        box.style.flowChildren = 'down';
        box.style.horizontalAlign = 'left';
        box.style.marginTop = '3px';
        box.style.visibility = 'collapse';
        makeLabel(box, 'DLMetaZipTitle', '13px', COLOR.text).text = 'Буст зиплайна';
        zip = { panel: box, time: makeLabel(box, 'DLMetaZipTime', '11px', COLOR.alert) };
        once('timers', 'timers panel created');
        return timersPanel;
    }

    function parseTime(text) {
        text = String(text || '').replace(/<[^>]*>/g, '').trim();
        var m = text.match(/(\d+):(\d\d)/);
        if (m) return parseInt(m[1], 10) * 60 + parseInt(m[2], 10);
        m = text.match(/^(\d+)\s*s?$/);
        return m ? parseInt(m[1], 10) : null;
    }

    // ---- mid boss ----
    // Shown only while it is dead and coming back, so nothing before the first kill. The kill is seen on its
    // minimap marker (midboss_spawned goes away), and the countdown then runs on the match clock by itself: the
    // game updates its own countdown text only now and then, so that text only corrects the time when it moves.
    // The timer hides when the marker shows the boss up again or the time runs out.

    var RESPAWN = schedule.bossRespawn || [420, 360, 300];
    function freshBoss() { return { marker: null, search: 0, alive: null, kills: 0, target: null, text: '' }; }
    var boss = freshBoss();

    function bossMarker() {
        if (valid(boss.marker)) return boss.marker;
        var h = getHud();
        if (!h || boss.search > now()) return null;
        boss.search = now() + 3;
        var found = h.FindChildrenWithClassTraverse('mid_boss') || [];
        for (var i = 0; i < found.length; i++)
            if (valid(found[i]) && found[i].BHasClass('map_button')) { boss.marker = found[i]; break; }
        if (valid(boss.marker)) once('boss', 'mid boss marker found');
        return valid(boss.marker) ? boss.marker : null;
    }

    // Seconds until the mid boss is back, or null when there is nothing to wait for. t is the match clock.
    function bossLeft(t) {
        var marker = bossMarker(), label = null, alive = null;
        if (marker) {
            alive = marker.BHasClass('midboss_spawned');
            label = (marker.FindChildrenWithClassTraverse('midboss_timer') || [])[0];
        }
        if (!valid(label)) label = find('MidbossTimerLabel');

        if (alive === true) {
            boss.alive = true;
            boss.target = null;
            return null;
        }
        if (alive === false && boss.alive === true) {
            boss.alive = false;
            boss.target = t + RESPAWN[Math.min(boss.kills, RESPAWN.length - 1)];
            boss.kills++;
            $.Msg(TAG + 'mid boss killed at ' + fmt(t) + ', kill ' + boss.kills);
        }
        // A countdown from the game that is going down is the exact time left; a stuck one (like 0:01 while the
        // boss is up) is ignored.
        var text = valid(label) ? String(label.text || '') : '';
        if (text !== boss.text) {
            var left = parseTime(text), before = parseTime(boss.text);
            boss.text = text;
            if (left !== null && before !== null && left < before && left > 1) {
                if (!boss.kills) boss.kills = 1;
                boss.target = t + left;
            }
        }
        if (boss.target === null || t >= boss.target) {
            boss.target = null;
            return null;
        }
        return boss.target - t;
    }

    var zipLabel = null, zipSearch = 0;
    function zipline() {
        if (!valid(zipLabel)) {
            zipLabel = null;
            var h = getHud();
            if (!h || zipSearch > now()) return null;
            zipSearch = now() + 3;
            var found = h.FindChildrenWithClassTraverse('cooldown_timer') || [];
            for (var i = 0; i < found.length; i++) {
                var parent = found[i].GetParent();
                if (valid(parent) && parent.paneltype === 'CitadelZiplineBoostIcon') { zipLabel = found[i]; break; }
            }
            if (!zipLabel) return null;
            once('zip', 'zipline boost found');
        }
        var text = String(zipLabel.text || '').trim();
        if (!text || text === '0' || !shown(zipLabel)) return null;
        return /^\d+$/.test(text) ? text + 'с' : text;
    }

    function colorFor(left) {
        if (left <= (schedule.alert || 10)) return COLOR.alert;
        if (left <= (schedule.soon || 30)) return COLOR.soon;
        return COLOR.text;
    }

    function setSlot(id, text, color) {
        var slot = slots[id];
        if (!slot) return;
        show(slot.panel, text !== null);
        if (text === null) return;
        setText(slot.time, text);
        setColor(slot.time, color);
    }

    // ---- events with a minimap marker (the rift) ----
    // While the marker is on the map the event is up and its timer hides. When the marker goes away it was
    // taken, and the next spawn window starts counting from that moment.

    var markers = {};

    function markerUp(e) {
        var st = markers[e.id] || (markers[e.id] = { panel: null, search: 0, up: false, from: null, to: null });
        if (!valid(st.panel)) {
            st.panel = null;
            var h = getHud();
            if (h && st.search <= now()) {
                st.search = now() + 2;
                var found = h.FindChildrenWithClassTraverse(e.marker) || [];
                for (var i = 0; i < found.length; i++)
                    if (valid(found[i]) && found[i].BHasClass('map_button')) { st.panel = found[i]; break; }
                if (st.panel) once('marker_' + e.id, e.id + ' marker found');
            }
        }
        return st;
    }

    // The spawn window to count down to: [from, to] on the match clock, or null when the event is up or done.
    function nextWindow(e, t) {
        if (!e.marker) return t < e.first + e.window ? [e.first, e.first + e.window] : null;
        var st = markerUp(e);
        // A marker that is there before the event can spawn is a placeholder the game keeps around, so it says
        // nothing; then only the schedule is used.
        if (st.panel && t < e.first - 5) st.placeholder = true;
        if (st.placeholder) return t < e.first + e.window ? [e.first, e.first + e.window] : null;
        var up = !!st.panel && shown(st.panel);
        if (up) { st.up = true; return null; }
        if (st.up) {
            st.up = false;
            st.from = t + e.respawn[0];
            st.to = t + e.respawn[1];
        }
        if (st.from !== null) return t < st.to ? [st.from, st.to] : null;
        return t < e.first + e.window ? [e.first, e.first + e.window] : null;
    }

    var lastClock = null;
    function updateTimers(t) {
        if (!ensureTimers()) return;
        // The clock going backwards means a new match: forget the old one's mid boss and rift.
        if (t !== null && lastClock !== null && t < lastClock - 5) { boss = freshBoss(); markers = {}; }
        if (t !== null) lastClock = t;

        var on = CONFIG.timers && t !== null;
        var b = on ? bossLeft(t) : null;
        setSlot('boss', b === null ? null : fmt(b), b === null ? null : colorFor(b));

        var events = schedule.events || [];
        for (var i = 0; i < events.length; i++) {
            var e = events[i], text = null, color = COLOR.text;
            if (on && t >= (e.showFrom || 0) && !(e.hideFrom && t >= e.hideFrom)) {
                var at = e.first;
                if (e.every && t >= e.first) at = e.first + Math.ceil((t - e.first + 0.001) / e.every) * e.every;
                if (e.window) {
                    var w = nextWindow(e, t);
                    if (w) {
                        if (t >= w[0]) { text = 'до ' + fmt(w[1] - t); color = COLOR.soon; }
                        else { text = fmt(w[0] - t) + ' – ' + fmt(w[1] - t); color = colorFor(w[0] - t); }
                    }
                } else if (t < at || !e.once) {
                    text = fmt(at - t);
                    color = colorFor(at - t);
                }
            }
            setSlot(e.id, text, color);
        }

        var z = CONFIG.zipline ? zipline() : null;
        show(zip.panel, z !== null);
        if (z !== null) setText(zip.time, 'КУЛДАУН ' + z);
    }

    // ---- reload countdown ----
    // While the gun reloads the game fills a ring around the crosshair. Its fill over time gives the reload
    // speed, so the seconds left are (1 - fill) / speed. When the fill cannot be read, the length of the previous
    // reload is used instead.

    var gunRoot = null, ring = null, reloadLabel = null;
    var reload = { active: false, start: 0, f0: 0, t0: 0, last: 0 };

    function fill(bar) {
        if (!valid(bar)) return null;
        var v = bar.value, min = bar.min || 0, max = bar.max;
        if (typeof v === 'number' && typeof max === 'number' && max > min) return Math.min(1, Math.max(0, (v - min) / (max - min)));
        var clip = String(bar.style.clip || '');
        var deg = clip.match(/[-+]?\d*\.?\d+\s*deg/g);
        if (deg && deg.length === 2) return Math.min(1, Math.abs(parseFloat(deg[1]) - parseFloat(deg[0])) / 360);
        return null;
    }

    function ensureReload() {
        var status = find('reticle_status');
        if (!status) return false;
        if (!valid(gunRoot)) {
            var p = status.GetParent();
            for (var i = 0; valid(p) && i < 4; i++, p = p.GetParent())
                if (p.paneltype === 'Citadel_AbilityHUDElement_Gun') { gunRoot = p; break; }
            if (!valid(gunRoot)) { once('gun', 'gun element not found'); return false; }
        }
        if (!valid(ring)) ring = findIn(status, 'attack_delayed_progress_bar');
        if (!valid(reloadLabel)) {
            reloadLabel = findIn(status, 'DLMetaReload') || $.CreatePanel('Label', status, 'DLMetaReload', { hittest: 'false' });
            var s = reloadLabel.style;
            s.horizontalAlign = 'center';
            s.verticalAlign = 'center';
            s.fontSize = '22px';
            s.fontWeight = 'bold';
            s.color = COLOR.alert;
            s.textShadow = '0px 0px 4px 3.0 #000000';
            s.zIndex = '20';
            s.visibility = 'collapse';
            once('reload', 'reload countdown attached');
        }
        return true;
    }

    function updateReload() {
        if (!CONFIG.reload || !ensureReload()) return false;
        var reloading = gunRoot.BHasClass('reloading');
        var t = now();
        if (reloading && !reload.active) {
            reload.active = true;
            reload.start = t;
            reload.f0 = fill(ring);
            reload.t0 = t;
        } else if (!reloading && reload.active) {
            reload.active = false;
            var took = t - reload.start;
            if (took > 0.2 && took < 10) reload.last = took;
        }
        if (!reload.active) { show(reloadLabel, false); return false; }

        var left = null, f = fill(ring);
        if (f !== null && reload.f0 !== null && t - reload.t0 > 0.08 && f > reload.f0) {
            left = (1 - f) * (t - reload.t0) / (f - reload.f0);
        } else if (reload.last > 0) {
            left = reload.last - (t - reload.start);
        }
        if (left === null || left <= 0.05) { show(reloadLabel, false); return true; }
        setText(reloadLabel, left >= 10 ? String(Math.ceil(left)) : left.toFixed(1));
        show(reloadLabel, true);
        return true;
    }

    // ---- frameless, larger minimap ----

    var mapDone = 0;
    function updateMap() {
        if (mapDone > now()) return;
        mapDone = now() + 2; // the game may rebuild these panels, so the styles are put back now and then
        // The minimap sits in the bottom right corner, so a larger one grows up and to the left.
        var scale = CONFIG.mapScale || 1;
        var persp = find('minimap_persp');
        if (persp && scale > 1.001) persp.style.uiScale = Math.round(scale * 100) + '%';
        if (!CONFIG.cleanMap) return;
        var frame = find('minimap_frame');
        if (frame) frame.style.visibility = 'collapse';
        var blur = find('minimap_blur');
        if (blur) blur.style.visibility = 'collapse';
        var bg = find('MinimapBackgroundTest');
        if (bg) bg.style.opacity = String(CONFIG.mapOpacity || 0.75);
        if (frame || bg) once('map', 'minimap restyled');
    }

    // ---- main loop ----

    var errors = 0;
    function tick() {
        if (stopped || !valid(ctx)) return;
        var fast = false;
        try {
            if (inMatch()) {
                var t = clock();
                updateTimers(t);
                fast = updateReload();
                updateMap();
            } else if (valid(timersPanel)) {
                show(timersPanel, false);
            }
            if (valid(timersPanel) && inMatch()) show(timersPanel, true);
        } catch (e) {
            if (errors++ < 5) $.Msg(TAG + 'error: ' + e);
        }
        $.Schedule(fast ? 0.05 : 0.2, tick);
    }

    $.Msg(TAG + 'HUD script loaded');
    $.Schedule(1.0, tick);
})();
