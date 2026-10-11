import assert from 'node:assert/strict';
import test from 'node:test';
import {
    affineRegistrationSaveAllowed,
    evaluateAffineRegistrationHoldout,
    maxAffineHoldoutErrorSourcePixels
} from '../.test-dist/affine-registration.js';

const transform = Object.freeze({kind: 'Affine', m11: 0, m12: -2, m13: 400, m21: 2, m22: 0, m23: -100, m31: 0, m32: 0});
const fittedPoints = [{sourcePixel: {x: 1, y: 1}}, {sourcePixel: {x: 100, y: 1}}, {sourcePixel: {x: 1, y: 100}}];
const landmark = sourcePixel => ({sourcePixel, worldPoint: {
    x: transform.m11 * sourcePixel.x + transform.m12 * sourcePixel.y + transform.m13,
    y: transform.m21 * sourcePixel.x + transform.m22 * sourcePixel.y + transform.m23
}});

test('exact fourth landmark has zero error after rotation and scaling', () => {
    const check = evaluateAffineRegistrationHoldout(transform, landmark({x: 123, y: 456}), fittedPoints);
    assert.equal(check.errorSourcePixels, 0);
    assert.deepEqual(check.predictedWorldPoint, {x: -512, y: 146});
    assert.equal(maxAffineHoldoutErrorSourcePixels, 12);
});

test('discrepancy is expressed in original pixels and independent of world unit size', () => {
    const point = landmark({x: 135, y: 234});
    const incorrect = {...point, worldPoint: {x: point.worldPoint.x + 10, y: point.worldPoint.y + 24}};
    const measured = evaluateAffineRegistrationHoldout(transform, incorrect, fittedPoints);
    assert.ok(Math.abs(measured.errorSourcePixels - 13) < 1e-10);
    const scaled = {...transform, m11: transform.m11 * 100, m12: transform.m12 * 100,
        m13: transform.m13 * 100, m21: transform.m21 * 100, m22: transform.m22 * 100, m23: transform.m23 * 100};
    const scaledMeasured = evaluateAffineRegistrationHoldout(scaled, {
        sourcePixel: incorrect.sourcePixel,
        worldPoint: {x: incorrect.worldPoint.x * 100, y: incorrect.worldPoint.y * 100}
    }, fittedPoints);
    assert.ok(Math.abs(scaledMeasured.errorSourcePixels - 13) < 1e-10);
});

test('does not validate a projective, singular or nonfinite transform', () => {
    assert.throws(() => evaluateAffineRegistrationHoldout({...transform, kind: 'Projective'}, landmark({x: 1, y: 2}), fittedPoints), /affine/);
    assert.throws(() => evaluateAffineRegistrationHoldout({...transform, m12: 0, m21: 0}, landmark({x: 1, y: 2}), fittedPoints), /nondegenerate/);
    assert.throws(() => evaluateAffineRegistrationHoldout(transform, {sourcePixel: {x: NaN, y: 3}, worldPoint: {x: 2, y: 3}}, fittedPoints), /finite/);
});

test('three-point legacy registration remains permitted; fourth point is a fail-closed optional check', () => {
    assert.equal(affineRegistrationSaveAllowed(3, true, false, null), true);
    assert.equal(affineRegistrationSaveAllowed(3, true, true, null), false);
    assert.equal(affineRegistrationSaveAllowed(4, true, false, 11), true);
    assert.equal(affineRegistrationSaveAllowed(4, true, false, 12), true);
    assert.equal(affineRegistrationSaveAllowed(4, true, false, 13), false);
    assert.equal(affineRegistrationSaveAllowed(4, true, false, NaN), false);
    assert.equal(affineRegistrationSaveAllowed(4, true, false, -1), false);
    assert.equal(affineRegistrationSaveAllowed(4, false, false, 0), false);
    assert.equal(affineRegistrationSaveAllowed(5, true, false, 0), false);
});

test("reusing a fitted landmark is not independent validation", () => {
    assert.throws(() => evaluateAffineRegistrationHoldout(transform, landmark({x: 1, y: 1}), fittedPoints), /must differ/);
});
