const STYLE_ID = "hex-crawl-party-responsive-styles";

export function ensurePartyResponsiveStyles(): void {
    if (document.getElementById(STYLE_ID)) return;

    const style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
        .hc-party-editor-panel,
        .hc-party-register {
            container-type: inline-size;
        }

        @container (max-width: 720px) {
            .hc-party-editor-panel .hc-party-editor-row,
            .hc-party-editor-panel .hc-party-movement-grid {
                grid-template-columns: minmax(0, 1fr);
            }

            .hc-party-editor-panel .hc-party-editor-row > .hc-danger-action {
                justify-self: start;
            }

            .hc-party-editor-panel .hc-party-save-row {
                align-items: stretch;
                flex-direction: column;
            }

            .hc-party-register .hc-party-register-grid {
                grid-template-columns: minmax(0, 1fr);
            }

            .hc-party-register .hc-party-register-grid > div {
                grid-column: 1 !important;
                border-right: 0;
            }
        }
    `;
    document.head.append(style);
}
