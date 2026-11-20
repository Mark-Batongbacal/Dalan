(() => {
    "use strict";
    async function json(url, options) {
        const response = await fetch(url, { credentials: "same-origin", cache: "no-store", ...options });
        if (response.redirected || response.status === 401 || response.status === 403) throw Error("Your session or permission changed. Sign in as Admin again.");
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw Error(body.message || (response.status === 503 ? "This feature is not enabled or configured." : "The request failed. Refresh and try again."));
        return body;
    }
    function row(body, values) {
        const tr = document.createElement("tr");
        values.forEach(value => { const td = document.createElement("td"); td.textContent = value ?? "—"; tr.append(td); });
        body.append(tr);
    }
    const access = document.querySelector("[data-driver-access]");
    if (access) {
        const id = access.dataset.driverAccess, status = access.querySelector("[data-access-status]"), message = access.querySelector("[data-access-message]");
        async function refresh() {
            try { const body = await json(`/api/admin/driver-mobile-access/${id}`); status.textContent = {NoAccount:"No linked mobile account",PendingActivation:"Invitation pending activation",Enabled:"Mobile access enabled",Disabled:"Mobile access disabled"}[body.mobileAccess] || "Account requires review"; const email = access.querySelector("[name=email]"); if (!email.value && body.email) email.value = body.email; }
            catch (error) { status.textContent = error.message; }
        }
        access.querySelector("[data-access-refresh]").onclick = refresh;
        access.querySelectorAll("form").forEach(form => form.addEventListener("submit", async event => {
            event.preventDefault(); const data = new FormData(form), invite = form.dataset.accessForm === "invite";
            const action = event.submitter?.value;
            if (!invite && !["disable", "enable"].includes(action)) return;
            if (!invite && action === "disable" && !confirm("Disable this Driver's mobile access and revoke existing sessions?")) return;
            const buttons = [...access.querySelectorAll("button")]; buttons.forEach(b => b.disabled = true); message.textContent = "Submitting…";
            try {
                const body = await json(invite ? "/api/driver-invitations/issue" : `/api/admin/driver-mobile-access/${id}/${action}`, { method: "POST", headers: { "Content-Type": "application/json", RequestVerificationToken: access.querySelector("[name=__RequestVerificationToken]").value }, body: JSON.stringify(invite ? { driverId: id, email: data.get("email") } : { reason: data.get("reason") }) });
                message.textContent = body.message || "Mobile access updated. Existing sessions require a fresh sign-in."; await refresh();
            } catch (error) { message.textContent = error.message; }
            finally { buttons.forEach(b => b.disabled = false); }
        }));
        refresh();
    }
    const audit = document.querySelector("[data-account-audit]");
    if (audit) {
        let before = "", beforeId = "", busy = false; const body = audit.querySelector("tbody"), status = audit.querySelector("[data-table-status]"), more = audit.querySelector("[data-audit-more]");
        async function load(append = false) {
            if (busy) return; busy = true; more.disabled = true;
            if (!append) { body.replaceChildren(); before = ""; beforeId = ""; }
            const driver = audit.querySelector("[name=driverId]").value.trim();
            if (driver && !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(driver)) { status.textContent = "Enter a valid Driver identifier or leave it empty."; busy = false; return; }
            try {
                const query = new URLSearchParams({ take: "50" }); if (driver) query.set("driverId", driver); if (before) { query.set("beforeUtc", before); query.set("beforeId", beforeId); }
                const items = await json("/api/admin/account-audit?" + query);
                items.forEach(x => row(body, [x.occurredAtUtc, x.action, x.outcome, x.actorUserId, x.targetUserId, x.reason]));
                before = items.at(-1)?.occurredAtUtc || ""; beforeId = items.at(-1)?.id || ""; more.hidden = items.length < 50;
                status.textContent = body.children.length ? "History loaded. Times are UTC." : "No matching account events.";
            } catch (error) { status.textContent = error.message; more.hidden = true; }
            finally { busy = false; more.disabled = false; }
        }
        audit.querySelector("form").onsubmit = event => { event.preventDefault(); load(); }; more.onclick = () => load(true); load();
    }
    const emails = document.querySelector("[data-credential-emails]");
    if (emails) {
        const button = emails.querySelector("button"), body = emails.querySelector("tbody"), status = emails.querySelector("[data-table-status]");
        async function refresh() { button.disabled = true;
            try { const items = await json("/api/admin/driver-credential-emails"); body.replaceChildren(); items.forEach(x => row(body, [x.createdAtUtc, x.kind, x.status, x.attempts, x.acceptedAtUtc])); status.textContent = items.length ? "Recent delivery attempts loaded." : "No credential emails recorded."; }
            catch (error) { status.textContent = error.message; } finally { button.disabled = false; }
        }
        button.onclick = refresh; refresh();
    }
    document.querySelectorAll("[data-mobile-access-status]").forEach(async element => {
        try { const status = await json(`/api/admin/driver-mobile-access/${element.dataset.mobileAccessStatus}`); element.textContent = {NoAccount:"No account",PendingActivation:"Pending activation",Enabled:"Enabled",Disabled:"Disabled"}[status.mobileAccess] || "Needs review"; }
        catch { element.textContent = "Status unavailable"; }
    });
})();
