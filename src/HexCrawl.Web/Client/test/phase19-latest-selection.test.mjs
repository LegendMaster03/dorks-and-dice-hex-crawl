import assert from "node:assert/strict";
import test from "node:test";
import { resolveLatestWorldCellSelection } from "../.test-dist/map-selection-policy.js";

const deferred = () => {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
};
const id = name => ({tilingId:"tiling",address:{motifCellId:name,translation:{u:0,v:0}}});
const geometry = name => ({id:id(name), polygon:[],center:{x:0,y:0}});
const lookup = name => ({status:"Unique",candidates:[id(name)]});

test("late geometry for A never overwrites newer B or invokes its callback", async () => {
    let generation = 0;
    const aGeometry = deferred(), bGeometry = deferred();
    const selected = [], unavailable = [];
    const start = (name, waiter) => {
        const own = ++generation;
        return resolveLatestWorldCellSelection(
            async () => lookup(name), async () => waiter.promise,
            () => generation === own, "tiling",
            status => unavailable.push(status),
            cell => selected.push(cell.id.address.motifCellId));
    };
    const a = start("A", aGeometry);
    await Promise.resolve(); // A's lookup completes and starts the geometry fetch.
    const b = start("B", bGeometry);
    await Promise.resolve();
    bGeometry.resolve(geometry("B"));
    await b;
    aGeometry.resolve(geometry("A"));
    await a;
    assert.deepEqual(selected, ["B"]);
    assert.deepEqual(unavailable, []);
});

test("world version or revision mutation during geometry fetch invalidates its callback", async () => {
    let version = 5, revision = 3;
    const first = deferred();
    const selected = [];
    const a = resolveLatestWorldCellSelection(
        async () => lookup("A"), async () => first.promise,
        () => version === 5 && revision === 3, "tiling",
        () => assert.fail("stale selection must not announce missing cell"),
        cell => selected.push(cell));
    await Promise.resolve();
    version++;
    revision++;
    first.resolve(geometry("A"));
    await a;
    assert.deepEqual(selected, []);
});

test("wrong-tiling response and late ambiguous lookup cannot publish", async () => {
    const selected=[], unavailable=[];
    await resolveLatestWorldCellSelection(
        async()=>lookup("A"), async()=>({...geometry("A"),id:{...id("A"),tilingId:"other"}}),
        ()=>true,"tiling", status=>unavailable.push(status), value=>selected.push(value));
    let current = true;
    const slowLookup = deferred();
    const pending = resolveLatestWorldCellSelection(
        ()=>slowLookup.promise,async()=>geometry("A"),
        ()=>current,"tiling",status=>unavailable.push(status),value=>selected.push(value));
    current=false;
    slowLookup.resolve({status:"Ambiguous",candidates:[id("A"),id("B")]});
    await pending;
    assert.deepEqual(selected, []);
    assert.deepEqual(unavailable, []);
});
