const test = require("node:test");
const assert = require("node:assert/strict");
const { warehouse, keyFor } = require("../../src/Dalan.Web/wwwroot/js/warehouse-selection.js");
const { nextSlot, localValue } = require("../../src/Dalan.Web/wwwroot/js/delivery-time.js");

test("warehouse selection accepts zero coordinates and preserves a checked address", () => {
    assert.deepEqual(warehouse({ latitude: "0", longitude: "0", address: "  Main warehouse  " }),
        { latitude: 0, longitude: 0, address: "Main warehouse" });
    assert.equal(warehouse({ latitude: 14, longitude: 121 }).address, "Warehouse");
});
test("invalid saved warehouses cannot restore bogus coordinates", () => {
    for (const value of [null, {}, { latitude: "", longitude: "0" }, { latitude: "0", longitude: "" },
        { latitude: 91, longitude: 121 }, { latitude: 14, longitude: 181 },
        { latitude: "NaN", longitude: 121 }, { latitude: 14, longitude: 121, address: "x".repeat(501) }])
        assert.equal(warehouse(value), null);
});
test("saved warehouse keys are scoped to the authenticated account", () => {
    assert.notEqual(keyFor("first-account"), keyFor("second-account"));
    assert.equal(keyFor(""), null);
});
test("deadline defaults to the next half-hour and is strictly later than now", () => {
    assert.equal(localValue(nextSlot(new Date(2040, 0, 1, 12, 12, 5))), "2040-01-01T12:30");
    assert.equal(localValue(nextSlot(new Date(2040, 0, 1, 12, 30))), "2040-01-01T13:00");
    assert.equal(localValue(nextSlot(new Date(2040, 0, 1, 23, 59))), "2040-01-02T00:00");
});
test("local picker values round-trip to an explicit UTC timestamp", () => {
    const chosen = new Date(2040, 5, 2, 13, 30);
    const local = localValue(chosen);
    assert.equal(new Date(local).toISOString(), chosen.toISOString());
    assert.equal(local.endsWith("13:30"), true);
});
