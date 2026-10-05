export function ensurePhase15Styles(): void {
    if (document.getElementById("hex-crawl-phase15-styles")) return;
    const style = document.createElement("style");
    style.id = "hex-crawl-phase15-styles";
    style.textContent = `
        .hc-phase15 { --hc-ux-gap: clamp(.75rem, 1.5vw, 1.15rem); }
        .hc-phase15 .hc-page-header { align-items:flex-start; }
        .hc-mode-switcher { display:flex; gap:.35rem; flex-wrap:wrap; align-items:center; }
        .hc-mode-switcher button[aria-pressed="true"] { font-weight:700; outline:2px solid var(--hc-focus); outline-offset:1px; }
        .hc-procedure-shell { display:grid; gap:var(--hc-ux-gap); }
        .hc-procedure-toolbar { display:flex; gap:.75rem; justify-content:space-between; align-items:center; flex-wrap:wrap; }
        .hc-procedure-summary-strip { display:grid; grid-template-columns:repeat(auto-fit,minmax(10rem,1fr)); gap:.65rem; }
        .hc-summary-metric { padding:.75rem .85rem; border:1px solid var(--hc-border); border-radius:.7rem; display:grid; gap:.2rem; min-width:0; background:var(--hc-surface); color:var(--hc-text); }
        .hc-summary-metric strong { font-size:1.05rem; overflow-wrap:anywhere; color:var(--hc-text-strong); }
        .hc-summary-metric span { font-size:.78rem; color:var(--hc-muted); text-transform:uppercase; letter-spacing:.045em; }
        .hc-procedure-home-section { display:grid; gap:.85rem; }
        .hc-procedure-home-section > .hc-preset-browser-header h2,
        .hc-procedure-home-section > .hc-preset-browser-header p { margin:0; }
        .hc-build-custom { grid-template-columns:minmax(0,1fr) auto; align-items:center; padding:1rem; border:1px solid var(--hc-border); border-radius:.8rem; background:var(--hc-surface); }
        .hc-build-custom h2, .hc-build-custom p { margin:0; }
        .hc-build-custom p { margin-top:.25rem; color:var(--hc-muted); }
        .hc-saved-procedure-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,16rem),1fr)); gap:.75rem; }
        .hc-saved-procedure-card { display:grid; grid-template-rows:auto auto 1fr auto; gap:.55rem; align-content:start; }
        .hc-saved-procedure-card h3, .hc-saved-procedure-card p { margin:0; }
        .hc-card-meta { display:flex; gap:.4rem; flex-wrap:wrap; }
        .hc-preset-browser { display:grid; gap:1rem; }
        .hc-preset-browser-header { display:flex; align-items:end; justify-content:space-between; gap:1rem; flex-wrap:wrap; }
        .hc-preset-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,18rem),1fr)); gap:.85rem; align-items:stretch; }
        .hc-preset-card { border:1px solid var(--hc-border); border-radius:.85rem; padding:1rem; display:grid; grid-template-rows:auto 1fr auto auto; gap:.75rem; background:var(--hc-surface); color:var(--hc-text); min-width:0; }
        .hc-preset-card h3 { margin:0; color:var(--hc-text-strong); }
        .hc-preset-card-head { min-height:4rem; }
        .hc-preset-card-head p { margin:.3rem 0 0; color:var(--hc-muted); }
        .hc-preset-facts, .hc-rule-facts { display:grid; grid-template-columns:minmax(7rem,auto) minmax(0,1fr); gap:.3rem .75rem; margin:0; align-content:start; }
        .hc-preset-facts dt, .hc-rule-facts dt { font-weight:600; color:var(--hc-text-strong); }
        .hc-preset-facts dd, .hc-rule-facts dd { margin:0; overflow-wrap:anywhere; color:var(--hc-text); }
        .hc-preset-actions, .hc-area-actions, .hc-json-actions { display:flex; gap:.5rem; flex-wrap:wrap; align-items:center; }
        .hc-preset-actions { align-self:end; }
        .hc-preset-provenance { color:var(--hc-muted); }
        .hc-inspect-rule { display:grid; grid-template-columns:minmax(9rem,.7fr) minmax(0,1fr); gap:.75rem; padding:.45rem 0; border-bottom:1px solid var(--hc-border); }
        .hc-inspect-rule:last-child { border-bottom:0; }
        .hc-procedure-area-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,17rem),1fr)); gap:.8rem; align-items:stretch; }
        .hc-area-card { width:100%; text-align:left; border:1px solid var(--hc-border); border-radius:.8rem; padding:.95rem; background:var(--hc-surface); color:var(--hc-text); display:grid; gap:.55rem; align-content:start; min-width:0; }
        button.hc-area-card { cursor:pointer; }
        button.hc-area-card:hover { border-color:var(--hc-button-hover-border); background:var(--hc-surface-elevated); }
        .hc-area-card:focus-visible, .hc-procedure-shell button:focus-visible, .hc-procedure-shell input:focus-visible, .hc-procedure-shell select:focus-visible, .hc-procedure-shell textarea:focus-visible { outline:3px solid var(--hc-focus); outline-offset:2px; }
        .hc-area-card h3 { margin:0; color:var(--hc-text-strong); }
        .hc-area-card-heading { display:flex; gap:.5rem; align-items:flex-start; justify-content:space-between; }
        .hc-area-card-summary { margin:0; }
        .hc-area-card-meta { display:flex; gap:.4rem; flex-wrap:wrap; }
        .hc-rule-card { grid-template-rows:auto auto 1fr auto; }
        .hc-rule-card > p { margin:0; color:var(--hc-muted); }
        .hc-table-procedure > p, .hc-rule-group > p { color:var(--hc-muted); }
        .hc-procedure-step-list { display:grid; gap:.75rem; margin:.8rem 0 0; padding-left:2.15rem; }
        .hc-procedure-step-list > li { padding-left:.15rem; }
        .hc-procedure-step-list > li::marker { font-weight:700; color:var(--hc-text-strong); }
        .hc-add-rule-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,15rem),1fr)); gap:.7rem; }
        .hc-add-rule-card { grid-template-rows:auto 1fr auto; }
        .hc-add-rule-card p { margin:0; color:var(--hc-muted); }
        .hc-add-rule-strip { display:flex; gap:.45rem; flex-wrap:wrap; align-items:center; padding-top:.7rem; margin-top:.2rem; border-top:1px solid var(--hc-border); }
        .hc-neutral-procedure { display:grid; gap:.8rem; }
        .hc-neutral-procedure h2, .hc-neutral-procedure p { margin:0; }
        .hc-neutral-procedure p { color:var(--hc-muted); }
        .hc-ux-badge { display:inline-flex; align-items:center; border:1px solid currentColor; border-radius:999px; padding:.12rem .48rem; font-size:.74rem; line-height:1.35; white-space:nowrap; }
        .hc-ux-badge-good { color:var(--hc-success); }
        .hc-ux-badge-warning { color:var(--hc-warning); font-weight:600; }
        .hc-ux-badge-danger { color:var(--hc-danger-text); font-weight:700; }
        .hc-ux-badge-info { color:var(--hc-muted); }
        .hc-focus-workspace { position:fixed; z-index:45; right:clamp(.5rem,2vw,1.5rem); top:clamp(.5rem,2vw,1.5rem); bottom:clamp(.5rem,2vw,1.5rem); width:min(44rem,calc(100vw - 1rem)); border:1px solid var(--hc-border); border-radius:1rem; background:var(--hc-surface); color:var(--hc-text); box-shadow:0 .8rem 2.4rem rgba(0,0,0,.28); display:grid; grid-template-rows:auto minmax(0,1fr); overflow:hidden; color-scheme:inherit; }
        .hc-focus-workspace-header { display:flex; align-items:center; justify-content:space-between; gap:1rem; padding:1rem 1.1rem; border-bottom:1px solid var(--hc-border); background:var(--hc-surface); }
        .hc-focus-workspace-header h2 { margin:0; color:var(--hc-text-strong); }
        .hc-focus-workspace-body { overflow:auto; overscroll-behavior:contain; padding:1rem 1.1rem 2rem; display:grid; gap:1rem; align-content:start; background:var(--hc-surface); }
        .hc-focus-workspace-module { display:grid; gap:.8rem; padding:1rem; border:1px solid var(--hc-border); border-radius:.75rem; background:var(--hc-surface-elevated); color:var(--hc-text); }
        .hc-focus-workspace-module h3, .hc-focus-workspace-module h4 { margin:0; color:var(--hc-text-strong); }
        .hc-compact-field-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,14rem),1fr)); gap:.75rem; }
        .hc-compact-field { display:grid; gap:.3rem; min-width:0; color:var(--hc-text); }
        .hc-compact-field > span { font-weight:600; color:var(--hc-text-strong); }
        .hc-compact-field small { color:var(--hc-muted); }
        .hc-compact-field input:not([type="checkbox"]), .hc-compact-field select, .hc-compact-field textarea,
        .hc-advanced-section select, .hc-json-editor textarea { width:100%; min-width:0; box-sizing:border-box; border:1px solid var(--hc-border); border-radius:.4rem; background:var(--hc-input-bg); color:var(--hc-input-text); padding:.48rem .55rem; }
        .hc-duration-control { display:grid; grid-template-columns:minmax(0,1fr) minmax(7rem,.65fr); gap:.45rem; }
        .hc-readonly-domain-value { min-height:2.2rem; display:flex; align-items:center; padding:.48rem .55rem; border:1px solid var(--hc-border); border-radius:.4rem; background:var(--hc-surface-elevated); color:var(--hc-text); }
        .hc-domain-diagnostic { border-left:3px solid var(--hc-warning); padding:.55rem .7rem; margin:0; background:var(--hc-warning-bg); color:var(--hc-warning-text); }
        .hc-domain-diagnostic strong { display:block; }
        .hc-advanced-layout { display:grid; grid-template-columns:minmax(14rem,19rem) minmax(0,1fr); gap:1rem; align-items:start; }
        .hc-advanced-index { position:sticky; top:.75rem; display:grid; gap:.45rem; }
        .hc-advanced-index > p { margin:0 0 .35rem; color:var(--hc-muted); }
        .hc-advanced-module-toggle { display:grid; grid-template-columns:auto minmax(0,1fr); gap:.35rem .5rem; align-items:center; padding:.45rem; border-radius:.4rem; }
        .hc-advanced-module-toggle:hover { background:var(--hc-surface-elevated); }
        .hc-advanced-module-toggle code { grid-column:2; color:var(--hc-muted); overflow-wrap:anywhere; }
        .hc-advanced-module { display:grid; gap:.85rem; }
        .hc-advanced-module-heading { display:flex; justify-content:space-between; gap:.75rem; align-items:flex-start; }
        .hc-advanced-module-heading h2, .hc-advanced-module-heading p { margin:0; }
        .hc-advanced-module-heading p { margin-top:.3rem; color:var(--hc-muted); }
        .hc-advanced-module-heading code, .hc-advanced-section code { color:var(--hc-muted); overflow-wrap:anywhere; }
        .hc-advanced-section { display:grid; gap:.55rem; padding-top:.75rem; border-top:1px solid var(--hc-border); }
        .hc-advanced-section h3, .hc-advanced-section p { margin:0; }
        .hc-advanced-section p { color:var(--hc-muted); }
        .hc-advanced-parameter-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,16rem),1fr)); gap:.7rem; }
        .hc-advanced-contracts { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:.75rem; }
        .hc-json-editor { display:grid; gap:.75rem; }
        .hc-json-editor textarea { min-height:32rem; max-height:70vh; resize:vertical; font-family:ui-monospace,SFMono-Regular,Menlo,Monaco,Consolas,"Liberation Mono",monospace; font-size:.86rem; line-height:1.45; tab-size:2; }
        .hc-json-status { min-height:1.4em; margin:0; color:var(--hc-muted); }
        .hc-json-status.is-error { font-weight:600; color:var(--hc-error-text); }
        .hc-json-status.is-valid { color:var(--hc-success); }
        .hc-ux-disclosure { border-top:1px solid var(--hc-border); padding-top:.5rem; }
        .hc-ux-disclosure > summary { cursor:pointer; font-weight:600; color:var(--hc-text-strong); }
        .hc-procedure-shell button:disabled, .hc-focus-workspace button:disabled { border-color:var(--hc-disabled-border); background:var(--hc-disabled-bg); color:var(--hc-disabled-text); cursor:not-allowed; }
        .hc-stat-action-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,11.5rem),1fr)); gap:.55rem; }
        .hc-stat-action { text-align:left; display:grid; gap:.18rem; min-width:0; border:1px solid var(--hc-border); border-radius:.68rem; padding:.7rem .78rem; background:var(--hc-surface); color:var(--hc-text); }
        .hc-stat-action-label { font-size:.74rem; text-transform:uppercase; letter-spacing:.045em; color:var(--hc-muted); }
        .hc-stat-action-value { overflow-wrap:anywhere; }
        .hc-stat-action-detail { font-size:.78rem; color:var(--hc-muted); overflow-wrap:anywhere; }
        .hc-stat-action.is-warning, .hc-stat-action.is-danger { border-width:2px; }
        .hc-current-action { border:2px solid var(--hc-border); border-radius:.85rem; padding:1rem; display:grid; gap:.7rem; background:var(--hc-surface); color:var(--hc-text); }
        .hc-current-action header { display:flex; justify-content:space-between; align-items:start; gap:.75rem; flex-wrap:wrap; }
        .hc-current-action h2 { margin:0; color:var(--hc-text-strong); }
        .hc-current-action-primary { font-size:1.05rem; margin:0; }
        .hc-current-action-detail { margin:0; color:var(--hc-muted); }
        .hc-phase15-expedition { display:grid; gap:var(--hc-ux-gap); }
        .hc-phase15-expedition .hc-workspace-grid { grid-template-columns:minmax(0,1fr) minmax(18rem,23rem); align-items:start; }
        .hc-phase15-expedition .hc-map-sheet-top { grid-template-columns:minmax(0,1.5fr) minmax(17rem,.8fr); }
        .hc-phase15-expedition .hc-sidebar { display:grid; gap:.7rem; align-content:start; }
        .hc-phase15-expedition .hc-running-sheet { display:grid; gap:.75rem; }
        .hc-phase15-expedition .hc-sheet-status { display:none; }
        .hc-phase15-runtime-summary { display:grid; gap:.65rem; }
        .hc-phase15-context-strip { display:flex; gap:.5rem; flex-wrap:wrap; }
        .hc-context-button { border-radius:999px; padding:.35rem .65rem; }
        .hc-context-card { display:grid; gap:.65rem; padding:.8rem; border:1px solid var(--hc-border); border-radius:.7rem; background:var(--hc-surface); color:var(--hc-text); }
        .hc-nonspatial-primary { display:grid; grid-template-columns:minmax(0,1.4fr) minmax(17rem,.75fr); gap:1rem; align-items:start; }
        .hc-journey-primary { border:1px solid var(--hc-border); border-radius:.85rem; padding:1rem; display:grid; gap:.65rem; background:var(--hc-surface); color:var(--hc-text); }
        .hc-phase15-empty { padding:1rem; border:1px dashed var(--hc-border); border-radius:.7rem; color:var(--hc-muted); background:var(--hc-surface); }
        @media (max-width: 1040px) {
            .hc-phase15-expedition .hc-workspace-grid,
            .hc-nonspatial-primary { grid-template-columns:1fr; }
            .hc-phase15-expedition .hc-map-sheet-top { grid-template-columns:1fr; }
        }
        @media (max-width: 760px) {
            .hc-build-custom { grid-template-columns:1fr; }
            .hc-advanced-layout { grid-template-columns:1fr; }
            .hc-advanced-index { position:static; }
            .hc-advanced-contracts { grid-template-columns:1fr; }
            .hc-focus-workspace { inset:.25rem; width:auto; border-radius:.7rem; }
            .hc-json-editor textarea { min-height:22rem; }
            .hc-preset-grid, .hc-procedure-area-grid, .hc-stat-action-grid, .hc-saved-procedure-grid { grid-template-columns:1fr; }
            .hc-preset-card-head { min-height:0; }
            .hc-duration-control { grid-template-columns:1fr; }
            .hc-inspect-rule { grid-template-columns:1fr; gap:.2rem; }
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
