# Execution ledger

Plan: ../event-platform-implementation-plan.md

- Execution: native implementation in this chat, following the user's instruction to start.
- Ruling: this is a new standalone repository on feature/event-platform; no existing checkout needs a linked worktree.
- Ruling: .NET 10 SDK installed in workspace work/runtime because the machine has .NET 9. Use the local SDK until a machine-wide 10 SDK is installed.
- Preflight: catalogue filter contracts feed AI search; Identity GUIDs feed bookings; booking confirmation produces tickets; realtime only signals committed changes. No interface conflicts found after the plan's prior self-review.
- Task 1: complete. Real PostgreSQL HTTP tests written before endpoint implementation.
- Task 1 RED: PostgreSQL harness started successfully; 3 assertions failed at unimplemented routes and the draft 404 assertion passed, as expected.
- Task 1 GREEN: 6/6 HTTP tests pass against PostgreSQL 18; React TypeScript production build passes; Docker Compose builds and serves the application; 8/8 Edge desktop/mobile browser checks pass. Desktop/mobile screenshots inspected.
- Ruling: browser tests live under src/EventPlatform.Web/e2e so they resolve the frontend's Playwright dependency without a second package manifest. Cost: planned test paths differ.
- Ruling: expose the date-window helper through a bounded read-only endpoint so the browser and future AI search share server timezone conversion. Cost: one additional API route.
- Ruling: the first milestone uses fictional Malaysian cities/events in MYR and fixed November 2026 dates. This is demo data, not an inference about the user's location. Add city/timezone mappings and refresh demo dates when needed.
- Review: fixed one-sided date filtering after reproducing the failure in both browser profiles; all 8 browser checks then passed. Also fixed select label associations after the initial browser checks could not find the controls by their exact labels.
- Review minor (resolved in Task 2): extracted WebAppFixture and CreatePublishedEventAsync for shared database and HTTP setup.
- Review minor (deferred): pin Docker image digests before deployment; moving major image tags currently permit runtime changes between local builds.
- Review rulings: later auth/booking/payment/QR/AI/AWS features are not part of the catalogue milestone; local city/currency/date restrictions remain documented; native dialog semantics are retained, with broader keyboard/assistive-technology checks still to come.
- Task 2: complete. Accounts, cookie authentication, CSRF and per-organisation permissions. Initial auth suite failed 5/5 for missing routes; final full API suite passed 12/12, including production Secure-cookie checks and unchanged database state for rejected signup. Docker production build succeeded; all 10 desktop/mobile browser checks passed. A signed-in organiser session survived force-recreating the app container.
- Task 2 ruling: demo organiser/staff accounts require a generated password in ignored .env; no passwords are hard-coded in source. Reusing a demo email already owned by a different user fails safely instead of granting it permissions. Email verification/recovery remain outside this milestone.
- Task 3: complete. Organisation-owned event CRUD and publication. Initial 8 organiser tests failed at missing routes; final API suite passed 21/21, including cross-organisation write/list denial. Docker build applied ownership migration to the existing database without dropping events. All 12 desktop/mobile browser journeys passed, including creating, editing, publishing into discovery and unpublishing.
- Task 3 ruling: editor/API support the approved demo cities and MYR explicitly, with Malaysia local times; broadening geography/currency requires extending the mappings and money formatting. Image references are HTTPS URLs until the storage milestone. The dashboard uses ordinary document navigation and lists at most the newest 200 events.
- Task 4: starting. Transactional reservations, expiry, idempotency and inventory-safe event edits.
- Tasks 5–10: pending. OpenAI key not present in the environment; Stripe connector requires reauthentication; AWS CLI not installed. User setup questions sent while independent work continues.
