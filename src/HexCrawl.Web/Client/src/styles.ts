const STYLE_ID = "hex-crawl-styles";

export function ensureStyles(): void {
    if (document.getElementById(STYLE_ID)) return;
    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
        #tool-root.hex-crawl-app {
            --hc-page-bg: #f7f8fb;
            --hc-text: #242938;
            --hc-text-strong: #171a24;
            --hc-muted: #5f6778;
            --hc-surface: #ffffff;
            --hc-surface-elevated: #f2f4f8;
            --hc-surface-soft: #e9edf4;
            --hc-border: #cfd5e1;
            --hc-input-bg: #ffffff;
            --hc-input-text: #171a24;
            --hc-primary: var(--bs-primary, #6557d2);
            --hc-primary-hover: color-mix(in srgb, var(--hc-primary) 82%, black);
            --hc-secondary: #e8ebf2;
            --hc-secondary-hover: #dde2ec;
            --hc-button-border: #b9c1d0;
            --hc-button-hover-border: #9da7ba;
            --hc-danger: #a93643;
            --hc-danger-hover: #8f2b36;
            --hc-danger-text: #8f2632;
            --hc-danger-text-hover: #711d26;
            --hc-focus: #6557d2;
            --hc-selected: #9b641c;
            --hc-success: #2f7d46;
            --hc-warning: #946410;
            --hc-disabled-border: #d5dae4;
            --hc-disabled-bg: #eef0f4;
            --hc-disabled-text: #8a91a0;
            --hc-error-border: #d49aa0;
            --hc-error-bg: #fff0f1;
            --hc-error-text: #7b2730;
            --hc-warning-border: #d6b46a;
            --hc-warning-bg: #fff8e7;
            --hc-warning-text: #6d4a0a;
            --hc-auth-accent: #5876c9;
            --hc-map-bg: #dad5c7;
            --hc-map-border: #7b8291;
            box-sizing: border-box;
            width: 100%;
            min-height: 12rem;
            border-radius: .85rem;
            background: var(--hc-page-bg);
            color: var(--hc-text);
            font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
            color-scheme: light;
        }

        html[data-bs-theme="dark"] #tool-root.hex-crawl-app {
            --hc-page-bg: #171a25;
            --hc-text: #e5e7eb;
            --hc-text-strong: #f3f4f6;
            --hc-muted: #a7adbd;
            --hc-surface: #151824;
            --hc-surface-elevated: #1d2130;
            --hc-surface-soft: #202536;
            --hc-border: #303449;
            --hc-input-bg: #11131d;
            --hc-input-text: #f3f4f6;
            --hc-primary: var(--bs-primary, #6d61dc);
            --hc-primary-hover: color-mix(in srgb, var(--hc-primary) 82%, white);
            --hc-secondary: #2b3042;
            --hc-secondary-hover: #373d52;
            --hc-button-border: #454c64;
            --hc-button-hover-border: #59617d;
            --hc-danger: #b94a52;
            --hc-danger-hover: #cf5a63;
            --hc-danger-text: #f0a6ad;
            --hc-danger-text-hover: #ffd2d6;
            --hc-focus: #a99df5;
            --hc-selected: #d69a45;
            --hc-success: #65b47a;
            --hc-warning: #d8a64f;
            --hc-disabled-border: #353a4c;
            --hc-disabled-bg: #242837;
            --hc-disabled-text: #777e91;
            --hc-error-border: #87484f;
            --hc-error-bg: #2b191e;
            --hc-error-text: #ffd5d8;
            --hc-warning-border: #846632;
            --hc-warning-bg: #2a2417;
            --hc-warning-text: #f3dfb6;
            --hc-auth-accent: #8aa2ec;
            --hc-map-bg: #dad5c7;
            --hc-map-border: #4b526a;
            color-scheme: dark;
        }

        @media (prefers-color-scheme: dark) {
            html:not([data-bs-theme]) #tool-root.hex-crawl-app {
                --hc-page-bg: #171a25;
                --hc-text: #e5e7eb;
                --hc-text-strong: #f3f4f6;
                --hc-muted: #a7adbd;
                --hc-surface: #151824;
                --hc-surface-elevated: #1d2130;
                --hc-surface-soft: #202536;
                --hc-border: #303449;
                --hc-input-bg: #11131d;
                --hc-input-text: #f3f4f6;
                --hc-primary: #6d61dc;
                --hc-primary-hover: color-mix(in srgb, var(--hc-primary) 82%, white);
                --hc-secondary: #2b3042;
                --hc-secondary-hover: #373d52;
                --hc-button-border: #454c64;
                --hc-button-hover-border: #59617d;
                --hc-danger: #b94a52;
                --hc-danger-hover: #cf5a63;
                --hc-danger-text: #f0a6ad;
                --hc-danger-text-hover: #ffd2d6;
                --hc-focus: #a99df5;
                --hc-selected: #d69a45;
                --hc-success: #65b47a;
                --hc-warning: #d8a64f;
                --hc-disabled-border: #353a4c;
                --hc-disabled-bg: #242837;
                --hc-disabled-text: #777e91;
                --hc-error-border: #87484f;
                --hc-error-bg: #2b191e;
                --hc-error-text: #ffd5d8;
                --hc-warning-border: #846632;
                --hc-warning-bg: #2a2417;
                --hc-warning-text: #f3dfb6;
                --hc-auth-accent: #8aa2ec;
                --hc-map-bg: #dad5c7;
                --hc-map-border: #4b526a;
                color-scheme: dark;
            }
        }
        #tool-root.hex-crawl-app *, #tool-root.hex-crawl-app *::before, #tool-root.hex-crawl-app *::after { box-sizing: border-box; }
        #tool-root.hex-crawl-app [hidden] { display: none !important; }
        .hc-sr-only { position: absolute !important; width: 1px !important; height: 1px !important; padding: 0 !important; margin: -1px !important; overflow: hidden !important; clip: rect(0, 0, 0, 0) !important; white-space: nowrap !important; border: 0 !important; }
        .hc-page { width: 100%; max-width: 1600px; margin: 0 auto; padding: 1rem; }
        .hc-page-header { display: flex; justify-content: space-between; gap: 1rem; align-items: flex-start; margin-bottom: 1rem; }
        .hc-page-header h1 { margin: 0; color: var(--hc-text-strong); font-size: clamp(1.45rem, 2vw, 1.8rem); line-height: 1.2; }
        .hc-page-header p { margin: .25rem 0 0; color: var(--hc-muted); }
        .hc-page-header nav { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: .45rem; }
        .hc-columns { display: grid; grid-template-columns: minmax(0, 1.7fr) minmax(19rem, .9fr); gap: 1rem; align-items: start; }
        .hc-panel, .hc-sidebar details, .hc-map-panel, .hc-loading-panel {
            border: 1px solid var(--hc-border);
            border-radius: .75rem;
            background: var(--hc-surface);
            box-shadow: 0 .15rem .45rem rgba(0, 0, 0, .12);
        }
        .hc-panel, .hc-sidebar details, .hc-loading-panel { padding: .9rem; }
        .hc-panel h2, .hc-panel h3 { margin: 0 0 .65rem; color: var(--hc-text-strong); }
        .hc-panel-heading { display: flex; justify-content: space-between; gap: .75rem; align-items: baseline; margin-bottom: .65rem; }
        .hc-panel-heading h2, .hc-panel-heading p { margin: 0; }
        .hc-world-list { display: grid; gap: .55rem; }
        .hc-expedition-list { display: grid; gap: .65rem; }
        .hc-expedition-card { display: grid; gap: .7rem; padding: .8rem; border: 1px solid var(--hc-border); border-radius: .6rem; background: var(--hc-surface-elevated); }
        .hc-expedition-card h3, .hc-expedition-card p { margin: 0; }
        .hc-expedition-card h3 { color: var(--hc-text-strong); }
        .hc-mode-section { margin-top: 1rem; }
        .hc-mode-section > h2 { margin: 0 0 .65rem; color: var(--hc-text-strong); font-size: 1.05rem; }
        .hc-mode-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(15rem, 1fr)); gap: .65rem; }
        .hc-home-three-column-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); gap: .65rem; }
        .hc-home-main-grid > :first-child { grid-column: span 2; }
        .hc-mode-card { padding: .8rem; border: 1px solid var(--hc-border); border-radius: .65rem; background: var(--hc-surface); }
        .hc-mode-card h3 { margin: 0 0 .35rem; color: var(--hc-text-strong); font-size: .95rem; }
        .hc-mode-card p { margin: 0; color: var(--hc-muted); font-size: .86rem; }
        .hc-view-switcher { display: flex; flex-wrap: wrap; gap: .4rem; margin: -.2rem 0 .75rem; }
        .hc-focus-hint { margin: -.1rem 0 .75rem; }
        .hc-empty-state { display: grid; gap: .25rem; padding: .85rem; border: 1px dashed var(--hc-border); border-radius: .65rem; background: var(--hc-surface-elevated); color: var(--hc-muted); }
        .hc-empty-state strong { color: var(--hc-text-strong); }
        .hc-world-card { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: .75rem; align-items: center; width: 100%; padding: .75rem .8rem; }
        .hc-world-card-copy { min-width: 0; }
        .hc-world-card-title { display: block; overflow: hidden; color: var(--hc-text-strong); font-weight: 700; text-overflow: ellipsis; white-space: nowrap; }
        .hc-world-card-meta { display: block; margin-top: .15rem; color: var(--hc-muted); font-size: .82rem; }
        .hc-form { display: grid; gap: .65rem; }
        .hc-form label { display: grid; gap: .25rem; min-width: 0; color: var(--hc-text); font-size: .88rem; font-weight: 600; }
        .hc-form input, .hc-form select, .hc-form textarea, .hc-form button, #tool-root.hex-crawl-app button { font: inherit; }
        .hc-form input:not([type="checkbox"]), .hc-form select, .hc-form textarea {
            width: 100%; min-width: 0; padding: .48rem .55rem; border: 1px solid var(--hc-border); border-radius: .4rem;
            background: var(--hc-input-bg); color: var(--hc-input-text); outline: none;
        }
        .hc-form input[type="checkbox"] { width: 1rem; height: 1rem; margin: 0; accent-color: var(--hc-primary); }
        .hc-form label:has(> input[type="checkbox"]) { display: flex; grid-template-columns: none; flex-direction: row; align-items: center; gap: .5rem; font-weight: 500; }
        .hc-form input:not([type="checkbox"]):focus-visible, .hc-form select:focus-visible, .hc-form textarea:focus-visible,
        #tool-root.hex-crawl-app button:focus-visible, .hc-sidebar summary:focus-visible, .hc-map-canvas:focus-visible {
            border-color: var(--hc-focus); outline: .18rem solid color-mix(in srgb, var(--hc-focus) 35%, transparent); outline-offset: .08rem;
        }
        #tool-root.hex-crawl-app button {
            min-height: 2.15rem; padding: .42rem .7rem; border: 1px solid var(--hc-button-border); border-radius: .45rem;
            background: var(--hc-secondary); color: var(--hc-text-strong); cursor: pointer;
            transition: background-color 120ms ease, border-color 120ms ease, transform 80ms ease;
        }
        #tool-root.hex-crawl-app button:hover:not(:disabled) { background: var(--hc-secondary-hover); border-color: var(--hc-button-hover-border); }
        #tool-root.hex-crawl-app button:active:not(:disabled) { transform: translateY(1px); }
        #tool-root.hex-crawl-app button.hc-primary-action { background: var(--hc-primary); border-color: var(--hc-primary); color: #fff; font-weight: 700; }
        #tool-root.hex-crawl-app button.hc-primary-action:hover:not(:disabled) { background: var(--hc-primary-hover); border-color: var(--hc-primary-hover); }
        #tool-root.hex-crawl-app button.hc-danger-action { background: transparent; border-color: var(--hc-danger); color: var(--hc-danger-text); }
        #tool-root.hex-crawl-app button.hc-danger-action:hover:not(:disabled) { background: color-mix(in srgb, var(--hc-danger) 28%, transparent); border-color: var(--hc-danger-hover); color: var(--hc-danger-text-hover); }
        #tool-root.hex-crawl-app button:disabled { border-color: var(--hc-disabled-border); background: var(--hc-disabled-bg); color: var(--hc-disabled-text); cursor: not-allowed; opacity: 1; transform: none; }
        .hc-resource-button { display: block; width: 100%; text-align: left; margin: .25rem 0; }
        .hc-error { margin-bottom: .8rem; padding: .7rem .8rem; border: 1px solid var(--hc-error-border); border-left: .28rem solid var(--hc-danger); border-radius: .45rem; background: var(--hc-error-bg); color: var(--hc-error-text); white-space: pre-wrap; }
        .hc-error[data-kind="validation"] { border-color: var(--hc-warning-border); border-left-color: var(--hc-warning); background: var(--hc-warning-bg); color: var(--hc-warning-text); }
        .hc-error[data-kind="conflict"] { border-color: var(--hc-warning-border); border-left-color: var(--hc-warning); background: var(--hc-warning-bg); color: var(--hc-warning-text); }
        .hc-error[data-kind="auth"] { border-left-color: var(--hc-auth-accent); }
        .hc-loading-panel { display: flex; align-items: center; gap: .65rem; color: var(--hc-muted); }
        .hc-loading-dot { width: .7rem; height: .7rem; flex: 0 0 auto; border-radius: 50%; background: var(--hc-primary); box-shadow: 0 0 0 .25rem color-mix(in srgb, var(--hc-primary) 18%, transparent); }
        .hc-form details, .hc-sidebar details details { margin-top: .1rem; border: 1px solid var(--hc-border); border-radius: .55rem; background: var(--hc-surface-elevated); padding: .6rem .7rem; }
        .hc-form details > summary, .hc-sidebar summary { color: var(--hc-text-strong); cursor: pointer; font-weight: 700; }
        .hc-form details[open] > summary, .hc-sidebar details[open] > summary { margin-bottom: .65rem; }
        .hc-custom-unit-fields { display: grid; gap: .65rem; padding: .1rem 0; }
        .hc-workspace-grid { display: grid; grid-template-columns: minmax(28rem, 1fr) minmax(18rem, 24rem); gap: .85rem; align-items: start; }
        .hc-tracker-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(20rem, 28rem); gap: .85rem; align-items: start; }
        .hc-assistant-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(18rem, .75fr); gap: .85rem; align-items: start; }
        .hc-assistant-form { margin-top: .8rem; }
        .hc-assistant-warning { margin: .75rem 0; padding: .65rem .7rem; border: 1px solid var(--hc-warning-border); border-left: .28rem solid var(--hc-warning); border-radius: .45rem; background: var(--hc-warning-bg); color: var(--hc-warning-text); font-size: .86rem; }
        .hc-runtime-panel { min-width: 0; }
        .hc-focus-primary { border-color: var(--hc-focus) !important; box-shadow: 0 0 0 .12rem color-mix(in srgb, var(--hc-focus) 18%, transparent); }
        .hc-focus-secondary { opacity: .78; }
        .hc-map-panel { min-width: 0; padding: .65rem; background: var(--hc-surface); }
        .hc-map-host { min-height: 560px; height: 68vh; overflow: hidden; border: 1px solid var(--hc-map-border); border-radius: .55rem; background: var(--hc-map-bg); }
        .hc-map-canvas { display: block; width: 100%; height: 100%; min-height: 560px; touch-action: none; }
        .hc-sidebar { display: grid; gap: .6rem; max-height: 82vh; overflow: auto; padding-right: .12rem; }
        .hc-world-editor .hc-sidebar { max-height: none; overflow: visible; padding-right: 0; }
        .hc-world-editor .hc-map-panel { position: sticky; top: .75rem; }
        .hc-sidebar details { box-shadow: none; }
        .hc-sidebar details summary { cursor: pointer; font-weight: 700; }
        .hc-inline { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: .5rem; }
        .hc-button-row { display: flex; flex-wrap: wrap; gap: .4rem; }
        .hc-hint, .hc-muted { color: var(--hc-muted); font-size: .84rem; }
        .hc-map-panel > .hc-hint { margin: .55rem .15rem .15rem; }
        .hc-discovery-row { display: flex; gap: .5rem; justify-content: space-between; align-items: center; padding: .4rem 0; border-bottom: 1px solid var(--hc-border); }
        .hc-discovery-row:last-child { border-bottom: 0; }
        .hc-status-section { margin-top: .7rem; }
        .hc-status-section h2 { margin: 0 0 .45rem; font-size: 1rem; }
        .hc-status-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(8.5rem, 1fr)); gap: .4rem; }
        .hc-status-grid > div { display: grid; gap: .12rem; min-width: 0; padding: .5rem; border: 1px solid var(--hc-border); border-radius: .45rem; background: var(--hc-surface-elevated); }
        .hc-status-grid strong { color: var(--hc-muted); font-size: .72rem; letter-spacing: .04em; text-transform: uppercase; }
        .hc-status-grid span { overflow-wrap: anywhere; color: var(--hc-text-strong); font-size: .88rem; }
        .hc-history-panel { margin-top: .65rem; padding: .7rem; border: 1px solid var(--hc-border); border-radius: .55rem; background: var(--hc-surface-elevated); }
        .hc-history-panel summary { cursor: pointer; color: var(--hc-text-strong); font-weight: 700; }
        .hc-history { max-height: 260px; overflow: auto; margin-bottom: 0; padding-left: 1.35rem; color: var(--hc-muted); font-size: .84rem; }
        .hc-history li + li { margin-top: .3rem; }
        .hc-subsection-title { margin: .2rem 0 .1rem; color: var(--hc-muted); font-size: .72rem; font-weight: 800; letter-spacing: .07em; text-transform: uppercase; }
        .hc-error-page { display: grid; max-width: 46rem; gap: .75rem; }
        .hc-error-page h1, .hc-error-page p { margin: 0; }
        .hc-error-page p { color: var(--hc-muted); }

        /* Running-sheet presentation: recognizable paper workflow with modern, optional automation. */
        .hc-run-toolbar { align-items: center; padding: .45rem; border: 1px solid var(--hc-border); border-radius: .65rem; background: var(--hc-surface); }
        .hc-run-toolbar .hc-active-view { border-color: var(--hc-primary); background: color-mix(in srgb, var(--hc-primary) 12%, var(--hc-surface)); box-shadow: inset 0 -2px 0 var(--hc-primary); }
        .hc-home-optional-tools { margin-bottom: 1rem; }
        .hc-home-optional-tools > summary { cursor: pointer; color: var(--hc-text-strong); font-weight: 800; }
        .hc-home-optional-tools[open] > summary { margin-bottom: .7rem; }
        .hc-run-toolbar-divider { width: 1px; height: 1.8rem; margin: 0 .2rem; background: var(--hc-border); }
        .hc-run-toolbar-label { color: var(--hc-muted); font-size: .76rem; font-weight: 800; letter-spacing: .06em; text-transform: uppercase; }
        .hc-run-column { display: grid; gap: .75rem; }
        .hc-running-sheet {
            min-width: 0;
            padding: .85rem;
            border: 1px solid var(--hc-border);
            border-radius: .55rem;
            background:
                linear-gradient(var(--hc-surface), var(--hc-surface)) padding-box,
                repeating-linear-gradient(0deg, transparent 0, transparent 31px, color-mix(in srgb, var(--hc-border) 35%, transparent) 32px) border-box;
        }
        .hc-map-panel > .hc-running-sheet { margin-top: .75rem; box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--hc-border) 35%, transparent); }
        .hc-sheet-heading { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; padding-bottom: .65rem; border-bottom: 2px solid var(--hc-text-strong); }
        .hc-sheet-heading h2 { margin: .1rem 0 0; color: var(--hc-text-strong); font-size: 1.08rem; }
        .hc-sheet-kicker { color: var(--hc-muted); font-size: .7rem; font-weight: 900; letter-spacing: .12em; text-transform: uppercase; }
        .hc-sheet-note { color: var(--hc-muted); font-size: .76rem; font-weight: 700; }
        .hc-sheet-status { margin-top: .7rem; }
        .hc-sheet-status > div { border-radius: .2rem; box-shadow: none; }
        .hc-sheet-status strong { letter-spacing: .075em; }
        .hc-sheet-ledger { margin-top: .8rem; border-top: 1px solid var(--hc-border); padding-top: .7rem; }
        .hc-sheet-ledger-heading { display: flex; justify-content: space-between; align-items: baseline; gap: .75rem; margin-bottom: .45rem; }
        .hc-sheet-ledger-heading h3 { margin: 0; color: var(--hc-text-strong); font-size: .95rem; }
        .hc-sheet-ledger-heading span { color: var(--hc-muted); font-size: .72rem; }
        .hc-sheet-empty { margin: 0; padding: .8rem; border: 1px dashed var(--hc-border); color: var(--hc-muted); font-size: .84rem; }
        .hc-watch-ledger-wrap { width: 100%; overflow-x: auto; }
        .hc-watch-ledger { width: 100%; min-width: 820px; border-collapse: collapse; table-layout: fixed; background: var(--hc-surface); font-size: .82rem; }
        .hc-watch-ledger th, .hc-watch-ledger td { padding: .42rem .48rem; border: 1px solid var(--hc-border); vertical-align: top; text-align: left; overflow-wrap: anywhere; }
        .hc-watch-ledger th { background: var(--hc-surface-soft); color: var(--hc-text-strong); font-size: .7rem; letter-spacing: .06em; text-transform: uppercase; }
        .hc-watch-ledger th:nth-child(1), .hc-watch-ledger td:nth-child(1) { width: 3.5rem; text-align: center; }
        .hc-watch-ledger th:nth-child(2), .hc-watch-ledger td:nth-child(2) { width: 4.5rem; text-align: center; }
        .hc-watch-ledger th:nth-child(3), .hc-watch-ledger td:nth-child(3) { width: 11rem; }
        .hc-watch-ledger th:nth-child(4), .hc-watch-ledger td:nth-child(4) { width: 10rem; }
        .hc-watch-ledger th:nth-child(5), .hc-watch-ledger td:nth-child(5) { width: 8rem; }
        .hc-watch-ledger th:nth-child(6), .hc-watch-ledger td:nth-child(6) { width: 13rem; }
        .hc-watch-ledger th:nth-child(7), .hc-watch-ledger td:nth-child(7) { width: 8rem; }
        .hc-watch-ledger tbody tr:first-child td { background: color-mix(in srgb, var(--hc-primary) 7%, var(--hc-surface)); }
        .hc-event-audit { margin-top: .65rem; padding: .55rem .65rem; border: 1px solid var(--hc-border); border-radius: .4rem; background: var(--hc-surface-elevated); }
        .hc-event-audit > summary { cursor: pointer; color: var(--hc-text-strong); font-size: .82rem; font-weight: 700; }
        .hc-event-audit-list { display: grid; gap: .4rem; max-height: 18rem; overflow: auto; margin: .55rem 0 0; padding-left: 1.35rem; }
        .hc-event-audit-list li { color: var(--hc-muted); font-size: .78rem; }
        .hc-event-audit-list li strong, .hc-event-audit-list li span { display: block; }
        .hc-event-audit-list li strong { color: var(--hc-text); font-size: .72rem; }
        .hc-sheet-controls > summary { font-size: .98rem; }
        .hc-sheet-controls fieldset { margin: 0; padding: .7rem; border: 1px solid var(--hc-border); border-radius: .45rem; }
        .hc-sheet-controls fieldset legend { padding: 0 .3rem; color: var(--hc-text-strong); font-size: .78rem; font-weight: 800; letter-spacing: .055em; text-transform: uppercase; }
        .hc-watch-requirements { margin-bottom: .7rem; padding: .55rem .65rem; border-left: .22rem solid var(--hc-primary); background: var(--hc-surface-elevated); }
        .hc-watch-requirements .hc-hint { margin: .15rem 0; }
        .hc-optional-reference { opacity: .92; }

        .hc-party-register { margin-top: .8rem; padding-top: .7rem; border-top: 1px solid var(--hc-border); }
        .hc-party-register-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); border: 1px solid var(--hc-border); background: var(--hc-surface); }
        .hc-party-register-grid > div { display: grid; gap: .15rem; min-width: 0; padding: .5rem .55rem; border-right: 1px solid var(--hc-border); border-bottom: 1px solid var(--hc-border); }
        .hc-party-register-grid > div:nth-child(3n) { border-right: 0; }
        .hc-party-register-grid > div:last-child { border-bottom: 0; }
        .hc-party-register-grid strong { color: var(--hc-muted); font-size: .68rem; letter-spacing: .065em; text-transform: uppercase; }
        .hc-party-register-grid span { overflow-wrap: anywhere; color: var(--hc-text-strong); font-size: .82rem; line-height: 1.35; }
        .hc-party-register-grid .hc-party-register-wide { grid-column: 1 / -1; border-right: 0; }
        .hc-party-form fieldset { margin: 0; padding: .65rem; border: 1px solid var(--hc-border); border-radius: .45rem; }
        .hc-party-form fieldset legend { padding: 0 .3rem; color: var(--hc-text-strong); font-size: .78rem; font-weight: 800; letter-spacing: .055em; text-transform: uppercase; }
        .hc-party-editor-list { display: grid; gap: .5rem; margin-bottom: .55rem; }
        .hc-party-editor-row { display: grid; grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr) auto; gap: .45rem; align-items: end; padding: .5rem; border: 1px solid var(--hc-border); border-radius: .4rem; background: var(--hc-surface-elevated); }
        .hc-party-member-row { grid-template-columns: minmax(0, 1.1fr) minmax(0, 1fr) minmax(10rem, auto) auto; }
        .hc-marching-row { grid-template-columns: minmax(0, 1fr) 5rem 5rem auto; }
        .hc-standing-order-row { grid-template-columns: auto minmax(0, 1fr) auto; }
        .hc-party-checkbox { align-self: center; }
        .hc-watch-entry { display: grid; gap: .4rem; padding: .5rem; border: 1px solid var(--hc-border); border-radius: .4rem; background: var(--hc-surface-elevated); }
        .hc-watch-heading-row { grid-template-columns: 5rem minmax(0, 1fr) auto; padding: 0; border: 0; background: transparent; }
        .hc-watch-members { display: flex; flex-wrap: wrap; gap: .35rem .7rem; }
        .hc-watch-member { display: inline-flex !important; grid-template-columns: none !important; flex-direction: row; align-items: center; gap: .3rem !important; font-size: .82rem !important; font-weight: 500 !important; }
        .hc-party-movement-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: .45rem; }
        .hc-movement-reference { display: grid; gap: .4rem; padding: .5rem; border: 1px solid var(--hc-border); border-radius: .4rem; background: var(--hc-surface-elevated); }
        .hc-party-save-row { display: flex; justify-content: space-between; align-items: center; gap: .6rem; padding-top: .25rem; border-top: 1px solid var(--hc-border); }
        .hc-party-empty-line { margin: 0; padding: .35rem 0; }
        .hc-party-editor-panel[open] > summary { margin-bottom: .7rem; }

        @media (max-width: 720px) {
            .hc-run-toolbar-divider, .hc-run-toolbar-label { display: none; }
            .hc-sheet-heading, .hc-sheet-ledger-heading { display: grid; gap: .2rem; }
            .hc-watch-ledger { font-size: .78rem; }
            .hc-watch-ledger th:nth-child(n), .hc-watch-ledger td:nth-child(n) { width: auto; }
            .hc-party-register-grid, .hc-party-movement-grid { grid-template-columns: 1fr; }
            .hc-party-register-grid > div { grid-column: 1 !important; border-right: 0; }
            .hc-party-member-row, .hc-party-editor-row, .hc-marching-row, .hc-standing-order-row, .hc-watch-heading-row { grid-template-columns: 1fr; }
            .hc-party-save-row { align-items: stretch; flex-direction: column; }
        }
        @media (max-width: 1050px) {
            .hc-workspace-grid { grid-template-columns: minmax(0, 1fr) minmax(17rem, 21rem); }
            .hc-tracker-grid { grid-template-columns: minmax(0, 1fr) minmax(18rem, 24rem); }
            .hc-assistant-grid { grid-template-columns: minmax(0, 1fr) minmax(17rem, 22rem); }
            .hc-map-host, .hc-map-canvas { min-height: 470px; height: 60vh; }
        }
        @media (max-width: 820px) {
            .hc-columns, .hc-workspace-grid, .hc-tracker-grid, .hc-assistant-grid, .hc-home-three-column-grid { grid-template-columns: 1fr; }
            .hc-home-main-grid > :first-child { grid-column: auto; }
            .hc-sidebar { max-height: none; overflow: visible; }
            .hc-world-editor .hc-map-panel { position: static; }
            .hc-map-host, .hc-map-canvas { min-height: 400px; height: 55vh; }
        }
        @media (max-width: 560px) {
            .hc-page { padding: .75rem; }
            .hc-page-header { display: grid; }
            .hc-page-header nav { justify-content: flex-start; }
            .hc-inline { grid-template-columns: 1fr; }
            .hc-world-card { grid-template-columns: 1fr; }
            .hc-world-card > button { width: 100%; }
            .hc-map-host, .hc-map-canvas { min-height: 340px; height: 50vh; }
        }
    `;
    document.head.append(style);
}
