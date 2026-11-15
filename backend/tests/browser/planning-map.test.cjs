const test = require("node:test");
const assert = require("node:assert/strict");
const { coordinate, decodePolyline6 } = require("../../src/Dalan.Web/wwwroot/js/planning-map.js");

test("coordinate input distinguishes zero from blank and rejects invalid bounds", () => {
    assert.equal(coordinate("0", 90), 0);
    for (const value of ["", "  ", null, undefined, "NaN", "Infinity", "91"])
        assert.equal(coordinate(value, 90), null);
});

test("polyline6 decodes latitude/longitude in GeoJSON longitude-first order", () => {
    assert.deepEqual(decodePolyline6("??_c`|@_gayB"), [[0, 0], [2, 1]]);
});

test("truncated and out-of-range road geometries are rejected", () => {
    for (const value of ["", "_", "a", null]) assert.throws(() => decodePolyline6(value));
});
