import assert from 'node:assert/strict';
import test from 'node:test';
import { nextCellExpeditionAction } from '../src/modules/expeditions/phase15-cell-action.ts';

const baseline = () => ({
    pendingEncounter: false,
    pauseReason: null,
    executable: true,
    hasSelectedCourse: true,
    activeWatchNumber: null,
    survival: null,
    journey: null,
    navigationDue: false,
    attentionStatus: 'ready'
});
const survival = (checkDue = false, consequences = []) => ({
    forcedTravel: { checkDue },
    pendingResourceConsequences: consequences
});
const journey = (events = [], processes = []) => ({
    eventOccurrences: events,
    activeProcesses: processes
});

for (const [name, state, kind, label] of [
    ['routine travel', {}, 'travel', 'Continue travel'],
    ['active watch', { activeWatchNumber: 3 }, 'travel', 'Resume watch 3'],
    ['forced travel', { survival: survival(true) }, 'survival', 'Resolve forced travel'],
    ['resource consequence', { survival: survival(false, [{ consequenceId: 'c' }]) }, 'survival', 'Resolve travel consequence'],
    ['journey event', { journey: journey([{ status: 'ResolutionRequired', eventType: 'Hazard' }]) }, 'journey', 'Resolve journey event'],
    ['journey process', { journey: journey([], [{ status: 'ResolutionRequired', pendingActions: [], definition: { displayName: 'Journey' } }]) }, 'journey', 'Resolve journey stage'],
    ['journey stage pending action', { journey: journey([], [{ status: 'Active', pendingActions: [{ detail: 'Pick an approach' }], definition: { displayName: 'Challenge' } }]) }, 'journey', 'Resolve journey stage'],
    ['navigation due', { navigationDue: true }, 'navigation', 'Resolve navigation'],
    ['navigation before course selection', { navigationDue: true, hasSelectedCourse: false }, 'navigation', 'Resolve navigation'],
    ['survival before unavailable movement rules', { survival: survival(true), executable: false }, 'survival', 'Resolve forced travel'],
    ['travel direction', { hasSelectedCourse: false }, 'travel', 'Choose travel direction'],
    ['encounter precedence', { pendingEncounter: true, survival: survival(true) }, 'encounter', 'Resolve encounter'],
    ['course precedence', { pauseReason: 'CellCourseAdjudicationRequired', survival: survival(true) }, 'boundary', 'Resolve boundary course'],
    ['lost-party precedence', { pauseReason: 'LostRecognitionRequired', survival: survival(true) }, 'boundary', 'Resolve lost-party decision'],
    ['survival over journey', { survival: survival(true), journey: journey([{ status: 'ResolutionRequired' }]) }, 'survival', 'Resolve forced travel'],
    ['journey over navigation', { journey: journey([{ status: 'ResolutionRequired' }]), navigationDue: true }, 'journey', 'Resolve journey event'],
    ['loading unknown aggregates', { attentionStatus: 'loading' }, 'travel', 'Loading gameplay state'],
    ['failed aggregate fetch', { attentionStatus: 'error' }, 'travel', 'Review gameplay state']
]) {
    test(name, () => {
        const actual = nextCellExpeditionAction({ ...baseline(), ...state });
        assert.equal(actual.kind, kind);
        assert.equal(actual.label, label);
        assert.equal(actual.urgent,
            ['survival', 'journey', 'navigation', 'encounter', 'boundary'].includes(kind)
                || state.attentionStatus === 'error');
    });
}
