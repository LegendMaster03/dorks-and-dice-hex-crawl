export function ensurePhase15Styles(): void {
    if (document.getElementById("hex-crawl-phase15-styles")) return;
    const style = document.createElement("style");
    style.id = "hex-crawl-phase15-styles";
    style.textContent = `
        .hc-phase15 { --hc-ux-gap: clamp(.75rem, 1.5vw, 1.15rem); }
        .hc-phase15 .hc-page-header { align-items:flex-start; }
        .hc-mode-switcher { display:flex; gap:.35rem; flex-wrap:wrap; align-items:center; }
        .hc-mode-switcher button[aria-pressed="true"] { font-weight:700; outline:2px solid currentColor; outline-offset:1px; }
        .hc-procedure-shell { display:grid; gap:var(--hc-ux-gap); }
        .hc-procedure-toolbar { display:flex; gap:.75rem; justify-content:space-between; align-items:center; flex-wrap:wrap; }
        .hc-procedure-summary-strip { display:grid; grid-template-columns:repeat(auto-fit,minmax(10rem,1fr)); gap:.65rem; }
        .hc-summary-metric { padding:.75rem .85rem; border:1px solid var(--hc-border, rgba(127,127,127,.35)); border-radius:.7rem; display:grid; gap:.2rem; min-width:0; }
        .hc-summary-metric strong { font-size:1.05rem; overflow-wrap:anywhere; }
        .hc-summary-metric span { font-size:.78rem; opacity:.75; text-transform:uppercase; letter-spacing:.045em; }
        .hc-preset-browser { display:grid; gap:1rem; }
        .hc-preset-browser-header { display:flex; align-items:end; justify-content:space-between; gap:1rem; flex-wrap:wrap; }
        .hc-preset-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,18rem),1fr)); gap:.85rem; }
        .hc-preset-card { border:1px solid var(--hc-border, rgba(127,127,127,.35)); border-radius:.85rem; padding:1rem; display:grid; gap:.75rem; align-content:start; background:var(--hc-panel, transparent); }
        .hc-preset-card.is-selected { outline:2px solid currentColor; outline-offset:2px; }
        .hc-preset-card h3, .hc-area-card h3 { margin:0; }
        .hc-preset-facts { display:grid; grid-template-columns:auto minmax(0,1fr); gap:.3rem .75rem; margin:0; }
        .hc-preset-facts dt { font-weight:600; }
        .hc-preset-facts dd { margin:0; overflow-wrap:anywhere; }
        .hc-preset-actions, .hc-area-actions, .hc-json-actions { display:flex; gap:.5rem; flex-wrap:wrap; align-items:center; }
        .hc-procedure-area-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,17rem),1fr)); gap:.8rem; }
        .hc-area-card { width:100%; text-align:left; border:1px solid var(--hc-border, rgba(127,127,127,.35)); border-radius:.8rem; padding:.95rem; background:var(--hc-panel, transparent); display:grid; gap:.55rem; cursor:pointer; }
        .hc-area-card:hover { border-color:currentColor; }
        .hc-area-card:focus-visible { outline:3px solid currentColor; outline-offset:2px; }
        .hc-area-card-heading { display:flex; gap:.5rem; align-items:flex-start; justify-content:space-between; }
        .hc-area-card-summary { margin:0; }
        .hc-area-card-meta { display:flex; gap:.4rem; flex-wrap:wrap; }
        .hc-ux-badge { display:inline-flex; align-items:center; border:1px solid currentColor; border-radius:999px; padding:.12rem .48rem; font-size:.74rem; line-height:1.35; white-space:nowrap; }
        .hc-ux-badge-good { opacity:.85; }
        .hc-ux-badge-warning { font-weight:600; }
        .hc-ux-badge-danger { font-weight:700; }
        .hc-ux-badge-info { opacity:.9; }
        .hc-focus-workspace { position:fixed; z-index:45; right:clamp(.5rem,2vw,1.5rem); top:clamp(.5rem,2vw,1.5rem); bottom:clamp(.5rem,2vw,1.5rem); width:min(44rem,calc(100vw - 1rem)); border:1px solid var(--hc-border, rgba(127,127,127,.45)); border-radius:1rem; background:var(--hc-panel,#fff); color:inherit; box-shadow:0 .8rem 2.4rem rgba(0,0,0,.28); display:grid; grid-template-rows:auto minmax(0,1fr); overflow:hidden; }
        .hc-focus-workspace-header { display:flex; align-items:center; justify-content:space-between; gap:1rem; padding:1rem 1.1rem; border-bottom:1px solid var(--hc-border, rgba(127,127,127,.35)); }
        .hc-focus-workspace-header h2 { margin:0; }
        .hc-focus-workspace-body { overflow:auto; overscroll-behavior:contain; padding:1rem 1.1rem 2rem; display:grid; gap:1rem; align-content:start; }
        .hc-focus-workspace-module { display:grid; gap:.8rem; padding:1rem; border:1px solid var(--hc-border, rgba(127,127,127,.3)); border-radius:.75rem; }
        .hc-compact-field-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,14rem),1fr)); gap:.75rem; }
        .hc-compact-field { display:grid; gap:.3rem; min-width:0; }
        .hc-compact-field input, .hc-compact-field select, .hc-compact-field textarea { width:100%; min-width:0; box-sizing:border-box; }
        .hc-domain-diagnostic { border-left:3px solid currentColor; padding:.55rem .7rem; margin:0; }
        .hc-domain-diagnostic strong { display:block; }
        .hc-advanced-layout { display:grid; grid-template-columns:minmax(12rem,17rem) minmax(0,1fr); gap:1rem; align-items:start; }
        .hc-advanced-index { position:sticky; top:.75rem; }
        .hc-advanced-module { display:grid; gap:.85rem; }
        .hc-advanced-contracts { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:.75rem; }
        .hc-json-editor { display:grid; gap:.75rem; }
        .hc-json-editor textarea { width:100%; min-height:32rem; max-height:70vh; resize:vertical; box-sizing:border-box; font-family:ui-monospace,SFMono-Regular,Menlo,Monaco,Consolas,"Liberation Mono",monospace; font-size:.86rem; line-height:1.45; tab-size:2; }
        .hc-json-status { min-height:1.4em; margin:0; }
        .hc-json-status.is-error { font-weight:600; }
        .hc-json-status.is-valid { opacity:.9; }
        .hc-ux-disclosure { border-top:1px solid var(--hc-border, rgba(127,127,127,.25)); padding-top:.5rem; }
        .hc-ux-disclosure > summary { cursor:pointer; font-weight:600; }
        .hc-stat-action-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,11.5rem),1fr)); gap:.55rem; }
        .hc-stat-action { text-align:left; display:grid; gap:.18rem; min-width:0; border:1px solid var(--hc-border, rgba(127,127,127,.32)); border-radius:.68rem; padding:.7rem .78rem; background:var(--hc-panel, transparent); color:inherit; }
        .hc-stat-action-label { font-size:.74rem; text-transform:uppercase; letter-spacing:.045em; opacity:.7; }
        .hc-stat-action-value { overflow-wrap:anywhere; }
        .hc-stat-action-detail { font-size:.78rem; opacity:.78; overflow-wrap:anywhere; }
        .hc-stat-action.is-warning, .hc-stat-action.is-danger { border-width:2px; }
        .hc-current-action { border:2px solid currentColor; border-radius:.85rem; padding:1rem; display:grid; gap:.7rem; }
        .hc-current-action header { display:flex; justify-content:space-between; align-items:start; gap:.75rem; flex-wrap:wrap; }
        .hc-current-action h2 { margin:0; }
        .hc-current-action-primary { font-size:1.05rem; margin:0; }
        .hc-current-action-detail { margin:0; opacity:.82; }
        .hc-phase15-expedition { display:grid; gap:var(--hc-ux-gap); }
        .hc-phase15-expedition .hc-workspace-grid { grid-template-columns:minmax(0,1fr) minmax(18rem,23rem); align-items:start; }
        .hc-phase15-expedition .hc-map-sheet-top { grid-template-columns:minmax(0,1.5fr) minmax(17rem,.8fr); }
        .hc-phase15-expedition .hc-sidebar { display:grid; gap:.7rem; align-content:start; }
        .hc-phase15-expedition .hc-running-sheet { display:grid; gap:.75rem; }
        .hc-phase15-expedition .hc-sheet-status { display:none; }
        .hc-phase15-runtime-summary { display:grid; gap:.65rem; }
        .hc-phase15-context-strip { display:flex; gap:.5rem; flex-wrap:wrap; }
        .hc-context-button { border-radius:999px; padding:.35rem .65rem; }
        .hc-context-card { display:grid; gap:.65rem; padding:.8rem; border:1px solid var(--hc-border, rgba(127,127,127,.3)); border-radius:.7rem; }
        .hc-nonspatial-primary { display:grid; grid-template-columns:minmax(0,1.4fr) minmax(17rem,.75fr); gap:1rem; align-items:start; }
        .hc-journey-primary { border:1px solid var(--hc-border, rgba(127,127,127,.35)); border-radius:.85rem; padding:1rem; display:grid; gap:.65rem; }
        .hc-phase15-empty { padding:1rem; border:1px dashed currentColor; border-radius:.7rem; opacity:.85; }
        @media (max-width: 1040px) {
            .hc-phase15-expedition .hc-workspace-grid,
            .hc-nonspatial-primary { grid-template-columns:1fr; }
            .hc-phase15-expedition .hc-map-sheet-top { grid-template-columns:1fr; }
        }
        @media (max-width: 760px) {
            .hc-advanced-layout { grid-template-columns:1fr; }
            .hc-advanced-index { position:static; }
            .hc-advanced-contracts { grid-template-columns:1fr; }
            .hc-focus-workspace { inset:.25rem; width:auto; border-radius:.7rem; }
            .hc-json-editor textarea { min-height:22rem; }
            .hc-preset-grid, .hc-procedure-area-grid, .hc-stat-action-grid { grid-template-columns:1fr; }
        }
        @media (prefers-reduced-motion: reduce) {
            .hc-focus-workspace { scroll-behavior:auto; }
        }
        @media print {
            .hc-mode-switcher, .hc-focus-workspace, .hc-preset-actions, .hc-json-actions { display:none !important; }
        }
    `;
    document.head.append(style);
}
