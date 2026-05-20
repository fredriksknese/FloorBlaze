window.floorplan = (function () {
    let ro = null;
    let dnet = null;
    const imgCache = {};

    function getImg(url) {
        let e = imgCache[url];
        if (e) return e;
        e = { img: new Image(), ready: false, bad: false };
        e.img.onload = () => {
            e.ready = true;
            if (dnet) dnet.invokeMethodAsync('OnRepaint');
        };
        e.img.onerror = () => { e.bad = true; };
        e.img.src = url;
        imgCache[url] = e;
        return e;
    }

    function fit(canvas) {
        const parent = canvas.parentElement;
        const w = parent.clientWidth;
        const h = parent.clientHeight;
        if (canvas.width !== w) canvas.width = w;
        if (canvas.height !== h) canvas.height = h;
        return { w, h };
    }

    return {
        observe: function (canvas, dotnetRef) {
            dnet = dotnetRef;
            const report = () => {
                const s = fit(canvas);
                dotnetRef.invokeMethodAsync('OnResize', s.w, s.h);
            };
            ro = new ResizeObserver(report);
            ro.observe(canvas.parentElement);
            report();

            window.addEventListener('keydown', (e) => {
                const k = (e.key || '').toLowerCase();
                const ctrl = e.ctrlKey || e.metaKey;
                const isArrow = k === 'arrowup' || k === 'arrowdown' ||
                                k === 'arrowleft' || k === 'arrowright';
                const handled =
                    k === 'escape' ||
                    k === 'delete' ||
                    isArrow ||
                    (ctrl && (k === 'z' || k === 'y'));
                if (!handled) return;
                if (k !== 'escape') e.preventDefault();
                dotnetRef.invokeMethodAsync('OnKey', k, ctrl);
            });
        },

        render: function (canvas, scene) {
            const ctx = canvas.getContext('2d');
            ctx.setTransform(1, 0, 0, 1, 0, 0);
            const cmds = scene.cmds || scene.Cmds || [];
            const num = (lo, up) => lo !== undefined ? lo : (up !== undefined ? up : 0);
            for (const c of cmds) {
                const t = c.t || c.T;
                if (t === 'clear') {
                    ctx.setTransform(1, 0, 0, 1, 0, 0);
                    ctx.fillStyle = c.fill || c.Fill || '#fff';
                    ctx.fillRect(0, 0, canvas.width, canvas.height);
                    continue;
                }
                const m = c.m || c.M;
                ctx.save();
                if (m) ctx.setTransform(m[0], m[1], m[2], m[3], m[4], m[5]);
                else ctx.setTransform(1, 0, 0, 1, 0, 0);

                const fill = c.fill || c.Fill;
                const stroke = c.stroke || c.Stroke;
                const lw = c.lw || c.Lw || 1;

                const x = num(c.x, c.X), y = num(c.y, c.Y);
                if (t === 'rect') {
                    const w = num(c.w, c.W), h = num(c.h, c.H);
                    if (fill) { ctx.fillStyle = fill; ctx.fillRect(x, y, w, h); }
                    if (stroke) { ctx.lineWidth = lw; ctx.strokeStyle = stroke; ctx.strokeRect(x, y, w, h); }
                } else if (t === 'line') {
                    ctx.beginPath();
                    ctx.moveTo(x, y);
                    ctx.lineTo(num(c.x2, c.X2), num(c.y2, c.Y2));
                    ctx.lineWidth = lw;
                    ctx.strokeStyle = stroke || '#000';
                    ctx.stroke();
                } else if (t === 'circle') {
                    ctx.beginPath();
                    ctx.arc(x, y, num(c.w, c.W), 0, Math.PI * 2);
                    if (fill) { ctx.fillStyle = fill; ctx.fill(); }
                    if (stroke) { ctx.lineWidth = lw; ctx.strokeStyle = stroke; ctx.stroke(); }
                } else if (t === 'img') {
                    const w = num(c.w, c.W), h = num(c.h, c.H);
                    const e = getImg(c.s || c.S);
                    if (e.ready) {
                        try { ctx.drawImage(e.img, 0, 0, w, h); }
                        catch (_) { ctx.strokeStyle = '#bbb'; ctx.strokeRect(0, 0, w, h); }
                    } else if (e.bad) {
                        ctx.fillStyle = '#eceff1'; ctx.fillRect(0, 0, w, h);
                        ctx.strokeStyle = '#b0bec5'; ctx.strokeRect(0, 0, w, h);
                    } else {
                        ctx.fillStyle = '#f2f4f7'; ctx.fillRect(0, 0, w, h);
                    }
                } else if (t === 'poly') {
                    const pts = c.pts || c.Pts || [];
                    if (pts.length >= 6) {
                        ctx.beginPath();
                        ctx.moveTo(pts[0], pts[1]);
                        for (let i = 2; i < pts.length; i += 2) ctx.lineTo(pts[i], pts[i + 1]);
                        ctx.closePath();
                        if (fill) { ctx.fillStyle = fill; ctx.fill(); }
                        if (stroke) { ctx.lineWidth = lw; ctx.strokeStyle = stroke; ctx.stroke(); }
                    }
                } else if (t === 'text') {
                    const fs = c.fs || c.Fs || 12;
                    ctx.font = fs + 'px Segoe UI, Arial, sans-serif';
                    ctx.fillStyle = fill || '#000';
                    ctx.textAlign = c.al || c.Al || 'center';
                    ctx.textBaseline = 'middle';
                    const s = c.s || c.S || '';
                    const tw = ctx.measureText(s).width;
                    ctx.save();
                    ctx.fillStyle = 'rgba(255,255,255,0.75)';
                    ctx.fillRect(x - tw / 2 - 3, y - fs / 2 - 2, tw + 6, fs + 4);
                    ctx.restore();
                    ctx.fillStyle = fill || '#000';
                    ctx.fillText(s, x, y);
                }
                ctx.restore();
            }
        },

        download: function (filename, text) {
            const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(a.href);
        },

        print: function (canvas) {
            const data = canvas.toDataURL('image/png');
            const w = window.open('');
            w.document.write('<img src="' + data + '" style="max-width:100%" onload="window.print();window.close()"/>');
        },

        storageGet: function (key) {
            try { return localStorage.getItem(key); }
            catch (_) { return null; }
        },

        storageSet: function (key, value) {
            try { localStorage.setItem(key, value); } catch (_) { }
        }
    };
})();
