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
        .hc-rule-fact-groups { display:grid; gap:.55rem; min-width:0; }
        .hc-rule-fact-group { display:grid; gap:.3rem; min-width:0; padding-top:.45rem; border-top:1px solid var(--hc-border); }
        .hc-rule-fact-group:first-child { border-top:0; padding-top:0; }
        .hc-rule-fact-group h4 { margin:0; font-size:.76rem; text-transform:uppercase; letter-spacing:.045em; color:var(--hc-muted); }
        .hc-rule-map { width:100%; border-collapse:collapse; font-size:.9rem; }
        .hc-rule-map th, .hc-rule-map td { padding:.18rem .3rem; border-bottom:1px solid var(--hc-border); text-align:left; vertical-align:top; }
        .hc-rule-map th { width:42%; font-weight:650; color:var(--hc-text-strong); }
        .hc-rule-value-list { margin:0; padding-left:1.1rem; display:grid; gap:.1rem; }
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
        @media (min-width: 1041px) {
            .hc-current-action { grid-template-columns:minmax(11rem,.48fr) minmax(0,1fr) auto; align-items:center; gap:.35rem .9rem; padding:.72rem .85rem; }
            .hc-current-action header { grid-column:1; grid-row:1 / span 2; display:grid; justify-content:start; gap:.3rem; align-content:center; }
            .hc-current-action-primary { grid-column:2; grid-row:1; align-self:end; }
            .hc-current-action-detail { grid-column:2; grid-row:2; align-self:start; }
            .hc-current-action > .hc-button-row { grid-column:3; grid-row:1 / span 2; justify-content:flex-end; align-self:center; }
            .hc-current-action header .hc-ux-badge { justify-self:start; width:max-content; }
            .hc-current-travel { grid-template-columns:auto minmax(0,1fr) auto; align-items:center; gap:.28rem .8rem; padding:.55rem .7rem; }
            .hc-current-travel > h3 { grid-column:1; grid-row:1; white-space:nowrap; }
            .hc-current-travel-facts { grid-column:2; grid-row:1; gap:.12rem .4rem; }
            .hc-current-travel > p { grid-column:1 / 3; grid-row:2; }
            .hc-current-travel-actions { grid-column:3; grid-row:1 / span 2; justify-content:flex-end; flex-wrap:nowrap; }
            .hc-current-travel-pace-editor { grid-column:1 / -1; grid-row:3; }
            .hc-table-rail .hc-current-travel { grid-template-columns:1fr; align-items:stretch; }
            .hc-table-rail .hc-current-travel > h3,
            .hc-table-rail .hc-current-travel-facts,
            .hc-table-rail .hc-current-travel > p,
            .hc-table-rail .hc-current-travel-actions,
            .hc-table-rail .hc-current-travel-pace-editor { grid-column:1; grid-row:auto; }
            .hc-table-rail .hc-current-travel-facts { grid-template-columns:auto minmax(0,1fr); }
            .hc-table-rail .hc-current-travel-actions { justify-content:flex-start; flex-wrap:wrap; }
            .hc-phase15-expedition .hc-stat-action-grid { grid-template-columns:repeat(8,minmax(0,1fr)); }
            .hc-phase15-expedition .hc-stat-action { padding:.58rem .62rem; }
            .hc-phase15-expedition .hc-stat-action-detail { font-size:.73rem; }
        }
        @media (min-width: 1041px) and (max-width: 1199px) {
            .hc-current-travel { grid-template-columns:minmax(0,1fr) auto; }
            .hc-current-travel > h3 { grid-column:1; grid-row:1; }
            .hc-current-travel-actions { grid-column:2; grid-row:1; justify-content:flex-end; }
            .hc-current-travel-facts {
                grid-column:1 / -1;
                grid-row:2;
                grid-template-columns:repeat(4,auto minmax(0,1fr));
            }
            .hc-current-travel > p { grid-column:1 / -1; grid-row:3; }
            .hc-current-travel-pace-editor { grid-column:1 / -1; grid-row:4; }
        }
        @media (min-width: 1200px) {
            .hc-current-travel-facts { grid-template-columns:repeat(4,auto minmax(0,1fr)); }
        }
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
        .hc-adjacency-navigator { position:absolute; z-index:6; top:.8rem; left:.8rem; width:8.5rem; aspect-ratio:1; pointer-events:none; filter:drop-shadow(0 .2rem .4rem rgba(0,0,0,.24)); }
        .hc-adjacency-cell { position:absolute; inset:0; width:100%; height:100%; overflow:visible; pointer-events:none; }
        .hc-adjacency-cell polygon { fill:color-mix(in srgb,var(--hc-surface) 36%,transparent); stroke:var(--hc-text-strong); stroke-width:5; stroke-linejoin:round; vector-effect:non-scaling-stroke; }
        #tool-root.hex-crawl-app button.hc-adjacency-interface { position:absolute; pointer-events:auto; left:var(--hc-adjacency-x); top:var(--hc-adjacency-y); transform:translate(-50%,-50%); display:grid; place-items:center; width:2.75rem; height:2.75rem; min-width:2.75rem; min-height:2.75rem; padding:0; border:0; border-radius:.35rem; background:transparent; color:inherit; line-height:1; box-shadow:none; cursor:pointer; }
        .hc-adjacency-arrow-shape { width:2.45rem; height:1.4rem; overflow:visible; transform:rotate(var(--hc-adjacency-angle)); transform-origin:center; pointer-events:none; filter:drop-shadow(0 .1rem .13rem rgba(0,0,0,.58)); }
        .hc-adjacency-arrow-shape path { fill:#fff; stroke:transparent; stroke-width:4; stroke-linejoin:round; paint-order:stroke fill; vector-effect:non-scaling-stroke; }
        #tool-root.hex-crawl-app button.hc-adjacency-interface:hover:not(:disabled) { transform:translate(-50%,-50%) scale(1.06); border-color:transparent; background:transparent; }
        .hc-adjacency-interface:hover:not(:disabled) .hc-adjacency-arrow-shape path { stroke:var(--hc-primary); }
        #tool-root.hex-crawl-app button.hc-adjacency-interface:focus-visible { transform:translate(-50%,-50%) scale(1.06); border-color:transparent; outline:3px solid var(--hc-primary); outline-offset:2px; background:transparent; z-index:2; }
        .hc-adjacency-interface:focus-visible .hc-adjacency-arrow-shape path { stroke:var(--hc-primary); }
        #tool-root.hex-crawl-app button.hc-adjacency-interface:active:not(:disabled) { transform:translate(calc(-50% + var(--hc-adjacency-feedback-x)),calc(-50% + var(--hc-adjacency-feedback-y))) scale(1.02); border-color:transparent; background:transparent; }
        #tool-root.hex-crawl-app button.hc-adjacency-interface:active:not(:disabled) .hc-adjacency-arrow-shape path { stroke:var(--hc-primary); }
        .hc-adjacency-interface.is-selected .hc-adjacency-arrow-shape path { stroke:var(--hc-primary); }
        .hc-adjacency-interface.is-actual-course:not(.is-selected) .hc-adjacency-arrow-shape path { stroke:var(--hc-warning); }
        #tool-root.hex-crawl-app button.hc-adjacency-interface:disabled { border-color:transparent; background:transparent; color:inherit; opacity:.45; cursor:not-allowed; }
        .hc-map-context-overlay { position:absolute; z-index:5; right:.75rem; bottom:.75rem; width:min(22rem,calc(100% - 1.5rem)); max-height:min(18rem,55%); overflow:auto; display:grid; gap:.5rem; padding:.7rem .8rem; border:1px solid var(--hc-border); border-radius:.7rem; background:color-mix(in srgb,var(--hc-surface) 92%,transparent); color:var(--hc-text); box-shadow:0 .3rem .9rem rgba(0,0,0,.2); }
        .hc-map-context-overlay h3, .hc-map-context-overlay p { margin:0; }
        .hc-current-travel { display:grid; gap:.6rem; padding:.75rem .85rem; border:1px solid var(--hc-border); border-radius:.7rem; background:var(--hc-surface-elevated); }
        .hc-current-travel h3, .hc-current-travel p { margin:0; }
        .hc-cell-progress { display:grid; gap:.3rem; min-width:0; padding:.5rem .6rem; border:1px solid var(--hc-border); border-radius:.55rem; background:var(--hc-surface); }
        .hc-progress-heading { display:flex; justify-content:space-between; gap:.65rem; align-items:baseline; flex-wrap:wrap; }
        .hc-ledger-kicker { font-size:.72rem; font-weight:800; text-transform:uppercase; letter-spacing:.045em; color:var(--hc-muted); }
        .hc-cell-progress progress, .hc-stage-content progress { width:100%; height:.65rem; accent-color:var(--hc-primary); }
        .hc-cell-progress.is-unbounded { border-style:dashed; }
        .hc-readable-facts { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:.45rem; margin:.55rem 0; }
        .hc-readable-facts > div { min-width:0; padding:.55rem .6rem; border:1px solid var(--hc-border); border-radius:.55rem; background:var(--hc-surface-elevated); }
        .hc-readable-facts dt { font-size:.72rem; font-weight:800; text-transform:uppercase; letter-spacing:.04em; color:var(--hc-muted); }
        .hc-readable-facts dd { margin:.12rem 0 0; min-width:0; color:var(--hc-text-strong); overflow-wrap:anywhere; }
        .hc-readable-table { width:100%; border-collapse:collapse; margin:.55rem 0; font-size:.88rem; }
        .hc-readable-table caption { text-align:left; padding:0 0 .35rem; font-weight:750; color:var(--hc-text-strong); }
        .hc-readable-table th, .hc-readable-table td { padding:.4rem .45rem; border-bottom:1px solid var(--hc-border); text-align:left; vertical-align:top; overflow-wrap:normal; word-break:normal; }
        .hc-readable-table thead th { font-size:.72rem; text-transform:uppercase; letter-spacing:.04em; color:var(--hc-muted); white-space:nowrap; }
        .hc-readable-table tbody th { font-weight:650; color:var(--hc-text-strong); white-space:nowrap; }
        .hc-form select[multiple] { min-height:4.5rem; }
        .hc-movement-composition-ledger { display:grid; gap:.65rem; min-width:0; padding:.75rem; border:1px solid var(--hc-border); border-radius:.7rem; background:var(--hc-surface-elevated); }
        .hc-movement-composition-ledger > header { display:flex; justify-content:space-between; align-items:flex-start; gap:.75rem; flex-wrap:wrap; }
        .hc-movement-composition-ledger > header > div { display:grid; gap:.1rem; }
        .hc-ledger-effective { font-size:1.15rem; color:var(--hc-text-strong); }
        .hc-ledger-status { border:1px solid var(--hc-border); border-radius:999px; padding:.16rem .5rem; font-size:.78rem; }
        .hc-movement-ledger-summary { display:grid; grid-template-columns:auto minmax(0,1fr); gap:.22rem .7rem; margin:0; }
        .hc-movement-ledger-summary dt { font-weight:650; color:var(--hc-text-strong); }
        .hc-movement-ledger-summary dd { margin:0; min-width:0; overflow-wrap:anywhere; }
        .hc-movement-ledger-table { width:100%; border-collapse:collapse; font-size:.88rem; }
        .hc-movement-ledger-table th, .hc-movement-ledger-table td { padding:.38rem .45rem; border-bottom:1px solid var(--hc-border); text-align:left; vertical-align:top; overflow-wrap:normal; word-break:normal; }
        .hc-movement-ledger-table thead th { font-size:.72rem; text-transform:uppercase; letter-spacing:.04em; color:var(--hc-muted); white-space:nowrap; }
        .hc-movement-ledger-table tbody th { font-weight:650; color:var(--hc-text-strong); white-space:nowrap; }
        .hc-movement-ledger-table small { color:var(--hc-muted); font-weight:400; }
        .hc-movement-ledger-diagnostic { margin:0; padding:.55rem .65rem; border-inline-start:3px solid var(--hc-warning); background:var(--hc-surface); }
        .hc-journey-stage-sequence { display:grid; gap:.45rem; }
        .hc-journey-stage-sequence h4 { margin:0; }
        .hc-stage-sequence { list-style:none; margin:0; padding:0; display:grid; gap:.4rem; }
        .hc-stage-step { display:grid; grid-template-columns:1.7rem minmax(0,1fr); gap:.5rem; align-items:start; padding:.5rem .55rem; border:1px solid var(--hc-border); border-radius:.55rem; background:var(--hc-surface); }
        .hc-stage-step.is-current { border-width:2px; }
        .hc-stage-step.is-complete { opacity:.78; }
        .hc-stage-marker { width:1.7rem; height:1.7rem; display:grid; place-items:center; border:1px solid var(--hc-border); border-radius:999px; font-weight:800; }
        .hc-stage-step.is-current .hc-stage-marker { border-color:var(--hc-primary); }
        .hc-stage-content { display:grid; gap:.18rem; min-width:0; }
        .hc-stage-content > span { overflow-wrap:anywhere; }
        .hc-stage-progress-label { font-size:.86rem; font-weight:650; color:var(--hc-text-strong); }
        .hc-current-travel-facts { display:grid; grid-template-columns:auto minmax(0,1fr) auto minmax(0,1fr); gap:.2rem .65rem; margin:0; align-items:baseline; }
        .hc-current-travel-facts dt { font-size:.74rem; font-weight:700; text-transform:uppercase; letter-spacing:.04em; color:var(--hc-muted); }
        .hc-current-travel-facts dd { margin:0; min-width:0; color:var(--hc-text-strong); overflow-wrap:anywhere; }
        .hc-current-travel-actions { align-items:center; }
        .hc-current-travel-pace-editor { display:grid; grid-template-columns:minmax(12rem,1fr) auto; gap:.55rem; align-items:end; }
        .hc-current-travel-pace-editor label { display:grid; gap:.3rem; font-weight:600; }
        .hc-current-travel-pace-editor input,
        .hc-current-travel-pace-editor select { width:100%; min-width:0; box-sizing:border-box; }
        .hc-fixed-travel-mode { display:inline-flex; align-items:center; min-height:2.25rem; font-weight:600; color:var(--hc-text-strong); }
        .hc-table-rail { padding:.75rem; gap:.45rem !important; }
        .hc-table-rail > .hc-current-travel { min-width:0; }
        .hc-table-rail > h2 { margin:0 0 .15rem; }
        .hc-rail-action { width:100%; min-width:0; display:grid; gap:.12rem; padding:.55rem .6rem; text-align:left; border-radius:.55rem; }
        .hc-rail-action strong, .hc-rail-action span { min-width:0; overflow-wrap:anywhere; }
        .hc-rail-action span { font-size:.78rem; }
        .hc-gm-tools { opacity:.86; }
        .hc-focused-hidden { display:none !important; }
        .hc-focused-resolution-summary { display:grid; gap:.45rem; padding:.8rem; border:1px solid var(--hc-border); border-radius:.7rem; background:var(--hc-surface-elevated); }
        .hc-focused-resolution-summary h3, .hc-focused-resolution-summary p { margin:0; }
        .hc-phase15-runtime-summary { display:grid; gap:.65rem; }
        .hc-phase15-context-strip { display:flex; gap:.5rem; flex-wrap:wrap; }
        .hc-context-button { border-radius:999px; padding:.35rem .65rem; }
        .hc-context-card { display:grid; gap:.65rem; padding:.8rem; border:1px solid var(--hc-border); border-radius:.7rem; background:var(--hc-surface); color:var(--hc-text); }
        .hc-nonspatial-primary { display:grid; grid-template-columns:minmax(0,1.4fr) minmax(17rem,.75fr); gap:1rem; align-items:start; }
        .hc-journey-primary { border:1px solid var(--hc-border); border-radius:.85rem; padding:1rem; display:grid; gap:.65rem; background:var(--hc-surface); color:var(--hc-text); }
        .hc-journey-primary-head { display:grid; gap:.3rem; }
        .hc-journey-primary-head h3, .hc-journey-primary-head p { margin:0; }
        .hc-journey-primary-head .hc-ux-badge { justify-self:start; }
        .hc-journey-state-grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:.55rem; margin:0; }
        .hc-journey-fact { min-width:0; display:grid; gap:.12rem; padding:.65rem .7rem; border:1px solid var(--hc-border); border-radius:.6rem; background:var(--hc-surface-elevated); }
        .hc-journey-fact dt { font-size:.72rem; font-weight:800; text-transform:uppercase; letter-spacing:.04em; color:var(--hc-muted); }
        .hc-journey-fact dd { margin:0; min-width:0; color:var(--hc-text-strong); overflow-wrap:anywhere; }
        .hc-phase15-empty { padding:1rem; border:1px dashed var(--hc-border); border-radius:.7rem; color:var(--hc-muted); background:var(--hc-surface); }
        @media (max-width: 1040px) {
            .hc-phase15-expedition .hc-workspace-grid,
            .hc-phase15-expedition.hc-page .hc-workspace-grid,
            .hc-nonspatial-primary { grid-template-columns:1fr; }
            .hc-phase15-expedition .hc-map-sheet-top { grid-template-columns:1fr; }
            .hc-phase15-expedition .hc-map-host { height:clamp(24rem,52vh,36rem); min-height:0; }
            .hc-table-rail { grid-template-columns:repeat(2,minmax(0,1fr)); }
            .hc-table-rail > h2,
            .hc-table-rail > .hc-current-travel { grid-column:1 / -1; }
        }
        @media (max-width: 760px) {
            .hc-phase15-expedition > .hc-current-action { order:1; }
            .hc-phase15-expedition > .hc-workspace-grid,
            .hc-phase15-expedition > .hc-nonspatial-primary { order:2; }
            .hc-phase15-expedition > .hc-phase15-runtime-summary { order:3; }
            .hc-phase15-expedition > .hc-ux-disclosure { order:4; }
            .hc-phase15-expedition .hc-map-panel { display:grid; }
            .hc-phase15-expedition .hc-map-panel > h2 { order:1; }
            .hc-phase15-expedition .hc-map-panel > .hc-map-frame { order:2; }
            .hc-phase15-expedition .hc-map-panel > .hc-current-travel { order:3; margin-top:.65rem; }
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
            .hc-adjacency-interface { min-width:2.75rem; min-height:2.75rem; }
            .hc-current-travel-facts { grid-template-columns:auto minmax(0,1fr); }
            .hc-movement-ledger-table, .hc-readable-table { display:block; width:100%; max-width:100%; overflow-x:auto; overscroll-behavior-inline:contain; }
            .hc-readable-facts { grid-template-columns:1fr; }
            .hc-reference-table-facts, .hc-rule-facts { grid-template-columns:minmax(6rem,auto) minmax(0,1fr); }
            .hc-journey-state-grid { grid-template-columns:1fr; }
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
