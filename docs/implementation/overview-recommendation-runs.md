# Overview and recommendation-run correction

Issue #456. Keep the established authorization and decision/execution flows.

- Overview: attention first, compact KPIs next, then Latest runs. One Inspect action on the unconfirmed-command item; it links to that command's context, not the generic approval inbox.
- Recommendations: show at most three originating runs from the authorized, filtered recent recommendation window (50 proposals). Select a run to see its proposals. Fetch the selected run's authorized proposals directly (up to 100), so the global inbox limit cannot split its proposals. Keep a View all runs link to the existing Runs history. Label the preview's bounds honestly; do not imply it is a complete count.
- Preserve exact recommendation links from Overview and Runs, including recommendations outside the recent window. Keep proposals without an originating run visible in an explicitly labelled group.
- Unconfirmed commands: name the state correctly. A command link focuses the matching authorized open command and provides a way back to the complete open-command list. Missing/settled/inaccessible commands show an unavailable message rather than an unrelated command.
- Verify KPI order, multiple-command duplication, exact command routing, three-run bounds and selection, multi-proposal runs, direct review links, permission revocation, and desktop/mobile layouts. Open a draft PR to develop; merge after current-head checks pass.
