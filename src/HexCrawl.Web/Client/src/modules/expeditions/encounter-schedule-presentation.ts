import type { CampaignProcedure } from "../../types";

export type EncounterScheduleConfiguration = {
    scheduleModel: string | null;
    travelChecksPerInterval: string | null;
    campCheck: boolean | null;
    terrainProbabilityModel: string | null;
};

export function encounterScheduleConfiguration(
    procedure: CampaignProcedure): EncounterScheduleConfiguration | null {
    const module = procedure.modules.find(value => value.moduleKey === "encounters.schedule");
    if (!module) return null;

    const parameters = module.parameters;
    return {
        scheduleModel: valueOrNull(parameters.scheduleModel),
        travelChecksPerInterval: valueOrNull(parameters.travelChecksPerInterval),
        campCheck: booleanOrNull(parameters.campCheck),
        terrainProbabilityModel: valueOrNull(parameters.terrainProbabilityModel)
    };
}

export function hasEncounterSchedule(procedure: CampaignProcedure): boolean {
    return procedure.modules.some(value => value.moduleKey === "encounters.schedule");
}

function valueOrNull(value: string | undefined): string | null {
    return value && value.trim().length > 0 ? value : null;
}

function booleanOrNull(value: string | undefined): boolean | null {
    if (value === "true") return true;
    if (value === "false") return false;
    return null;
}
