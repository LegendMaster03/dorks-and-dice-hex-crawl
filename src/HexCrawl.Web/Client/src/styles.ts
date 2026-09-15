const STYLE_ID = "hex-crawl-styles";

export function ensureStyles(): void {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
        #tool-root { font-family: system-ui, sans-serif; color: #20261f; min-height: 100%; }
        .hc-page { box-sizing: border-box; padding: 1rem; max-width: 1600px; margin: 0 auto; }
        .hc-page-header { display: flex; justify-content: space-between; gap: 1rem; align-items: flex-start; margin-bottom: 1rem; }
        .hc-page-header h1 { margin: 0; font-size: 1.6rem; }
        .hc-page-header p { margin: .2rem 0 0; color: #596257; }
        .hc-page-header nav { display: flex; gap: .5rem; }
        .hc-columns { display: grid; grid-template-columns: minmax(0, 2fr) minmax(280px, 1fr); gap: 1rem; }
        .hc-panel, .hc-sidebar details, .hc-map-panel { border: 1px solid #c8cec6; border-radius: .5rem; background: #fff; padding: .8rem; }
        .hc-panel h2 { margin-top: 0; }
        .hc-form { display: grid; gap: .55rem; }
        .hc-form label { display: grid; gap: .2rem; font-size: .9rem; }
        .hc-form input, .hc-form select, .hc-form button, button { font: inherit; }
        .hc-form input, .hc-form select { box-sizing: border-box; width: 100%; padding: .35rem .45rem; border: 1px solid #aeb6ac; border-radius: .25rem; }
        button { padding: .4rem .65rem; border: 1px solid #8e998b; border-radius: .3rem; background: #f5f7f4; cursor: pointer; }
        button:disabled { cursor: default; opacity: .55; }
        .hc-resource-button { display: block; width: 100%; text-align: left; margin: .25rem 0; }
        .hc-error { background: #fff0ef; border: 1px solid #bb6964; padding: .6rem; margin-bottom: .8rem; white-space: pre-wrap; }
        .hc-workspace-grid { display: grid; grid-template-columns: minmax(420px, 1fr) minmax(300px, 420px); gap: .8rem; align-items: start; }
        .hc-map-host { min-height: 560px; height: 68vh; border: 1px solid #aeb6ac; background: #f4f1e9; overflow: hidden; }
        .hc-map-canvas { display: block; width: 100%; height: 100%; min-height: 560px; touch-action: none; }
        .hc-sidebar { display: grid; gap: .6rem; max-height: 82vh; overflow: auto; }
        .hc-sidebar details summary { cursor: pointer; font-weight: 650; margin-bottom: .55rem; }
        .hc-inline { display: grid; grid-template-columns: 1fr 1fr; gap: .5rem; }
        .hc-button-row { display: flex; flex-wrap: wrap; gap: .35rem; }
        .hc-hint { color: #626b60; font-size: .85rem; }
        .hc-discovery-row { display: flex; gap: .5rem; justify-content: space-between; align-items: center; padding: .25rem 0; }
        .hc-status-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: .4rem; margin-top: .6rem; }
        .hc-status-grid > div { display: grid; gap: .1rem; border: 1px solid #d4d9d2; padding: .45rem; }
        .hc-status-grid span { font-size: .9rem; }
        .hc-history { max-height: 260px; overflow: auto; font-size: .85rem; }
        @media (max-width: 900px) {
            .hc-columns, .hc-workspace-grid { grid-template-columns: 1fr; }
            .hc-sidebar { max-height: none; }
            .hc-map-host, .hc-map-canvas { min-height: 420px; height: 55vh; }
        }
    `;
    document.head.append(style);
}
