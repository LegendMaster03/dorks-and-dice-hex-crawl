# Tool Host integration

Hex Crawl targets **Embedded Module integration contract version 2**.

This matches the current Dorks & Dice Tool Host because Hex Crawl benefits from the normal site shell, nested route ownership under `/tools/{slug}`, Tool Host context, campaign/session APIs, and the authenticated upstream gateway. The map canvas does not require ownership of the entire HTTP response subtree, cookie sessions, redirects, WebSockets, or another capability that would justify Proxied Application.

The frontend mounts only into `#tool-root`. In hosted mode it reads `data-tool-context-url`, loads the host context, and sends backend requests through `${apiBaseUrl}/upstream`. In standalone mode it calls the same backend directly. The current backend exposes only anonymous/read-only demonstrator endpoints, so no authentication middleware is claimed yet.

Before persistent or privileged backend mutations are introduced, Hex Crawl must implement the current Tool Host ticket/introspection contract rather than accepting browser-controlled identity headers.

## Route ownership

The frontend respects `data-tool-base-path` and `data-tool-route`, and listens for `popstate`. This slice has one screen, but the routing boundary is established so later nested world/editor/expedition paths can remain Tool-relative and refresh safely through the Site shell.

## Frontend lifecycle

Application-owned DOM uses explicit state transitions and an explicit render lifecycle. The canvas renderer is invalidated through `RenderLifecycle.requestRender()`, which coalesces updates through `requestAnimationFrame`.

No `MutationObserver` is used. `ResizeObserver` is used only for the external layout boundary that determines canvas dimensions.

## Deployment

The validation workflow follows the existing first-party Tool convention: Node 24 builds the ES module, .NET 10 restores/tests the solution, Docker builds the deployable image, and a container smoke test exercises health/readiness, the standalone shell, and `/app.js`.

A production deploy workflow is intentionally not added in this foundation. The existing first-party deploy workflows encode a specific self-hosted runner label, shared Docker network, service name, and deployment target. Those values do not yet exist for Hex Crawl and inventing them would create a workflow that fails or targets infrastructure that has not been provisioned. Once the service/runner is provisioned, the Block Initiative pattern can be adopted without changing application architecture.
