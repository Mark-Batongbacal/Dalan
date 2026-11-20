(() => {
    const list = document.querySelector('[data-batch-rows]');
    if (!list) return;
    const add = document.querySelector('[data-batch-add]');
    const remove = document.querySelector('[data-batch-remove]');
    function update() {
        const rows = [...list.querySelectorAll('[data-batch-row]')];
        add.disabled = rows.length >= 15;
        remove.disabled = rows.length <= 1;
        document.querySelector('[data-batch-count]').textContent = `${rows.length} of 15 deliveries in this batch.`;
        document.dispatchEvent(new CustomEvent('planning-map-targets-replaced', { detail: { targets: rows.map((_,i) => ({
            label: `Delivery ${i+1} destination`, latitudeInputId: `Rows_${i}__DropoffLatitude`, longitudeInputId: `Rows_${i}__DropoffLongitude`, addressInputId: `Rows_${i}__DropoffAddress`
        })) }}));
    }
    add.addEventListener('click', () => {
        const index = list.children.length;
        if (index >= 15) return;
        const row = list.firstElementChild.cloneNode(true);
        row.querySelector('[data-batch-heading]').textContent = `Delivery ${index+1}`;
        row.querySelectorAll('[id],[for],[name],[data-valmsg-for]').forEach(element => {
            for (const attribute of ['id','for','name','data-valmsg-for']) if (element.hasAttribute(attribute))
                element.setAttribute(attribute,element.getAttribute(attribute).replaceAll('Rows_0__',`Rows_${index}__`).replaceAll('Rows[0]',`Rows[${index}]`));
        });
        row.querySelectorAll('input').forEach(input => { input.value=''; delete input.dataset.googleAddress; });
        row.querySelectorAll('details').forEach(details => details.open=false);
        row.querySelectorAll('.input-validation-error').forEach(element=>element.classList.remove('input-validation-error'));
        list.append(row); update();
        const picker=document.querySelector('[data-map-target]');
        if(picker) { picker.value=String(index); picker.dispatchEvent(new Event('change',{bubbles:true})); }
        row.querySelector('input').focus();
    });
    remove.addEventListener('click', () => {
        if(list.children.length<=1) return;
        const row=list.lastElementChild;
        if([...row.querySelectorAll('input')].some(input=>input.value) && !confirm('Remove the last delivery and its entered details?')) return;
        row.remove(); update();
    });
    update();
})();
