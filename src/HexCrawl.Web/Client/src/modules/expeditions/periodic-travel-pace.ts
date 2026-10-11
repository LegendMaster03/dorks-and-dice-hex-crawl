/**
 * No finite pace list means a fixed/default travel mode, NOT that cell-step
 * movement cannot run. "normal" is the existing server TravelModeSelection
 * default; a one-option ruleset uses its own declared identifier instead.
 */
export function resolvePeriodicPace(
    keys: readonly string[], selected: string, activePace: string | null
): { paceKey: string; needsChoice: boolean } {
    if (keys.length === 0)
        return { paceKey: activePace?.trim() || "normal", needsChoice: false };
    if (keys.length === 1)
        return { paceKey: activePace && keys.includes(activePace) ? activePace : keys[0],
            needsChoice: false };
    return {
        paceKey: activePace && keys.includes(activePace)
            ? activePace
            : keys.includes(selected) ? selected : keys[0],
        needsChoice: true
    };
}
