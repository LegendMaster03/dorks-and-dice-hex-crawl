const STYLE_ID = "hex-crawl-runtime-styles";

export function ensureStyles(): void {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
        :root { color-scheme: light; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
        * { box-sizing: border-box; }
        body { margin: 0; background: #e7e2d5; color: #20251f; }
        button, input, select { font: inherit; }
        button { border: 1px solid #858071; background: #f8f5eb; color: #242922; border-radius: 5px; padding: .42rem .65rem; cursor: pointer; }
        button:hover:not(:disabled) { background: #fff; }
        button:disabled { cursor: default; opacity: .55; }
        input, select { border: 1px solid #9b9585; border-radius: 4px; background: #fffdf6; color: #20251f; padding: .3rem .4rem; min-width: 0; }
        .hc-app { display: flex; flex-direction: column; min-height: 100vh; }
        .hc-toolbar, .hc-status { display: flex; align-items: center; gap: .8rem; flex-wrap: wrap; padding: .55rem .75rem; background: #f4f0e4; border-bottom: 1px solid #aaa28d; }
        .hc-status { border-top: 1px solid #aaa28d; border-bottom: 0; font-size: .78rem; color: #555b52; }
        .hc-control { display: grid; gap: .2rem; font-size: .75rem; font-weight: 650; }
        .hc-control input, .hc-control select { font-weight: 400; }
        .hc-map-note { flex: 1 1 18rem; font-size: .72rem; color: #67695f; }
        .hc-workspace { display: grid; grid-template-columns: minmax(0, 1fr) minmax(330px, 430px); flex: 1; min-height: 0; }
        .hc-map-column { display: flex; flex-direction: column; min-width: 0; min-height: 620px; }
        .hc-stage { position: relative; flex: 1; min-height: 540px; overflow: hidden; background: #ded9c8; }
        .hc-canvas { width: 100%; height: 100%; display: block; touch-action: none; cursor: grab; }
        .hc-canvas[data-dragging="true"] { cursor: grabbing; }
        .hc-overlay { position: absolute; left: .7rem; bottom: .7rem; padding: .45rem .6rem; background: rgba(251,248,239,.88); border: 1px solid rgba(99,94,82,.45); border-radius: 5px; font-size: .76rem; pointer-events: none; }
        .hc-runtime { border-left: 1px solid #aaa28d; background: #f8f5eb; overflow: auto; padding: .7rem; display: grid; gap: .7rem; align-content: start; }
        .hc-runtime-heading { display: grid; grid-template-columns: 1fr auto; gap: .45rem; align-items: end; border-bottom: 1px solid #c8c0ad; padding-bottom: .6rem; }
        .hc-runtime-heading > button { grid-column: 1 / -1; }
        .hc-muted { color: #686c64; font-size: .72rem; font-weight: 400; }
        .hc-runtime-state { display: grid; grid-template-columns: 1fr 1fr; gap: .35rem .7rem; margin: 0; }
        .hc-runtime-state > div { display: grid; grid-template-columns: minmax(5rem, auto) 1fr; gap: .35rem; padding: .22rem 0; border-bottom: 1px dotted #d0c9b8; }
        .hc-runtime-state dt { font-size: .7rem; color: #65685f; }
        .hc-runtime-state dd { margin: 0; font-size: .75rem; font-weight: 650; text-align: right; }
        .hc-runtime-form { display: grid; grid-template-columns: 1fr 1fr; gap: .55rem; padding: .65rem; border: 1px solid #c5bdab; background: #f1ede0; border-radius: 6px; }
        .hc-runtime-subgrid { grid-column: 1 / -1; display: grid; grid-template-columns: 1fr 1fr; gap: .5rem; border-top: 1px solid #d1c9b6; padding-top: .55rem; }
        .hc-runtime-subgrid[hidden], [hidden] { display: none !important; }
        .hc-inline-fieldset { margin: 0; padding: .3rem .45rem .4rem; border: 1px solid #aaa28d; border-radius: 4px; font-size: .72rem; }
        .hc-inline-fieldset legend { padding: 0 .2rem; font-weight: 650; }
        .hc-inline-fieldset label { display: block; }
        .hc-span-2 { grid-column: 1 / -1; }
        .hc-advance { grid-column: 1 / -1; background: #344c38; color: #fff; border-color: #26392a; font-weight: 700; }
        .hc-advance:hover:not(:disabled) { background: #3f5b44; }
        details { border: 1px solid #cbc3b2; border-radius: 5px; background: #fbf8ef; }
        details summary { cursor: pointer; padding: .45rem .55rem; font-size: .78rem; font-weight: 700; }
        .hc-discovery-list { display: grid; gap: .3rem; padding: 0 .5rem .55rem; max-height: 13rem; overflow: auto; }
        .hc-discovery-row { display: grid; grid-template-columns: 1fr auto; gap: .4rem; align-items: center; padding: .28rem 0; border-top: 1px dotted #ddd5c4; font-size: .72rem; }
        .hc-discovery-row button { font-size: .68rem; padding: .25rem .4rem; }
        .hc-history { margin: 0; padding: 0 .6rem .6rem 2rem; max-height: 19rem; overflow: auto; font-size: .69rem; }
        .hc-history li { padding: .28rem 0; border-top: 1px dotted #ddd5c4; }
        .hc-history span { color: #555a51; }
        .hc-error { position: fixed; right: 1rem; bottom: 1rem; max-width: min(36rem, calc(100vw - 2rem)); padding: .7rem .9rem; background: #7a2626; color: #fff; border-radius: 6px; box-shadow: 0 4px 20px rgba(0,0,0,.25); z-index: 20; }
        @media (max-width: 980px) {
            .hc-workspace { grid-template-columns: 1fr; }
            .hc-runtime { border-left: 0; border-top: 1px solid #aaa28d; }
            .hc-map-column { min-height: 520px; }
            .hc-stage { min-height: 440px; }
        }
        @media (max-width: 560px) {
            .hc-runtime-state, .hc-runtime-form, .hc-runtime-subgrid { grid-template-columns: 1fr; }
            .hc-span-2, .hc-runtime-subgrid { grid-column: 1; }
            .hc-runtime-state > div { grid-template-columns: 1fr auto; }
        }
    `;
    document.head.append(style);
}
