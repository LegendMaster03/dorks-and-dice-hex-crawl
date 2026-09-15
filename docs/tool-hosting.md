# Tool Host integration

Hex Crawl targets **Embedded Module integration contract version 2**.

The frontend mounts only into `#tool-root`. Hosted mode loads Tool Host context and sends backend requests through `${apiBaseUrl}/upstream`; standalone mode calls the same backend directly.

## Trusted hosted identity

Persistent writes now implement the current first-party Dorks & Dice Tool Host authentication contract rather than accepting browser-controlled identity headers.

For an upstream request, the Tool Host provides two reserved backend headers:

- `X-Dorks-Tool-Auth-Ticket`
- `X-Dorks-Tool-Auth-Introspection-Path`

Hex Crawl requires exactly one value for each. The introspection path is fixed to `/tool-host/hex-crawl/api/introspect`; a browser can not select an alternate introspection endpoint. `DorksAndDiceToolHostAuthenticationClient` redeems the one-time ticket with `Authorization: Bearer <ticket>` against the deployment-configured `ToolHost:BaseUrl` and validates contract version 1, tool slug `hex-crawl`, and a non-empty stable user ID.

A valid context becomes the request `ClaimsPrincipal`; `ClaimTypes.NameIdentifier` is the authoritative owner key used by persistent application services. A missing/malformed ticket is unauthorized. Tool Host configuration/network failures fail closed with service/gateway errors rather than falling back to a browser identity.

Hex Crawl does not read or share the main site's Identity database.

## Standalone development identity

A standalone development instance can explicitly enable:

- `ToolHost:StandaloneIdentity:Enabled=true`
- `ToolHost:StandaloneIdentity:UserId=<stable development id>`
- optional `ToolHost:StandaloneIdentity:DisplayName`

This mode is disabled by default. It exists for local development, automated integration tests, and the standalone container smoke test. It is not a production authentication mechanism and does not accept an identity from request headers or query parameters.

## Ownership boundary

The initial authorization model is intentionally simple:

- overworlds have `OwnerUserId`;
- world enumeration returns only the current owner's worlds;
- direct world mutations must load that owner-scoped world;
- expedition creation requires access to its parent world;
- expedition reads/mutations are owner-scoped and revalidate the associated world.

Campaign sharing/player permissions are deferred. The application boundary leaves room to replace the ownership check with a richer access policy later.

## Route ownership

Internal routes are Tool-relative:

- `/worlds`
- `/worlds/{worldId}`
- `/worlds/{worldId}/edit`
- `/worlds/{worldId}/expeditions/{expeditionId}`

The frontend derives the current Tool route from `data-tool-base-path`, `data-tool-route`, and browser location, and uses History API navigation with `popstate`. Standalone ASP.NET fallback serves the shell for non-API deep routes so refresh works directly. In hosted mode the same relative paths live under `/tools/hex-crawl/...`; the Dorks & Dice site does not need to understand the Hex Crawl route schema.

## Frontend lifecycle

Application-owned DOM uses explicit route/state transitions. Canvas drawing is invalidated through `RenderLifecycle.requestRender()`, which coalesces renders through `requestAnimationFrame`.

`MutationObserver` is not used. `ResizeObserver` is restricted to the external layout boundary that determines canvas dimensions.

## Validation modes

CI validates both hosting paths.

The Embedded Module smoke test supplies a contract-version-2 Tool Host context and proves persistent GET/POST calls use `${apiBaseUrl}/upstream` instead of bypassing the host gateway.

The standalone container smoke test uses an explicit development identity and deployment-owned SQLite volume. It verifies health/readiness, direct deep-route shell refresh, creates a persisted overworld, destroys/restarts the application container against the same volume, and confirms that the overworld is still present.

The .NET integration suite separately verifies that persistent APIs are unauthorized when neither a Tool Host ticket nor the explicit standalone-development identity is present.

## Deployment configuration

Production deployments must provide the durable `ConnectionStrings:HexCrawl` location and, for hosted authentication, `ToolHost:BaseUrl`. No credentials or environment-specific absolute source-map file paths are committed.

A production deploy workflow is still intentionally absent because Hex Crawl does not yet have an assigned deployment service/runner/network target in this repository. That provisioning decision does not affect the Embedded Module or persistence architecture.
