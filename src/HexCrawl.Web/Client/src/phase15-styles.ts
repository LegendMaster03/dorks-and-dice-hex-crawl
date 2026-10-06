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
        .hc-saved-procedure-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,18rem),1fr)); gap:.75rem; }
        .hc-saved-procedure-card { display:grid; gap:.55rem; align-content:start; min-width:0; }
        .hc-saved-procedure-card h3, .hc-saved-procedure-card p { margin:0; }
        .hc-saved-procedure-head { display:flex; align-items:flex-start; justify-content:space-between; gap:.6rem; flex-wrap:wrap; }
        .hc-saved-procedure-head h3 { min-width:0; overflow-wrap:anywhere; }
        .hc-saved-procedure-summary { display:grid; grid-template-columns:auto minmax(0,1fr); gap:.25rem .65rem; margin:0; font-size:.9rem; }
        .hc-saved-procedure-summary dt { font-weight:600; color:var(--hc-text-strong); }
        .hc-saved-procedure-summary dd { margin:0; color:var(--hc-muted); overflow-wrap:anywhere; }
        .hc-saved-procedure-actions { display:flex; justify-content:flex-end; }
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
        .hc-inspect-rules { gap:0; }
        .hc-inspect-rule { display:grid; gap:.4rem; padding:.8rem 0; border-bottom:1px solid var(--hc-border); min-width:0; }
        .hc-inspect-rule:first-of-type { padding-top:.25rem; }
        .hc-inspect-rule:last-child { border-bottom:0; padding-bottom:.25rem; }
        .hc-inspect-rule h4, .hc-inspect-rule p { margin:0; }
        .hc-inspect-rule > p { color:var(--hc-muted); }
        .hc-inspect-rule-facts { margin-top:.15rem; }
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
        .hc-procedure-step-list { display:grid; gap:.75rem; margin:.8rem 0 0; }
        .hc-add-rule-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,15rem),1fr)); gap:.7rem; }
        .hc-add-rule-card { grid-template-rows:auto 1fr auto; }
        .hc-add-rule-card p { margin:0; color:var(--hc-muted); }
        .hc-add-rule-strip { display:flex; gap:.45rem; flex-wrap:wrap; align-items:center; padding-top:.7rem; margin-top:.2rem; border-top:1px solid var(--hc-border); }
        .hc-neutral-procedure { display:grid; gap:.8rem; }
        .hc-neutral-procedure h2, .hc-neutral-procedure p { margin:0; }
        .hc-neutral-procedure p { color:var(--hc-muted); }
        .hc-rule-library { display:grid; gap:.7rem; }
        .hc-rule-library > summary { cursor:pointer; font-weight:700; color:var(--hc-text-strong); }
        .hc-rule-library > p { margin:0; color:var(--hc-muted); }
        .hc-rule-library-groups { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,18rem),1fr)); gap:.75rem; margin-top:.7rem; }
        .hc-rule-library-group { display:grid; gap:.45rem; align-content:start; min-width:0; padding:.75rem; border:1px solid var(--hc-border); border-radius:.65rem; background:var(--hc-surface-elevated); color:var(--hc-text); }
        .hc-rule-library-group h3, .hc-rule-library-group p { margin:0; }
        .hc-rule-library-group p { color:var(--hc-muted); }
        .hc-rule-library-group .hc-add-rule-strip { margin-top:.1rem; }
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
        .hc-advanced-layout { display:grid; grid-template-columns:minmax(20rem,24rem) minmax(0,1fr); gap:1rem; align-items:start; }
        .hc-advanced-index { position:sticky; top:.75rem; display:grid; gap:.65rem; max-height:calc(100vh - 1.5rem); overflow:auto; }
        .hc-advanced-index > h2, .hc-advanced-index > p { margin:0; }
        .hc-advanced-index > p { color:var(--hc-muted); }
        .hc-advanced-index-group { display:grid; gap:.3rem; padding-top:.45rem; border-top:1px solid var(--hc-border); }
        .hc-advanced-index-group h3 { margin:0; font-size:.82rem; text-transform:uppercase; letter-spacing:.045em; color:var(--hc-muted); }
        .hc-advanced-module-row { display:grid; grid-template-columns:auto minmax(0,1fr); gap:.45rem; align-items:center; padding:.25rem; border:1px solid transparent; border-radius:.55rem; min-width:0; }
        .hc-advanced-module-row:hover { background:var(--hc-surface-elevated); }
        .hc-advanced-module-row.is-selected { border-color:var(--hc-focus); background:var(--hc-surface-elevated); }
        .hc-advanced-module-row > input { margin:0; }
        .hc-advanced-module-select { width:100%; min-width:0; display:grid; gap:.12rem; justify-items:start; text-align:left; padding:.42rem .5rem; }
        .hc-advanced-module-select span { color:var(--hc-text-strong); font-weight:600; }
        .hc-advanced-module-select code { display:block; max-width:100%; color:var(--hc-muted); overflow-wrap:anywhere; white-space:normal; font-size:.78rem; }
        .hc-advanced-content { min-width:0; }
        .hc-advanced-module { display:grid; gap:.85rem; min-width:0; }
        .hc-advanced-module > code { color:var(--hc-muted); overflow-wrap:anywhere; white-space:normal; }
        .hc-advanced-module-heading { display:flex; justify-content:space-between; gap:.75rem; align-items:flex-start; }
        .hc-advanced-module-heading h2, .hc-advanced-module-heading p { margin:0; }
        .hc-advanced-module-heading p { margin-top:.3rem; color:var(--hc-muted); }
        .hc-advanced-module-heading code, .hc-advanced-section code { color:var(--hc-muted); overflow-wrap:anywhere; white-space:normal; }
        .hc-advanced-section { display:grid; gap:.55rem; padding-top:.75rem; border-top:1px solid var(--hc-border); }
        .hc-advanced-section h3, .hc-advanced-section p { margin:0; }
        .hc-advanced-section p { color:var(--hc-muted); }
        .hc-advanced-facts { display:grid; grid-template-columns:minmax(8rem,auto) minmax(0,1fr); gap:.35rem .7rem; margin:0; }
        .hc-advanced-facts dt { font-weight:600; color:var(--hc-text-strong); }
        .hc-advanced-facts dd { margin:0; min-width:0; color:var(--hc-text); overflow-wrap:anywhere; }
        .hc-advanced-parameter-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,16rem),1fr)); gap:.7rem; }
        .hc-advanced-contracts { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:.75rem; }
        .hc-advanced-contracts > * { min-width:0; }
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
        .hc-phase15-expedition.hc-page { max-width:none; }
        .hc-phase15-expedition.hc-page .hc-workspace-grid { grid-template-columns:minmax(0,1fr) minmax(15rem,19rem); }
        .hc-phase15-expedition .hc-map-host { height:clamp(28rem,58vh,46rem); min-height:0; }
        .hc-phase15-expedition .hc-map-canvas { height:100%; min-height:0; }
        .hc-map-frame { position:relative; min-width:0; overflow:hidden; border-radius:.55rem; }
        .hc-adjacency-navigator { position:absolute; z-index:6; top:.8rem; left:.8rem; width:9.5rem; aspect-ratio:1; pointer-events:none; filter:drop-shadow(0 .25rem .45rem rgba(0,0,0,.26)); }
        .hc-adjacency-cell { position:absolute; inset:0; width:100%; height:100%; overflow:visible; pointer-events:none; }
        .hc-adjacency-cell polygon { fill:color-mix(in srgb,var(--hc-surface) 88%,transparent); stroke:var(--hc-text-strong); stroke-width:8; stroke-linejoin:round; vector-effect:non-scaling-stroke; }
        .hc-adjacency-edge-mark { stroke:color-mix(in srgb,var(--hc-text-strong) 70%,transparent); stroke-width:8; stroke-linecap:round; vector-effect:non-scaling-stroke; }
        .hc-adjacency-edge-mark.is-selected { stroke:var(--hc-focus); stroke-width:13; }
        .hc-adjacency-caption { position:absolute; inset:50% auto auto 50%; transform:translate(-50%,-50%); width:4.4rem; text-align:center; font-size:.7rem; font-weight:700; line-height:1.1; color:var(--hc-text-strong); pointer-events:none; }
        .hc-adjacency-edge { position:absolute; pointer-events:auto; left:var(--hc-edge-x); top:var(--hc-edge-y); transform:translate(-50%,-50%); min-width:2.55rem; min-height:2.55rem; padding:.2rem .4rem; border:3px solid var(--hc-text-strong); border-radius:999px; background:var(--hc-surface); color:var(--hc-text-strong); font-weight:800; line-height:1; box-shadow:0 .12rem .35rem rgba(0,0,0,.22); }
        .hc-adjacency-edge:hover:not(:disabled) { transform:translate(-50%,-50%) scale(1.08); border-color:var(--hc-focus); background:var(--hc-surface-elevated); }
        .hc-adjacency-edge:focus-visible { transform:translate(-50%,-50%) scale(1.08); outline:4px solid var(--hc-focus); outline-offset:3px; z-index:2; }
        #tool-root.hex-crawl-app button.hc-adjacency-edge:active:not(:disabled) { transform:translate(calc(-50% + var(--hc-edge-feedback-x)),calc(-50% + var(--hc-edge-feedback-y))) scale(1.04); }
        .hc-adjacency-edge.is-selected { border-color:var(--hc-focus); background:var(--hc-text-strong); color:var(--hc-surface); box-shadow:0 0 0 3px var(--hc-focus),0 .18rem .45rem rgba(0,0,0,.3); }
        .hc-adjacency-edge.is-actual-course:not(.is-selected) { border-style:dashed; border-color:var(--hc-warning); }
        .hc-adjacency-edge:disabled { border-color:var(--hc-disabled-border); background:var(--hc-disabled-bg); color:var(--hc-disabled-text); cursor:not-allowed; }
        .hc-map-context-overlay { position:absolute; z-index:5; right:.75rem; bottom:.75rem; width:min(22rem,calc(100% - 1.5rem)); max-height:min(18rem,55%); overflow:auto; display:grid; gap:.5rem; padding:.7rem .8rem; border:1px solid var(--hc-border); border-radius:.7rem; background:color-mix(in srgb,var(--hc-surface) 92%,transparent); color:var(--hc-text); box-shadow:0 .3rem .9rem rgba(0,0,0,.2); }
        .hc-map-context-overlay h3, .hc-map-context-overlay p { margin:0; }
        .hc-current-travel { display:grid; gap:.6rem; padding:.75rem .85rem; border:1px solid var(--hc-border); border-radius:.7rem; background:var(--hc-surface-elevated); }
        .hc-current-travel h3, .hc-current-travel p { margin:0; }
        .hc-current-travel-facts { display:grid; grid-template-columns:auto minmax(0,1fr) auto minmax(0,1fr); gap:.2rem .65rem; margin:0; align-items:baseline; }
        .hc-current-travel-facts dt { font-size:.74rem; font-weight:700; text-transform:uppercase; letter-spacing:.04em; color:var(--hc-muted); }
        .hc-current-travel-facts dd { margin:0; min-width:0; color:var(--hc-text-strong); overflow-wrap:anywhere; }
        .hc-current-travel-actions { align-items:center; }
        .hc-current-travel-pace-editor { display:grid; grid-template-columns:minmax(12rem,1fr) auto; gap:.55rem; align-items:end; }
        .hc-current-travel-pace-editor label { display:grid; gap:.3rem; font-weight:600; }
        .hc-current-travel-pace-editor input { width:100%; min-width:0; box-sizing:border-box; }
        .hc-table-rail { padding:.75rem; gap:.45rem !important; }
        .hc-table-rail > h2 { margin:0 0 .15rem; }
        .hc-rail-action { width:100%; min-width:0; display:grid; gap:.12rem; padding:.55rem .6rem; text-align:left; border-radius:.55rem; }
        .hc-rail-action strong, .hc-rail-action span { min-width:0; overflow-wrap:anywhere; }
        .hc-rail-action span { font-size:.78rem; }
        .hc-gm-tools { opacity:.86; }
        .hc-focused-resolution-summary { display:grid; gap:.45rem; padding:.8rem; border:1px solid var(--hc-border); border-radius:.7rem; background:var(--hc-surface-elevated); }
        .hc-focused-resolution-summary h3, .hc-focused-resolution-summary p { margin:0; }
        .hc-phase15-runtime-summary { display:grid; gap:.65rem; }
        .hc-phase15-context-strip { display:flex; gap:.5rem; flex-wrap:wrap; }
        .hc-context-button { border-radius:999px; padding:.35rem .65rem; }
        .hc-context-card { display:grid; gap:.65rem; padding:.8rem; border:1px solid var(--hc-border); border-radius:.7rem; background:var(--hc-surface); color:var(--hc-text); }
        .hc-nonspatial-primary { display:grid; grid-template-columns:minmax(0,1.4fr) minmax(17rem,.75fr); gap:1rem; align-items:start; }
        .hc-journey-primary { border:1px solid var(--hc-border); border-radius:.85rem; padding:1rem; display:grid; gap:.65rem; background:var(--hc-surface); color:var(--hc-text); }
        .hc-phase15-empty { padding:1rem; border:1px dashed var(--hc-border); border-radius:.7rem; color:var(--hc-muted); background:var(--hc-surface); }
        @media (max-width: 1040px) {
            .hc-phase15-expedition .hc-workspace-grid,
            .hc-phase15-expedition.hc-page .hc-workspace-grid,
            .hc-nonspatial-primary { grid-template-columns:1fr; }
            .hc-phase15-expedition .hc-map-sheet-top { grid-template-columns:1fr; }
            .hc-phase15-expedition .hc-map-host { height:clamp(24rem,52vh,36rem); min-height:0; }
            .hc-table-rail { grid-template-columns:repeat(2,minmax(0,1fr)); }
            .hc-table-rail > h2 { grid-column:1 / -1; }
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
            .hc-advanced-index { max-height:none; }
            .hc-phase15-expedition .hc-map-host { height:clamp(20rem,48vh,30rem); min-height:0; }
            .hc-adjacency-navigator { width:7.5rem; top:.55rem; left:.55rem; }
            .hc-adjacency-caption { width:3.4rem; font-size:.62rem; }
            .hc-adjacency-edge { min-width:2.25rem; min-height:2.25rem; font-size:.78rem; }
            .hc-current-travel-facts { grid-template-columns:auto minmax(0,1fr); }
            .hc-current-travel-pace-editor { grid-template-columns:1fr; }
            .hc-map-context-overlay { max-height:48%; right:.5rem; bottom:.5rem; width:calc(100% - 1rem); }
            .hc-table-rail { grid-template-columns:1fr; }
            .hc-advanced-facts { grid-template-columns:1fr; gap:.15rem; }
            .hc-advanced-facts dd { margin-bottom:.35rem; }
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
