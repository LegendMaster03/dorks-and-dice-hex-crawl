const STYLE_ID = "hex-crawl-foundation-styles";

export function ensureStyles(): void {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
        .hc-app { display:grid; grid-template-rows:auto minmax(420px, 72vh) auto; gap:.75rem; min-width:0; }
        .hc-toolbar { display:flex; flex-wrap:wrap; gap:.75rem; align-items:end; padding:.75rem; border:1px solid var(--bs-border-color, #ccc); border-radius:.5rem; }
        .hc-control { display:grid; gap:.25rem; font-size:.875rem; }
        .hc-control input, .hc-control select, .hc-toolbar button { min-height:2.25rem; padding:.35rem .55rem; }
        .hc-stage { position:relative; overflow:hidden; border:1px solid var(--bs-border-color, #bbb); border-radius:.5rem; background:#eef1e8; min-height:420px; }
        .hc-canvas { width:100%; height:100%; display:block; touch-action:none; cursor:grab; }
        .hc-canvas[data-dragging="true"] { cursor:grabbing; }
        .hc-overlay { position:absolute; left:.75rem; top:.75rem; padding:.4rem .55rem; border-radius:.35rem; background:rgba(255,255,255,.9); font:12px/1.4 ui-monospace, SFMono-Regular, Menlo, monospace; pointer-events:none; }
        .hc-status { display:flex; flex-wrap:wrap; gap:1rem; font-size:.9rem; color:var(--bs-secondary-color, #555); }
        .hc-error { padding:.75rem; border:1px solid #b02a37; border-radius:.4rem; }
    `;
    document.head.append(style);
}
