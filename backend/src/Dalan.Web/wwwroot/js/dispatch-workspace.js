(() => {
    'use strict';
    const workspace = document.querySelector('[data-dispatch-workspace]');
    if (!workspace) return;
    const search = workspace.querySelector('[data-assignment-search]');
    const cards = [...workspace.querySelectorAll('[data-assignment-card]')];
    const form = document.getElementById('dispatch-preparation');
    const deliveryChecks = [...form.querySelectorAll('input[type=checkbox][name^="Deliveries["]')];
    const vehicleChecks = [...form.querySelectorAll('input[type=checkbox][name^="Vehicles["]')];
    let status = 'all';
    function filter() {
        const text = search.value.trim().toLocaleLowerCase();
        let visible = 0;
        for (const card of cards) {
            const show = (status === 'all' || card.dataset.status === status) && card.textContent.toLocaleLowerCase().includes(text);
            card.hidden = !show; if (show) visible++;
        }
        workspace.querySelector('[data-assignment-count]').textContent = `${visible} of ${cards.length} eligible deliveries shown. Filtering preserves your selections.`;
    }
    search.addEventListener('input', filter);
    workspace.querySelectorAll('[data-delivery-filter]').forEach(button => button.addEventListener('click', () => {
        status = button.dataset.deliveryFilter;
        workspace.querySelectorAll('[data-delivery-filter]').forEach(other => {
            other.classList.toggle('active', other === button); other.setAttribute('aria-pressed', String(other === button));
        }); filter();
    }));
    function counts() {
        const jobs = deliveryChecks.filter(x => x.checked).length, fleet = vehicleChecks.filter(x => x.checked).length;
        workspace.querySelector('[data-selection-status]').textContent = `${jobs} / 15 deliveries · ${fleet} / 8 vehicles selected${jobs >= 15 ? ". Delivery limit reached; deselect one to choose another." : ""}${fleet >= 8 ? ". Vehicle limit reached; deselect one to choose another." : ""}`;
        workspace.querySelector('[data-fleet-count]').textContent = `${fleet} selected · ${vehicleChecks.length} available`;
        deliveryChecks.forEach(x => { x.disabled = !x.checked && jobs >= 15; });
        vehicleChecks.forEach(x => { x.disabled = !x.checked && fleet >= 8; });
    }
    [...deliveryChecks, ...vehicleChecks].forEach(check => check.addEventListener('change', counts));
    const descriptions = {
        MinimumTotalTravelTime: 'Minimize total fleet driving. Existing assignments remain binding.',
        WorkloadEquity: 'Reduce the spread of driver workloads, including your existing-work estimates.',
        WarehouseClusterRotation: 'Group nearby destinations and rotate driver priority after confirmation.',
        EarliestFleetCompletion: 'Reduce the longest driver workload, including existing work.'
    };
    const strategy = form.querySelector('#Strategy');
    function explain() { workspace.querySelector('[data-strategy-description]').textContent = descriptions[strategy.value] + ' Only the selected strategy runs.'; }
    strategy.addEventListener('change', explain);
    function view(value) {
        workspace.dataset.workspaceView = value;
        workspace.querySelectorAll('[data-workspace-view]').forEach(button => {
            button.classList.toggle('active', button.dataset.workspaceView === value);
            button.setAttribute('aria-pressed', String(button.dataset.workspaceView === value));
        });
        window.dispatchEvent(new Event('resize'));
    }
    workspace.querySelectorAll('[data-workspace-view]').forEach(button => button.addEventListener('click', () => view(button.dataset.workspaceView)));
    workspace.querySelectorAll('[data-focus-route]').forEach(button => button.addEventListener('click', () => {
        view('map'); workspace.querySelector('[data-planning-map]').dispatchEvent(new CustomEvent('dalan-route-focus', {detail:button.dataset.focusRoute}));
        workspace.querySelector('[data-map-canvas]').focus({preventScroll:true});
    }));
    function invalidatePreview() {
        const review = workspace.querySelector('[data-review-preview]');
        if (!review) return;
        review.disabled = true;
        workspace.querySelector('[data-preview-validity]').textContent = 'Selections changed. Preview routes again before reviewing.';
    }
    form.addEventListener('input', invalidatePreview);
    form.addEventListener('change', invalidatePreview);
    form.addEventListener('submit', event => {
        if (form.getAttribute('aria-busy') === 'true') { event.preventDefault(); return; }
        queueMicrotask(() => {
        if (event.defaultPrevented) return;
        const progress = workspace.querySelector('[data-preview-progress]'); progress.hidden = false;
        progress.textContent = event.submitter?.formAction?.includes('SuggestStrategy') ? 'Checking your selections…' : 'Calculating your route preview… Nothing is dispatched until you review and confirm.';
        form.setAttribute('aria-busy', 'true');
        // Do not disable bound inputs: every selected collection item must still be posted.
        workspace.querySelector('[data-preview-submit]').disabled = true;
        });
    });
    window.addEventListener('pageshow', () => { form.removeAttribute('aria-busy'); workspace.querySelector('[data-preview-submit]').disabled = false; workspace.querySelector('[data-preview-progress]').hidden = true; });
    const warehouseLabel = workspace.querySelector('[data-workspace-warehouse]');
    function warehouse() {
        const name = document.getElementById('Settings_WarehouseAddress').value.trim();
        const lat = document.getElementById('Settings_WarehouseLatitude').value;
        const lon = document.getElementById('Settings_WarehouseLongitude').value;
        warehouseLabel.textContent = name && lat !== '' && lon !== '' ? `${name} · confirmed point` : 'Set warehouse · location needed';
    }
    document.addEventListener('warehouse-default-loaded', warehouse);
    ['Settings_WarehouseAddress','Settings_WarehouseLatitude','Settings_WarehouseLongitude'].forEach(id => document.getElementById(id).addEventListener('input', warehouse));
    // Errors and returned results need to remain reachable even when the mobile map was selected.
    view('deliveries'); counts(); filter(); explain(); warehouse();
    const error = workspace.querySelector('[data-dispatch-error]');
    if (error) { form.querySelectorAll('details').forEach(x => x.open = true); error.focus(); }
    const result = workspace.querySelector('#dispatch-plan-results');
    if (result && !error) { result.setAttribute('tabindex', '-1'); result.focus({preventScroll:true}); }
})();
