const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');

function workspace() {
    const handlers = {};
    const classes = new Set();
    const link = { focus() { document.activeElement = link; }, setAttribute() {} };
    const navigation = {
        classList: { toggle(name) { if (classes.has(name)) { classes.delete(name); return false; } classes.add(name); return true; }, remove(name) { classes.delete(name); }, contains(name) { return classes.has(name); } },
        contains(target) { return target === link; }, querySelector() { return link; }, querySelectorAll() { return [link]; }
    };
    const toggle = { attributes: {}, setAttribute(key, value) { this.attributes[key] = value; }, addEventListener(key, handler) { handlers['toggle:' + key] = handler; }, contains(target) { return target === toggle; }, focus() { document.activeElement = toggle; } };
    const document = { body: { style: {} }, activeElement: null, querySelector(selector) { return selector === '.nav-toggle' ? toggle : null; }, getElementById(id) { return id === 'main-navigation' ? navigation : null; }, addEventListener(key, handler) { handlers[key] = handler; } };
    vm.runInNewContext(fs.readFileSync(require.resolve('../../src/Dalan.Web/wwwroot/js/site.js'), 'utf8'), { document, window: { matchMedia() { return { matches: true, addEventListener(key, handler) { handlers.resize = handler; } }; } } });
    return { handlers, document, toggle, link };
}
test('mobile navigation focuses its first link and Escape restores the toggle', () => {
    const { handlers, document, toggle, link } = workspace();
    handlers['toggle:click']();
    assert.equal(toggle.attributes['aria-expanded'], 'true');
    assert.equal(document.activeElement, link);
    handlers.keydown({ key: 'Escape' });
    assert.equal(toggle.attributes['aria-expanded'], 'false');
    assert.equal(document.activeElement, toggle);
});
test('leaving the menu or resizing closes its expanded state', () => {
    const { handlers, toggle } = workspace();
    for (const event of ['click', 'focusin', 'resize']) {
        handlers['toggle:click']();
        handlers[event]({ target: {} });
        assert.equal(toggle.attributes['aria-expanded'], 'false');
    }
});
