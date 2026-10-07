export type JourneyStageDefinitionLike = {
    stageKey: string;
};

export function orderedJourneyStageDefinitions<T extends JourneyStageDefinitionLike>(
    stageOrder: readonly string[],
    stages: readonly T[]): T[] {
    const byKey = new Map(stages.map(stage => [stage.stageKey, stage]));
    return stageOrder.map(stageKey => {
        const stage = byKey.get(stageKey);
        if (!stage) {
            throw new Error(`Journey stage order references missing stage "${stageKey}".`);
        }
        return stage;
    });
}
