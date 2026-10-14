## What changed

Describe the completed behavior and why it is needed. Link relevant issues.

## Verification and maintainability

- [ ] Added/updated relevant regression tests (including failure and concurrency paths where relevant).
- [ ] Checked existing query, validation, transport, and UI helpers before duplicating functionality.
- [ ] For scoped queries, server-side scope filtering still runs **before pagination**; scoped/unscoped behavior has parity tests.
- [ ] Server-side RBAC and Asset authorization remain authoritative; UI filtering is not a security control.
- [ ] Generated API clients and database migrations were not hand-edited.
- [ ] Structured errors, audit/correlation, localized UI text, and responsive/accessible controls remain correct where relevant.

## Design impact

- **Shared code reused or reason for intentional duplication:**
- **API, architecture, security boundary, or persistence impact:**
- **Installer/configuration impact:**
- **Open risks / follow-ups:**

Mark inapplicable checks N/A with a brief reason. CI only blocks objective convention violations; human review covers domain semantics and sensible reuse.
