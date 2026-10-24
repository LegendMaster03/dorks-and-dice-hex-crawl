export function ensureExpeditionWorkspaceStyles(): void {
    if (document.getElementById("hex-crawl-expedition-workspace-styles")) return;
    const style = document.createElement("style");
    style.id = "hex-crawl-expedition-workspace-styles";
    style.textContent = `
        .hc-phase15-expedition { display:grid; gap:var(--hc-ux-gap, 1rem); }
        .hc-phase15-expedition .hc-workspace-grid {
            display:grid;
            grid-template-columns:minmax(0,1.55fr) minmax(18rem,.72fr);
            gap:1rem;
            align-items:start;
        }
        .hc-phase15-expedition .hc-map-panel { display:grid; gap:.85rem; min-width:0; }
        .hc-phase15-expedition .hc-map-host { min-height:clamp(24rem,58vh,48rem); }
        .hc-phase15-runtime-summary { display:grid; gap:.75rem; }
        .hc-direction-control { display:grid; gap:.6rem; }
        .hc-direction-control h3 { margin:0; }
        .hc-direction-grid {
            display:grid;
            grid-template-columns:repeat(3,minmax(0,1fr));
            gap:.45rem;
            max-width:42rem;
        }
        .hc-direction-grid button {
            min-height:2.8rem;
            white-space:normal;
        }
        .hc-direction-grid button[aria-pressed="true"] {
            outline:2px solid currentColor;
            outline-offset:1px;
            font-weight:700;
        }
        .hc-gm-tools { padding:.75rem 0 .25rem; }
        /* Spatial sessions keep their editors in nonmodal map tools. The
           uncovered map and toolbar remain operable while a tool is open. */
        .hc-phase15 .hc-periodic-expedition.hc-page > .hc-workspace-grid {
            display:grid; grid-template-columns:minmax(0,1fr);
        }
        .hc-phase15 .hc-periodic-expedition .hc-map-panel {
            display:flex; flex-direction:column; min-width:0;
        }
        .hc-periodic-expedition .hc-expedition-map-toolbar {
            display:flex; flex-wrap:wrap; gap:.4rem;
            position:sticky; top:.25rem; z-index:12;
            padding:.5rem; border-radius:.6rem;
            background:var(--hc-surface); border:1px solid var(--hc-border);
        }
        .hc-phase15 .hc-periodic-expedition .hc-map-host {
            height:clamp(25rem,70vh,60rem); min-height:0;
        }
        .hc-phase15 .hc-expedition-map-tool {
            position:absolute; top:auto; bottom:.75rem; right:.75rem; left:auto;
            width:min(30rem,44vw); height:min(38rem,calc(100% - 1.5rem)); max-height:calc(100% - 1.5rem);
        }
        .hc-expedition-map-tool .hc-focus-workspace-body {
            overflow:auto; overscroll-behavior:contain;
        }
        .hc-expedition-map-tool [data-gameplay-panels] > [hidden] { display:none !important; }
        @media (max-width:760px) {
            .hc-phase15 .hc-expedition-map-tool {
                inset:auto .5rem .5rem; width:auto;
                height:min(48dvh,calc(100% - 1rem)); max-height:calc(100% - 1rem);
            }
            .hc-phase15 .hc-periodic-expedition .hc-map-host { height:64dvh; min-height:20rem; }
            .hc-phase15 .hc-periodic-expedition .hc-map-panel > h2 { order:0; }
            .hc-phase15 .hc-periodic-expedition .hc-expedition-map-toolbar { order:1; }
        }
        .hc-focused-watch-workspace { display:grid; gap:.8rem; }
        .hc-focused-watch-workspace fieldset { display:grid; gap:.65rem; }
        .hc-focused-watch-workspace label { display:grid; gap:.28rem; }
        .hc-focused-watch-workspace input,
        .hc-focused-watch-workspace select,
        .hc-focused-watch-workspace textarea { min-width:0; max-width:100%; box-sizing:border-box; }
        .hc-procedure-entry-grid { margin-top:.5rem; }
        .hc-procedure-entry-card { min-height:12rem; }
        .hc-procedure-mode-toolbar { margin-bottom:.25rem; }
        .hc-procedure-summary { display:grid; gap:.75rem; }
        .hc-summary-metrics { display:grid; grid-template-columns:repeat(auto-fit,minmax(9.5rem,1fr)); gap:.55rem; }
        .hc-compact-procedure { display:grid; gap:1rem; }
        .hc-structure-card { cursor:default; }
        .hc-structure-card:hover { border-color:var(--hc-border, rgba(127,127,127,.35)); }
        .hc-advanced-module-toggle { display:block; padding:.28rem 0; overflow-wrap:anywhere; }
        .hc-advanced-module-toggle input { width:auto; }
        @media (max-width:1040px) {
            .hc-phase15-expedition .hc-workspace-grid { grid-template-columns:1fr; }
            .hc-phase15-expedition .hc-map-host { min-height:22rem; }
        }
        @media (max-width:620px) {
            .hc-direction-grid { grid-template-columns:repeat(2,minmax(0,1fr)); }
        }
    `;
    document.head.append(style);
}
