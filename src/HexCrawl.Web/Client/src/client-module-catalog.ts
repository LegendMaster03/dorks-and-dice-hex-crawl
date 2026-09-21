import type { ClientRouteKind, HexCrawlClientModule, KnownToolRoute } from "./client-module";
import { assistantsModule } from "./modules/assistants/module";
import { expeditionsModule } from "./modules/expeditions/module";
import { homeModule } from "./modules/home/module";
import { worldsModule } from "./modules/worlds/module";

const requiredRouteKinds: readonly ClientRouteKind[] = [
    "home",
    "worlds",
    "world",
    "edit",
    "expedition",
    "tracker",
    "assistant",
    "assistant-entry"
];

export const clientModules: readonly HexCrawlClientModule[] = validate([
    homeModule,
    worldsModule,
    expeditionsModule,
    assistantsModule
]);

export function findClientModule(route: KnownToolRoute): HexCrawlClientModule {
    const module = clientModules.find(candidate =>
        candidate.routeKinds.some(kind => kind === route.kind));

    if (!module) {
        throw new Error(`No Hex Crawl client module owns route kind '${route.kind}'.`);
    }

    return module;
}

function validate(modules: readonly HexCrawlClientModule[]): readonly HexCrawlClientModule[] {
    const ids = new Set<string>();
    const routeOwners = new Map<ClientRouteKind, string>();

    for (const module of modules) {
        const id = module.id.trim();
        if (!id) throw new Error("Hex Crawl client module IDs can not be blank.");
        if (ids.has(id)) throw new Error(`Duplicate Hex Crawl client module ID '${id}'.`);
        ids.add(id);

        if (module.routeKinds.length === 0) {
            throw new Error(`Hex Crawl client module '${id}' owns no routes.`);
        }

        for (const kind of module.routeKinds) {
            const owner = routeOwners.get(kind);
            if (owner) {
                throw new Error(
                    `Hex Crawl route kind '${kind}' is owned by both '${owner}' and '${id}'.`);
            }
            routeOwners.set(kind, id);
        }
    }

    for (const kind of requiredRouteKinds) {
        if (!routeOwners.has(kind)) {
            throw new Error(`Hex Crawl route kind '${kind}' has no client module owner.`);
        }
    }

    return [...modules];
}
