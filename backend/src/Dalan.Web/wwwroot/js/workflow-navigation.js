function workflowLink(value, base) {
    const url = new URL(value, base);
    if (url.origin !== new URL(base).origin ||
        !/^\/(Warehouse|Vehicles|Drivers|Deliveries|DeliveryBatch|DeliveryWorkflow|DispatchBoard|FleetPlanner|FleetTracking)(\/|$)/i.test(url.pathname)) return value;
    url.searchParams.set('workflow', 'guide');
    return url.pathname + url.search + url.hash;
}

if (typeof module !== 'undefined') module.exports = { workflowLink };
if (typeof document !== 'undefined') {
    const main = document.querySelector('[data-workflow-origin]');
    if (main) {
        const mark = (element, attribute) => {
            const value = element.getAttribute(attribute);
            if (value && !value.startsWith('#')) element.setAttribute(attribute, workflowLink(value, window.location.href));
        };
        const markForm = form => {
            const value = form.getAttribute('action') || window.location.href;
            const marked = workflowLink(value, window.location.href);
            form.setAttribute('action', marked);
            const target = new URL(marked, window.location.href);
            // GET submission replaces action queries, so keep the origin alongside its filter fields.
            if ((form.getAttribute('method') || 'get').toLowerCase() === 'get' &&
                target.origin === window.location.origin && target.searchParams.get('workflow') === 'guide') {
                let input = form.querySelector('input[name="workflow"]');
                if (!input) { input = document.createElement('input'); input.name = 'workflow'; form.appendChild(input); }
                input.type = 'hidden'; input.value = 'guide';
            }
        };
        main.querySelectorAll('a[href]').forEach(link => mark(link, 'href'));
        main.querySelectorAll('form').forEach(markForm);
        main.addEventListener('click', event => {
            const link = event.target.closest('a[href]');
            if (link) mark(link, 'href');
        });
        main.addEventListener('submit', event => {
            markForm(event.target);
            if (event.submitter) mark(event.submitter, 'formaction');
        });
    }
}
