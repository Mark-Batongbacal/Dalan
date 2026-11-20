const test = require('node:test');
const assert = require('node:assert/strict');
const { workflowLink } = require('../../src/Dalan.Web/wwwroot/js/workflow-navigation.js');
const base = 'https://dalan.test/Deliveries?workflow=guide';
test('workflow links preserve queries and anchors without duplicating context', () => {
    assert.equal(workflowLink('/Warehouse?workspace=true#pin', base), '/Warehouse?workspace=true&workflow=guide#pin');
    assert.equal(workflowLink('/DispatchBoard?workflow=guide', base), '/DispatchBoard?workflow=guide');
    assert.equal(workflowLink('Create', base), 'Create');
});
test('profile, unrelated pages, and external links never gain workflow context', () => {
    for (const value of ['/Account/Profile', '/Analytics', 'https://other.test/Warehouse', '//other.test/Deliveries', 'mailto:dispatch@example.test'])
        assert.equal(workflowLink(value, base), value);
});

test('GET filters keep workflow origin alongside dates', () => {
    const fs = require('node:fs');
    const vm = require('node:vm');
    const attributes = { method: 'get' };
    const fields = [{ name: 'from', value: '2026-11-20' }];
    const form = {
        getAttribute: name => attributes[name],
        setAttribute: (name, value) => attributes[name] = value,
        querySelector: () => fields.find(input => input.name === 'workflow'),
        appendChild: input => fields.push(input)
    };
    const main = {
        querySelectorAll: selector => selector === 'form' ? [form] : [],
        addEventListener: () => {}
    };
    vm.runInNewContext(fs.readFileSync(require.resolve('../../src/Dalan.Web/wwwroot/js/workflow-navigation.js'), 'utf8'), {
        URL, window: { location: { href: 'https://dalan.test/FleetTracking?workflow=guide', origin: 'https://dalan.test' } },
        document: { querySelector: selector => { assert.equal(selector, '[data-workflow-origin]'); return main; }, createElement: () => ({}) }
    });
    assert.equal(attributes.action, '/FleetTracking?workflow=guide');
    assert.deepEqual(fields.map(input => [input.name, input.value]), [['from', '2026-11-20'], ['workflow', 'guide']]);
    assert.equal(fields[1].type, 'hidden');
});

test('strategy suggestion submitter preserves workflow origin on its own destination', () => {
    const vm = require('node:vm');
    const listeners = {};
    const action = { formaction: '/DispatchBoard/SuggestStrategy' };
    const submitter = { getAttribute: name => action[name], setAttribute: (name, value) => action[name] = value };
    const form = { getAttribute: name => name === 'method' ? 'post' : '/DispatchBoard', setAttribute: () => {} };
    vm.runInNewContext(require('node:fs').readFileSync(require.resolve('../../src/Dalan.Web/wwwroot/js/workflow-navigation.js'), 'utf8'), {
        URL, window: { location: { href: 'https://dalan.test/DispatchBoard?workflow=guide', origin: 'https://dalan.test' } },
        document: { querySelector: () => ({ querySelectorAll: () => [], addEventListener: (name, listener) => listeners[name] = listener }) }
    });
    listeners.submit({ target: form, submitter });
    assert.equal(action.formaction, '/DispatchBoard/SuggestStrategy?workflow=guide');
});
