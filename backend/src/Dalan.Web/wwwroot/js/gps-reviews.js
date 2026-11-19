(() => {
 const root = document.querySelector('[data-gps-reviews]'); if (!root) return;
 const list = root.querySelector('[data-review-items]'), status = root.querySelector('[data-review-status]');
 async function load() {
  try {
   const response = await fetch('/api/gps-exceptions', {cache:'no-store'});
   if (!response.ok || response.redirected) throw Error('Unable to load GPS reviews. Check your session and feature/database configuration.');
   const items = await response.json(); list.replaceChildren();
   for (const item of items) {
    const section = document.createElement('section'); section.className = 'border-top py-3';
    const title = document.createElement('h2'); title.className = 'h5'; title.textContent = item.recipient + ' · ' + item.status; section.append(title);
    for (const value of [`Delivery ${item.deliveryId}`, `Reason: ${item.reason}`, `Submitted: ${item.submittedAtUtc}`, `Recorded fix: ${item.latitude ?? 'missing'}, ${item.longitude ?? 'missing'}; accuracy: ${item.accuracyMeters ?? 'missing'} m; time: ${item.fixAtUtc ?? 'missing'}`, `Saved destination: ${item.destinationLatitude}, ${item.destinationLongitude}`, item.reviewReason ? `Decision: ${item.reviewReason} · ${item.reviewerUserId} · ${item.reviewedAtUtc}` : 'Pending an explicit dispatcher decision']) {
     const p = document.createElement('p'); p.textContent = value; section.append(p);
    }
    const photo = document.createElement('a'); photo.href = `/api/gps-exceptions/${item.id}/photo`; photo.target = '_blank'; photo.rel = 'noopener'; photo.textContent = 'Open submitted proof photo'; section.append(photo);
    if (item.status === 'Pending') {
     const form = document.createElement('form'); form.className = 'mt-3'; const label = document.createElement('label'); label.className = 'form-label'; label.htmlFor = `reason-${item.id}`; label.textContent = 'Decision reason (8–500 characters)';
     const reason = document.createElement('textarea'); reason.id = label.htmlFor; reason.className = 'form-control mb-2'; reason.required = true; reason.minLength = 8; reason.maxLength = 500; form.append(label, reason);
     for (const approve of [true, false]) { const button = document.createElement('button'); button.className = 'btn btn-outline-secondary me-2'; button.value = String(approve); button.textContent = approve ? 'Approve exception and complete delivery' : 'Reject exception'; form.append(button); }
     form.onsubmit = async event => { event.preventDefault(); const approve = event.submitter.value === 'true';
      if (approve && !confirm('Accept this recorded GPS exception and complete the delivery?')) return;
      const buttons = [...form.querySelectorAll('button')]; buttons.forEach(x => x.disabled = true);
      try { const saved = await fetch(`/api/gps-exceptions/${item.id}/review`, {method:'POST',headers:{'Content-Type':'application/json', RequestVerificationToken:root.querySelector('[name=__RequestVerificationToken]').value},body:JSON.stringify({approve,reason:reason.value})});
       if (!saved.ok || saved.redirected) throw Error('Decision could not be saved. The assignment or review may have changed; refresh before retrying.');
       await load();
      } catch (error) { status.textContent = error.message; buttons.forEach(x => x.disabled = false); }
     }; section.append(form);
    }
    list.append(section);
   }
   status.textContent = items.length ? 'Most recent 100 requests loaded.' : 'No GPS exception requests.';
  } catch (error) { status.textContent = error.message; }
 }
 root.querySelector('[data-review-refresh]').onclick = load; load();
})();
