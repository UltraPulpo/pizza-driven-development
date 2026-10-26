# Pizza-Driven Development - Lean Implementation Plan

## 1. Overview

**Objective:** Deliver the assessment's working local pizza builder and separate order service, prioritizing exact pricing, durable submission retries, atomic lifecycle changes, and an honest handoff.

**Scope:** Interactive WEB journeys cover menu selection; line-item add/remove/edit; running quotes; description; dollar/percentage tips; one promo; submission/retry; status lookup; and NotStarted updates/cancellation. A manual HTTP advancement endpoint is the approved restaurant simulation, not a substitute for customer-facing functionality. Demonstrate the two processes communicating through the actual Vite proxy.

**Exclusions:** No authentication, payments, menu administration, multiple locations, kitchen UI/timer, production deployment, visual polish, browser-persistent drafts, polling, schema migrations, or multi-promo combination engine. Keep all approved functional scope; a later cut requires user approval.

**Assurance:** Smoke-first and risk-aware, while retaining the explicit invariant cases in LLD sections 8.1-8.4. Use parameterized pricing tests, real HTTP/SQLite tests, fetch-helper tests, and explicit manual WEB checks. No coverage quota, exhaustive browser suite, DOM framework, or new test platform.

**Design inputs:** `design\pizza-low-level-design.md` (LLD), its linked `design\pizza-requirements.md` (requirements), and `design\pizza-high-level-design.md` (HLD). The local-only assessment DOCX remains authoritative and must never be staged. Follow root `AGENTS.md`; no application code or existing design changes are authorized by creation or revision of this plan.

**Constraints:** Keep the roughly three-hour assessment proportionate; no invented cost budget, concurrency target, or model override. Use the existing stack and concrete services. No global installs. All execution artifacts stay within the repository; temporary test databases use unique ignored `data\pdd-test-<guid>.db` files, never `data\pdd.db`.

**Unresolved decisions:** None blocking this revision. It applies only the six confirmed interview decisions; no additional UI behavior is prescribed. The user approved at most **two automatic repair rounds per checkpoint**. Significant design/scope changes still require an immediate user decision. C1 and C2 explicitly settle unproven implementation contracts before dependent work; their existence is not a claim those contracts already work.

**Execution continuation:** Tasks 1.1/1.2/C1/3.1/3.2/C2 are complete. The user approved C2 and explicitly requested its local commit, then continuation. Task 4.1 is ready against the established C1/C2 contracts; lifecycle mutations remain unimplemented until 4.1/4.2 prove them.

**Local history:** Authorized C1 increment committed as `0822511` (`Add data-driven menu and exact live order quotes`). Working tree was clean immediately afterward. Later work remains uncommitted unless separately requested.

## 2. Implementation Sequence and Shared Contracts

**Reused foundations:** Two existing application projects and test projects; .NET 10/NUnit/`WebApplicationFactory<Program>`; React/TypeScript/Vite/Vitest; API health route; embedded idempotent schema loading; content-root-relative SQLite path resolution and WAL startup; `/api` proxy on WEB port 5173 to API port 5105; existing build/test CI jobs. Inspection establishes these foundations exist, not that this planning session has executed their checks. The empty OrderService, comment-only schema, scaffold App, placeholder API test, and import-only WEB smoke test do not prove a business slice.

**Sequence:** Minimal menu/network spine (1.1) -> complete multi-line quote reference slice (1.2) -> C1 contract gate -> durable submit/status API (3.1) -> browser submission/recovery (3.2) -> C2 persistence/retry gate -> cancellation/advancement (4.1) -> replacement updates (4.2) -> documentation and local hardening (5.1) -> C3 release gate. The separate draft-builder slice is absorbed into the reference slice; existing IDs are otherwise retained. The new task boundaries add no checkpoints or approval stops.

**Contract status and ownership:**

| Contract | Status at planning | Owner and stabilization |
|----------|--------------------|-------------------------|
| Existing connection factory, initializer, embedded resource, proxy, ports, runners | Established in source; runtime evidence not gathered here | Preserve existing owners/configuration; extend only where a task needs it. |
| Menu, OrderInput, TipInput, PriceQuote, camelCase JSON and integer-cent wire fields | Established; C1 user-approved | Tasks 1.1/1.2 evidence and canonical implementation references below. |
| `PricingService.Quote(OrderInput)` and `IPromoRule.GetDiscount(OrderInput, int subtotalCents)` | Established; C1 user-approved | API owns all validation, exact arithmetic, descriptions, promo eligibility, and structural single-promo exclusivity. |
| Problem Details, `OrderException` kinds, WEB `ApiError`, finite 10-second timeout | Quote/input/helper paths established; C1 user-approved | API owns binding/business/error mapping; WEB preserves messages and distinguishes transport uncertainty. C2 still owes storage-failure behavior. |
| OrderService async submit/get/update/cancel/advance methods, with cancellation tokens | Submit/get established; C2 user-approved. Mutations unimplemented. | Task 3.1 proves submit/get before browser consumption in 3.2; 4.1 adds cancel/advance and 4.2 adds update using the existing pricing path. |
| Receipt, current OrderView, immutable SubmissionAttempt, retry header and storage | Established; C2 user-approved | Task 3.1 owns persistence/HTTP contracts; 3.2 owns browser recovery. 4.1/4.2 preserve original retry information. |
| Status strings, expected-status advancement, conditional mutation outcomes | Specified by LLD 4.1/4.4/5; unimplemented | Task 4.1 proves progression/cancellation and the shared conditional-write/error pattern; 4.2 reuses it for updates. C3 reviews the integrated guarantees. |

Use the LLD's existing endpoint contracts, not a new API design: GET menu, POST quote, POST orders with GUID `Idempotency-Key`, GET/PUT order, POST cancel, and POST advance with `expectedStatus`, all under `/api`. Keep health. Money is checked nonnegative int cents, calculated with decimal intermediates and midpoint-away-from-zero rounding; tip `value` stays decimal text. Quote, submit, and update share one pricing method. WEB only formats returned cents.

Only declare types as they acquire a real consumer. Group related records; no generic repository, result framework, shared-runtime pricing library, or generated client. Menu configuration failures fail startup; expected input errors do not become startup/configuration errors.

**Canonical contracts:** Once implemented within the approved LLD, C# DTOs and HTTP serialization tests are the wire-contract source of truth; small handwritten TypeScript types mirror them. At C1/C2, link the relevant sources/tests and record decisions, not another complete field/signature specification in this plan. The approved design behavior remains binding; source code does not silently override it.

**C1 canonical implementation references (user-approved):** Menu records are in [`Models\Menu.cs`](../src/pdd-api/Models/Menu.cs); input, quote, and expected-error records are in [`Models\Order.cs`](../src/pdd-api/Models/Order.cs). [`MenuHttpTests.cs`](../tests/pdd-api/MenuHttpTests.cs) and [`QuoteHttpTests.cs`](../tests/pdd-api/QuoteHttpTests.cs) prove camelCase serialization, integer-cent components, and 400 Problem Details including binding errors. [`PricingTests.cs`](../tests/pdd-api/PricingTests.cs) proves the shared pricing/promo boundary. [`types.ts`](../src/pdd-web/client/types.ts) mirrors every menu/input/quote field; [`api.ts`](../src/pdd-web/client/api.ts) and [`api.test.ts`](../tests/pdd-web/api.test.ts) implement/prove raw tip text, HTTP errors versus transport/timeout, and the ten-second timeout. Tasks 3.1/3.2/4.1/4.2 consume these established contracts without changing their shapes; C2 still gates persistence-dependent mutations.

**C2 canonical server boundary (user-approved):** [`Models\Order.cs`](../src/pdd-api/Models/Order.cs) defines receipt/current-view/status contracts. [`OrderService.cs`](../src/pdd-api/Services/OrderService.cs), [`schema.sql`](../src/pdd-api/Data/schema.sql), and [`SubmissionHttpTests.cs`](../tests/pdd-api/SubmissionHttpTests.cs) prove POST/GET, original-receipt replay, typed-input matching, one-statement unique insertion, stored snapshots, and explicit 400/404/409/503/safe-500 responses. Tasks 4.1/4.2 extend the same concrete service, conditional SQL, DTOs, and error mapping without changing original retry fields.

**C2 canonical browser references (user-approved):** Receipt/current-state/status/readonly-attempt types in [`types.ts`](../src/pdd-web/client/types.ts) mirror the passing C# wire contracts. [`api.ts`](../src/pdd-web/client/api.ts) captures/freeze-serializes an attempt once and resends its body/key without automatic writes. [`App.tsx`](../src/pdd-web/client/App.tsx) distinguishes unknown submission from accepted/status-unavailable, locks edits/new submissions until recovery, and keeps accepted state separate from the draft. The existing helper suite and 3.2 browser evidence below cover both recovery branches. Tasks 4.1/4.2 reuse this accepted-state/draft separation and current-quote gate. No C1 wire shape changed.

C1/C2 are now user-approved and the implementation references above settle their contracts for 4.1/4.2. The remaining tasks implement only the approved cancel/advance/replacement extensions, preserving the established receipt, current view, pricing, and error shapes. A later contract change requires a consumer/fixture/documentation impact assessment; ask the user when it exceeds the agreed design or scope.

## 3. Shared Execution and Assurance Policy

- **Execution:** Sequential. The prerequisite chain serializes shared `Program.cs`, `App.tsx`, DTOs, request helpers, and HTTP fixtures. "None" in Conflicts means no additional independently runnable conflict, not disjoint files. Do not add parallel workers or prescribe models for this small assessment.
- **Responsibilities:** The executor is also the integrator and maintains the single tracker. Any bounded worker owns its targeted checks, not Git staging/commits. The integrator owns checkpoints. This plan neither starts execution nor invokes other skills/agents.
- **Verification:** API commands run at repository root; WEB commands run in `src\pdd-web`. Existing runners are `dotnet test PDD.slnx`, `npm test`, `npm run build`, and `npm run lint`. Named test fixtures/files below are established by their owning tasks, not claimed to exist today. Start with one small failing rule/HTTP/helper test where practical, then implement and keep affected checks green. No tooling installation unless a chosen command actually fails for a missing dependency or a manifest change requires it.
- **Gate breadth:** At C1/C2 run affected API tests, applicable WEB tests/type-check, WEB build/lint, and API build. C3 uses all final commands below. Broaden an intermediate check only for a material impact. Preserve CI's Release build/test behavior; do not add a CI framework or coverage threshold.
- **Evidence reuse:** Reuse current results for unchanged code/configuration; changes affecting a result invalidate it. Reviewers inspect that evidence instead of rerunning everything by default. Record brief actual results at the relevant checkpoint during execution, not invented results now.
- **Review:** Retain independent user review and approval stops at **all three checkpoints: C1, C2, and C3**, unless the user explicitly assigns another non-implementer. Review only affected contracts/interactions and current evidence, not discretionary redesign. Implementer self-review is useful but does not replace any of these reviews. Blocking findings are requirement violations, correctness defects, or material regressions.
- **Repair limit:** User-approved maximum **two automatic repair rounds per checkpoint**. A round is a targeted repair, verification, and focused re-review where needed. Stop earlier for a design/scope decision; after two unsuccessful rounds stop and escalate evidence and the remaining issue to the user. Do not replace this limit with repeated fresh-agent retries.
- **Local Git:** The user's explicit **commit-only-when-asked** rule overrides any autonomous-commit default. Execution remains uncommitted unless the user requests a commit. On authorization, the integrator stages only named files for the coherent verified increment, inspects the staged diff, includes the required co-author trailer, and protects unrelated/pre-existing or already-staged changes. Workers never stage/commit. Suggested boundaries below are opportunities, not permission or mandatory per-task commits. Do not switch/create branches, stash, reset, rebase, amend, or rewrite history without explicit authorization.
- **Remote boundary:** Never push to any remote, create/update PRs, mutate remote repositories, or deploy. The user handles publication. Checkpoint completion and local release readiness grant no remote-write permission.
- **Test resources:** Reuse a small real HTTP fixture with per-fixture file-backed SQLite and independent operation connections. Override configuration before host startup. Dispose hosts/connections and clear only the relevant pools before removing the fixture's exact DB/WAL/SHM files on Windows. No broad cleanup or access to the runtime DB. Concurrency uses coordinated request gates, not sleeps, production hooks, or random-winner assertions.

## 4. Slice and Task Details

### Slice 1: Reference menu-to-quote journey

#### Task 1.1: Establish the minimal menu/network spine

**Prerequisites:** None

**Conflicts with:** None

**Context:** Required: LLD 2.2, menu contract in 3.2, GET menu in 4.1, 7, and menu/fixture guidance in 8.1/8.4; existing `Program.cs`, `Data\SqliteConnectionFactory.cs`, `Data\DatabaseInitializer.cs`, `PDD.API.csproj`, WEB `App.tsx`/`vite.config.ts`, and both existing test projects. HLD 3 is optional background.

**Contracts:** Preserve established infrastructure; implement the proposed menu shape and minimal typed fetch/error/timeout boundary from section 2.

**Expected files:** API menu model, `menu.json`, `Services\MenuCatalog.cs`, API composition/project content settings; WEB `client\App.tsx`, `client\api.ts`, `client\types.ts`; meaningful menu/helper tests and a small HTTP fixture in the existing test directories.

**Objective:** The browser loads the real API menu and renders a usable size/topping selection, with visible load failures.

**Instructions:**
1. Store the brief's three sizes, two percentage categories, and twelve toppings as data. Load once from content root; package the file with the API. Reject duplicate IDs, dangling category references, and invalid prices/rates at startup.
2. Add GET `/api/menu`, retaining health/startup behavior. Use the existing connection factory and initializer in the HTTP fixture; no Orders schema or placeholder order DTOs yet.
3. Replace demo markup only as needed for the selection UI. Establish typed fetch, Problem Details extraction, and the fixed timeout without a configuration subsystem. Do not add a router, DOM test dependency, or broad CSS cleanup.
4. Replace the no-op API test once meaningful menu checks exist. Retire the import-only WEB smoke test when real helper tests supersede it, rather than preserving scaffold-only assertions.

**Acceptance criteria:**
- API returns all expected menu names, IDs, prices, and category references; the browser obtains them over `/api`, not a copied menu.
- Invalid menu fixtures fail clearly; menu HTTP failures are visible rather than an empty/success-shaped menu.
- Existing path resolution, WAL/schema initialization, health route, and project builds remain intact.

**Verification:**
- Establish menu fixture names matching `Menu`; run `dotnet test PDD.slnx --filter "FullyQualifiedName~Menu"`.
- Establish `tests\pdd-web\api.test.ts`; run `npm test -- api.test.ts`, `npm run build`, and `npm run lint`.
- Manually start both existing processes and confirm GET menu through Vite. This is a menu smoke, not proof of pricing or ordering.

**Requirements covered:** Menu foundation for FR-1/FR-11; IR-1 foundation; QR-2.

**Execution evidence:** Task 1.1 complete. Menu HTTP test first failed with 404; after implementation, 11 menu tests pass (including invalid configuration and real SQLite WAL/health). Six fetch-helper tests, WEB type-check/build/lint pass. API and Vite ran as separate processes; `http://localhost:5173/api/menu` returned three sizes/twelve toppings, proxied health returned `ok`, and the browser rendered size/topping controls. Used an isolated ignored test DB, not `data\pdd.db`. No persistence behavior claimed.

#### Task 1.2: Prove exact live quotes through the complete draft builder

**Prerequisites:** 1.1

**Conflicts with:** None

**Context:** Required: LLD 3.1-3.3, quote/draft/description behavior in 4.1-4.2, 6, and pricing/promo/helper cases in 8.1; requirements FR-1 through FR-5 and section 8; task 1.1's actual menu/fetch interfaces. No order persistence context is required.

**Contracts:** Implement the proposed OrderInput/TipInput/PriceQuote, pricing/promo signatures, and 400 Problem Details boundary. C1 must prove these before expansion.

**Expected files:** Grouped API order/quote/error models; `PricingService.cs`, `PromoRules.cs`, API wiring; WEB input/quote types, helper, `App.tsx`, and minimal form CSS only if needed; pricing/quote HTTP/helper tests.

**Objective:** A customer can add/remove/edit multiple pizza lines, quantities, toppings, tip, and promo and see an exact API-generated breakdown and description. Use the final items-array draft representation from the first quote UI implementation, avoiding an interim single-line form and a later conversion.

**Instructions:**
1. Implement the complete pricing pipeline once: per-pizza topping rounding before quantity; subtotal; one eligible promo; discounted tax and percentage-tip bases; rounded dollar tip; checked final sum. Implement both promo rules and all input validation from LLD 3.3.
2. Keep extensibility concrete: menu fixtures add size/category/topping data; a test-only registered order-dependent promo proves the rule signature without production engine changes. Validate unique registered promo codes at startup.
3. Add POST `/api/quote` and expected error mapping, normalizing malformed/binding inputs as well as business validation. Do not create/persist orders.
4. Wire the multi-line WEB form to live quotes. Every relevant edit, including line removal, tip, and promo, invalidates old totals immediately. Abort superseded calls and guard by draft revision, show pending/errors, and never calculate prices in WEB. Preserve tip text; show dollars/whole cents and provide Show Description.
5. Keep temporarily invalid form text editable. Pre-submit Save Changes reuses validation/quoting and never persists a draft. Keep the form simple and labeled; do not add a state framework or duplicate pricing validation in WEB.

**Acceptance criteria:**
- Independent parameterized fixtures cover every size, basic/premium charges, quantity/multiple lines, no toppings/zero tip, component sums, and descriptions including the no-topping case.
- Half-cent checks include plain Small tax 33 cents, dollar tip `"1.005"` -> 101 cents, and synthetic topping rounding before quantity. The LLD's two-Large/ILOVEPIZZA/15% case yields exactly **3,123 cents** with its specified breakdown.
- SAVE5 rejects 2,000 cents and accepts 2,001; exact-code matching rejects unknown/wrong-case/padded/empty/multiple-code text. A test-only Large-required promo distinguishes equal-subtotal orders. Added menu data prices correctly without algorithm edits.
- Empty/invalid items, nonpositive quantities, unknown IDs, repeated toppings, invalid/missing/negative tips, and overflow produce clear rejection; a representable percentage above 100 is accepted.
- Browser totals/descriptions come from the latest successful quote only. A superseded response cannot replace it; pending/failed quotes cannot look current. Returned Problem Details are visible.
- Add two differently configured lines, modify/remove them, and change both tip modes/promo with matching current breakdown/description. Empty or invalid drafts show a clear message and no current valid charge; Save Changes remains draft-only. Expose current-quote validity for later submission/update gating.

**Verification:**
- Establish `Pricing` and `Quote` fixture names; run `dotnet test PDD.slnx --filter "FullyQualifiedName~Pricing|FullyQualifiedName~Quote|FullyQualifiedName~Menu"`. Use fixed expected cents, not production helpers to compute assertions.
- Run `npm test -- api.test.ts`; include raw tip preservation, Problem Details, network rejection, and fake-timer timeout checks with restored fetch/timers.
- Manually add/remove/modify lines, change tip/promo/toppings, request description, and deliberately reorder quote responses using browser/network controls, including after line deletion. Observe pending, validation failure, and recovery. This single integrated pass replaces the separate draft-builder pass; no duplicate unit pricing tests or DOM suite.

**Requirements covered:** Interactive FR-1 through FR-5, FR-11/FR-12; QR-1/QR-2.

**Execution evidence:** Task 1.2 complete. The reference quote HTTP test first failed with 404. Current combined Menu/Pricing/Quote NUnit run passes **66 tests**, including the 3,123-cent reference, per-pizza rounding, SAVE5 2,000/2,001 boundaries, content-dependent promo/new menu fixtures, malformed/null/missing input, and overflow. API build passes with zero warnings/errors; menu is copied to output. **Seven** WEB helper tests plus test type-check, production build, and lint pass.

Browser-driven checks used the real Vite proxy and separate API process: two Small/no-topping pizzas plus three Medium/Onions/Garlic pizzas and dollar tip `1.005` showed **$38.27** and the complete description; changing to ILOVEPIZZA and 15% showed **$38.26**. Wrong-case promo and empty order showed clear errors and no stale total. Quantity text `-` remained editable, produced a visible rejection, and disabled description; correction recovered quoting. Save Changes explicitly validated only the draft. Browser-only interception held genuine API responses while ignoring caller abort: after changing quantity and deleting the second line, releasing the newest response displayed **$21.87**; releasing the older two-line response afterward left the same quote. Pending state hid the old charge. All interception hooks were removed. Temporary API/Vite processes were stopped and only the owned smoke DB/WAL/SHM files were removed; the user's runtime DB was untouched.

#### Checkpoint C1: Stabilize the quote contract before expansion

**Prerequisites:** 1.2

**Conflicts with:** None

**Required evidence:** Current 1.1/1.2 checks; API `dotnet build PDD.slnx`; WEB build/lint; successful real two-process multi-line quote plus one rejected input and the manual stale-response check.

**Review:** Independent user review of menu data, exact arithmetic, C#/TS JSON parity, validation ownership, error display, and quote revision handling. Inspect configuration packaging and the independent expected-cent fixtures. No persistence guarantees are claimed at this gate.

**Completion condition:** Success and essential failure paths work; section 2 links canonical menu/quote/error DTOs and HTTP tests, with TS parity checked. Reconcile any design discrepancy with the user and update provisional 3.1/3.2/4.1/4.2 packets before proceeding.

**On failure:** Apply section 3's repair limit and escalation policy.

**Approval:** User confirms the reference pattern and stable contracts; material design deviations need explicit approval.

**Local commit boundary:** Menu plus reference quote, only if the user requests a commit.

**Execution evidence:** C1 checks and two-process/browser evidence above are current. Self-inspection compared menu/input/quote wire fields with TypeScript, checked startup menu/promo validation, Problem Details binding normalization, and revision/abort guards. The user independently approved C1 with: "Approve C1, commit to the local branch, then continue to submission/retries". No design deviation or repair was requested. Section 2 links the established contracts; downstream packets consume them, while persistence remains provisional until its own evidence/gate. The user authorized one local menu/quote commit; no remote writes.

**Repair rounds used: 0 of 2.**

### Slice 3: Durable submission, status, and timeout recovery

#### Task 3.1: Prove durable submission and status through HTTP

**Prerequisites:** C1

**Conflicts with:** None

**Context:** Required: C1's settled quote/error boundary, LLD 3.2 receipt/OrderView types, 4.1 POST/GET orders, server replay behavior in 4.3, 5, storage errors in 6, and submission cases in 8.2/8.4; existing `OrderService.cs`, schema, connection factory, and HTTP fixture. Browser recovery and mutation SQL are outside this task.

**Contracts:** Implement and prove receipt/OrderView/status, retry-header, and server request-comparison contracts before 3.2 consumes them. Use the canonical DTO/HTTP-test policy in section 2; preserve C1's quote contract. C2 independently reviews the combined submission slice.

**Expected files:** Embedded `schema.sql`, `OrderService.cs`, grouped models, API composition/error mapping; HTTP fixture and submission integration tests. No WEB changes.

**Objective:** HTTP callers can submit, look up, and replay an accepted order across lost acknowledgements, concurrent requests, and API restart without duplication. Leave a buildable, tested service boundary ready for the existing WEB form.

**Instructions:**
1. Create the single Orders table with all six LLD columns/constraints using idempotent SQL. Parameterize values, open/dispose per-operation connections, and persist original request/receipt separately from the current accepted snapshot.
2. Add POST/GET routes. Replay lookup precedes pricing; single-statement insertion and unique key enforcement select the concurrent winner. Matching replay returns the same original 201 receipt/Location; same-key/different-typed-input is 409; a deliberate new key creates another order.
3. Complete storage error mapping: known busy exhaustion is visible 503 with warning context; unexpected errors are safe 500 with error logging. Do not turn arbitrary exceptions into business rejection or add application retry loops.

**Acceptance criteria:**
- Quote -> submit -> GET preserves the full accepted breakdown/input/description; quoting alone creates no order. Invalid submission leaves no row; bad key/binding inputs are 400 and missing lookup is 404.
- Discard an accepted response, recreate the host against the same DB, and retry: identical receipt/ID and exactly one row. Concurrent identical attempts also create one row; changed-input reuse conflicts without mutation; identical contents with a new key create a different ID.
- Server comparison uses typed serialization preserving line/topping order and tip text, not semantic normalization. GET uses the stored snapshot, not current-menu repricing.
- Repeatable fixture-controlled busy/unexpected DB failures exercise 503/safe 500 and appropriate logs without production delay/failure hooks; an uncertain outcome is not presented as certain rejection.

**Verification:**
- Establish `Submission` fixture names; run `dotnet test PDD.slnx --filter "FullyQualifiedName~Submission|FullyQualifiedName~Quote"`. Use independent file-backed connections, host recreation, coordinated concurrent requests, and targeted row reads.
- Point 3.2 to the actual passing POST/GET serialization and rejection cases; do not hand off guessed payloads or add frontend placeholders.

**Requirements covered:** Service portions of FR-6/FR-7 and submission FR-10; charge parity in FR-2; QR-2.

**Execution evidence:** Task 3.1 complete. Its first submit/lookup test failed with 404; current Submission/Quote selection passes **73 tests**. The real HTTP/file-backed SQLite cases prove quote/submit/GET equality and no quote writes, lost acknowledgement plus host restart (even with the original size removed from the replacement catalog), identical/conflicting coordinated submissions, raw tip-text/line/topping-order comparison, unchanged accepted snapshots on conflict, and distinct IDs for new keys. Fixture-controlled write contention produces 503 plus a contextual warning; explicit retry succeeds after lock release. Dropped-table and corrupt-stored-JSON cases produce safe 500 plus contextual error logs. Validation/missing lookup remain 400/404 without invalid rows or error-level validation traces. Each fixture clears only its connection pool and exact DB/WAL/SHM files; all work uses isolated ignored test DBs. POST/GET serialization is now a passing consumer boundary for 3.2, not a guessed payload.

#### Task 3.2: Integrate browser submission and bounded recovery

**Prerequisites:** 3.1

**Conflicts with:** None

**Context:** Required: 3.1's proven DTOs/HTTP tests; C1's WEB quote/error boundary; LLD 4.3, browser behavior in 6, helper cases in 8.1, and manual submission checks in 8.3; existing WEB form, helper, and TS types. SQL/storage internals are background, not required implementation context.

**Contracts:** Consume the proven POST/GET wire contracts and add the immutable WEB SubmissionAttempt from LLD 3.2. C2 remains the single independent integration checkpoint after 3.1 and 3.2.

**Expected files:** WEB `client\api.ts`, `client\types.ts`, `client\App.tsx`, and existing helper tests.

**Objective:** The complete draft builder submits real orders, displays their ID/status, and recovers from an unknown submission outcome without creating another order.

**Instructions:**
1. Capture a GUID and serialized body once per deliberate submission. Gate Submit on a valid current quote. Lock edits/new submissions while the attempt is unresolved; timeout/network/5xx retains the exact attempt and offers explicit Retry Submission. A definite validation rejection permits correction.
2. After receiving a receipt, retain it and GET current order state before enabling applicable actions. If that GET fails, show that the order was accepted but status is unavailable; retain the receipt/ID and offer Check Status, not submission replay or a fresh attempt. Keep editing locked until current status is recovered.
3. Keep accepted state separate from the unsaved draft, providing the foundation for later cancellation/update recovery. Display the current ID/status and support explicit Check Status; do not add polling.
4. A deliberate new submission uses a fresh key even for identical contents. No automatic write retries, browser storage, or additional recovery subsystem.

**Acceptance criteria:**
- Submit/check-status works through the real Vite proxy, using 3.1's proven payloads. The accepted quote is shown without browser repricing.
- Helper tests prove two sends preserve identical body/key, a new attempt changes key, raw tip text is unchanged, and transport/HTTP errors remain distinguishable.
- Losing the submission acknowledgement preserves the attempt and locks edits; retry recovers the same ID before current-state lookup restores applicable actions.
- Receiving a receipt followed by a failed status lookup retains known acceptance and the ID. Recovery retries GET only, never creates a new submission attempt, and does not unlock editing prematurely.
- Validation, transport, and lookup failures remain visible; no failed request becomes a fake empty/success response.

**Verification:**
- Run `npm test -- api.test.ts`, WEB build/lint, and manually submit/check status through Vite. Reuse 3.1's unchanged HTTP evidence.
- Manually lose only a submission acknowledgement (for example, browser interception that lets the request finish but withholds the response), then Retry Submission. Verify edit locking, same ID, and current-state recovery; no permanent demo hook.
- Separately let the receipt arrive but fail its follow-up GET; verify the retained ID, accepted/status-unavailable message, locked editing, and Check Status recovery.

**Requirements covered:** Interactive FR-6/FR-7 and submission FR-10; FR-2/FR-4 feedback; IR-1.

**Execution evidence:** Task 3.2 complete. **Ten** WEB helper tests and test type-check/build/lint pass, including frozen attempts, unchanged raw tip/body/key across two sends, fresh keys for deliberate new attempts, and GET-only lookup. Current combined API Menu/Pricing/Quote/Submission selection passes **85 tests**; API build has zero warnings/errors.

Browser checks used separate API/Vite processes and the actual proxy with one isolated ignored DB. Browser-only interception let the API accept a submission, then withheld its acknowledgement: UI displayed unknown outcome, locked the draft, removed new submission, and offered Retry Submission. Retry sent identical key/body, recovered the same ID and receipt, then GET restored Not Started editing. Explicit Check Status worked. A deliberate new order with identical contents used a different key/ID; its receipt arrived but its follow-up GET was withheld. UI retained that ID/receipt, showed accepted/status-unavailable, locked editing/new attempts, and offered Check Status rather than submission replay. Recovery added only one GET, with POST count unchanged. Direct read-only verification found exactly two deliberate orders, not an extra retry row. Editing the recovered draft changed its quote to $7.33 while the accepted snapshot stayed $5.33. All browser interception hooks were removed.

#### Checkpoint C2: Stabilize persistence and retry semantics

**Prerequisites:** 3.2

**Conflicts with:** None

**Required evidence:** Current 3.1 submission/quote HTTP results and 3.2 helper/manual recovery evidence, API build, and WEB build/lint. Inspect unique insertion, immutable retry fields, parameterization, safe error/logging behavior, and the distinction between unknown submission and accepted/status-unavailable recovery.

**Review:** Independent user review of the submission transaction boundary, request matching, restart/concurrent replay proof, immutable receipt versus current state, and UI uncertainty handling before adding mutations.

**Completion condition:** Matching retries provably produce one stored order and the original receipt; both browser recovery branches are demonstrated. Link canonical receipt/current-state DTOs and HTTP tests from section 2, check TS parity, and update provisional 4.1/4.2. Replay after cancellation is still owed by 4.1; replay after replacement is owed by 4.2.

**On failure:** Apply section 3's repair limit and escalation policy.

**Approval:** User confirms stable retry/persistence contracts; any design deviation requires explicit approval.

**Local commit boundary:** Durable submission/status/recovery, only if the user requests a commit.

**Execution evidence:** C2 API/helper/build/lint and browser evidence above are current. Self-inspection checked the single-statement insert's submission-key conflict target, immutable original request/receipt, parameterized values, independent connection ownership, safe storage-error logging, C#/TS parity, and distinct browser recovery branches. The user independently approved C2 with "Approve C2, commit locally, and continue"; no design deviation or repair was requested. Section 2 settles the contracts for 4.1/4.2. Replay after cancellation/replacement is still owed by those tasks. The user authorized the local submission/recovery commit. No remote writes.

Temporary API/Vite processes were stopped; the owned smoke DB/WAL/SHM files were deleted after read-only row verification. The user's runtime `data\pdd.db` and its sidecars were untouched.

**Repair rounds used: 0 of 2.**

### Slice 4: Editable orders and defensible kitchen transitions

#### Task 4.1: Integrate cancellation and manual advancement atomically

**Prerequisites:** C2

**Conflicts with:** None

**Context:** Required: C2 contracts; cancellation/advancement endpoints in LLD 4.1, cancellation race in 4.4, relevant conditional queries in 5.2, 6, and lifecycle/cancel-start cases in 8.2; existing OrderService, 3.2's accepted-state WEB boundary, and submission HTTP fixture. Replacement updates/pricing changes are outside this task.

**Contracts:** Extend the proven order service with cancel and expected-status advance. Establish the conditional-write/changed-row/error pattern for reuse by 4.2. Preserve original receipt/request fields and approved wire status strings.

**Expected files:** `OrderService.cs`, API routes/models as needed; WEB cancellation/status controls in `App.tsx`/helper/types; lifecycle HTTP tests and replay-after-cancellation cases.

**Objective:** Customers can safely cancel before kitchen start, and a manual restaurant caller can advance legal stages without races bypassing the rules.

**Instructions:**
1. Use atomic conditional statements returning the changed row. Cancel only WHERE NotStarted. Derive the next restaurant stage server-side; expectedStatus prevents a repeated request from advancing twice.
2. Distinguish missing/forbidden/already-cancelled results. Cancellation is terminal and repeat-safe; Delivered is terminal. Do not authorize a mutation through an earlier read or in-process lock.
3. Add customer cancellation using the existing accepted-state boundary. On stale-state conflict or uncertain cancellation, refresh accepted state without silently discarding the draft. Display current statuses with human-readable labels; refresh failures remain visible.
4. Add no kitchen UI/timer. Use the endpoint in manual checks and later README examples.

**Acceptance criteria:**
- Cancellation after start rejects with 409; forward stages cannot skip or reopen terminal states, and repeated old advancement cannot advance again.
- Repeat cancellation returns Cancelled; replaying the original submission after cancellation returns its original receipt without overwriting current status or adding rows.
- Cancel/start tests force both sequential orderings and coordinated concurrent release: exactly one success, the other 409, stored state matches the winner.
- WEB cancellation/status recovery preserves the unsaved draft and reports the current accepted state honestly; no assumption that a timeout means failure.

**Verification:**
- Establish `Lifecycle` fixture names; run `dotnet test PDD.slnx --filter "FullyQualifiedName~Lifecycle|FullyQualifiedName~Submission"`.
- Inspect unique/conditional SQL alongside race evidence; no arbitrary sleeps, production synchronization hooks, or required concurrent winner.
- Run `npm test`, WEB build/lint, and manually exercise cancel, all restaurant stages, stale-status rejection, and cancellation recovery through the real proxy.

**Requirements covered:** FR-7/FR-9; cancellation portions of FR-8/FR-10; customer feedback in FR-4; QR-2.

#### Task 4.2: Integrate replacement updates using the proven lifecycle pattern

**Prerequisites:** 4.1

**Conflicts with:** None

**Context:** Required: 4.1's conditional-write/changed-row/error implementation and HTTP fixture; C1's pricing/quote contract; 3.2's accepted-state/draft boundary; LLD PUT behavior in 4.1-4.2, update race in 4.4, update query in 5.2, and update/replay cases in 8.2. No redesign of submission or lifecycle progression.

**Contracts:** Reuse the proven OrderView, quote, and mutation error shapes. Extend OrderService with complete replacement while NotStarted; preserve immutable original request/receipt.

**Expected files:** `OrderService.cs`, PUT route; WEB Save Changes wiring in `App.tsx`/helper/types; existing lifecycle/quote HTTP tests with update and replay cases.

**Objective:** Customers can save a newly quoted replacement before kitchen start without stale writes bypassing the lifecycle or losing their unsaved draft.

**Instructions:**
1. Validate/price through the existing Quote path, then atomically replace the snapshot only WHERE NotStarted, returning the changed row. Reuse 4.1's missing/conflict mapping; do not add a version/merge framework.
2. Pre-submit Save Changes remains draft-only; after submission it PUTs only with a valid current quote and applicable known status. The service remains authoritative if kitchen start happened meanwhile.
3. On validation failure, stale-state conflict, or uncertain update, preserve the unsaved draft. Recheck accepted state where required without silently discarding edits or automatically retrying the write.

**Acceptance criteria:**
- Quote -> accepted update -> GET matches the new input, breakdown, and description. Invalid update leaves accepted contents/status unchanged; update after start rejects with 409.
- Update/start tests force both orderings and coordinated overlap: start-first rejects update unchanged; update-first may legitimately be followed by start retaining new contents. No update commits after start.
- Replaying the original submission after update returns its original receipt, creates no extra row, and leaves GET showing the later accepted contents/status.
- WEB supports NotStarted editing after submission recovery; current-quote gating and visible recheck/error handling preserve the unsaved draft. Quote revision protection continues to apply.

**Verification:**
- Extend the existing `Lifecycle` fixtures; run `dotnet test PDD.slnx --filter "FullyQualifiedName~Lifecycle|FullyQualifiedName~Submission|FullyQualifiedName~Quote"`.
- Inspect the update predicate/returned row and immutable retry fields alongside race/replay evidence.
- Run `npm test`, WEB build/lint, and manually exercise accepted updates, invalid updates, stale-status rejection, and uncertain-update recovery through the real proxy. Reuse unchanged cancellation/advancement evidence from 4.1.

**Requirements covered:** Post-submit FR-1/FR-2/FR-4; replacement-update portions of FR-8/FR-10; QR-2.

### Slice 5: Local release hardening and assessment handoff

#### Task 5.1: Finish the required write-up and local delivery evidence

**Prerequisites:** 4.2

**Conflicts with:** None

**Context:** Required: actual implemented API/commands and current verification evidence; requirements 6/8; LLD 7.1 and 8.3-8.4; README, `global.json`, package scripts, and CI. Consult the original local DOCX for the four write-up questions; never stage it.

**Contracts:** Document implemented contracts and confirmed assumptions, not planned behavior. Use the shared policy for any discovered repair.

**Expected files:** Primarily `README.md`; only directly related code/test/config fixes justified by discovered delivery defects. Do not rewrite the design documents to disguise an implementation gap.

**Objective:** A grader can start both processes, run checks, exercise the API, and understand guarantees/cuts without reading the planning documents.

**Instructions:**
1. Give prerequisites/working directories and one or two commands each for API start, WEB setup/start, and each test suite using existing scripts. Explain automatic SQLite/menu startup, ports/proxy, and the manual restaurant simulation.
2. Include example requests for every public operation, including the retry header, expected-status advancement, and representative validation/conflict responses.
3. Record all requirements section 8 assumptions and LLD 1.1 limits: tip/tax bases, rounding, exact promos/single promo, draft-only saves, retry identity, cancellation, numeric capacity, duplicate toppings, browser lifetime, fixed definitions, and last-write-wins NotStarted edits.
4. Include an approximately one-page design write-up answering all four brief questions, plus the few-sentence AI note with a real rejected/corrected output and a genuinely verified invariant. Do not invent user verification or AI evidence; obtain the user's example if required.
5. State automated invariants, why chosen, manual evidence, omissions/risks, deliberate cuts, and next work. Keep short write-up/AI sections in README rather than adding unnecessary deliverable files.

**Acceptance criteria:**
- All D-2 through D-5 content is present and agrees with shipped behavior; existing hosted-repository delivery (D-1) is identified without attempting publication.
- A new local start following README works with both separate processes and the actual proxy. A customer can complete the builder/submit/status/edit-or-cancel journey and a manual caller can advance another order through delivery.
- Each LLD 8.1/8.2 invariant row has actual automated evidence from its owning task; LLD 8.3 WEB/proxy checks have honestly recorded manual evidence. No missing in-scope behavior is called complete merely because it appears in the cut list.
- AI/user-verification claims have a real source; missing input blocks the relevant documentation acceptance, not unrelated code work.

**Verification:**
- Follow the README commands and examples locally; use existing isolated test DB configuration for destructive/restart checks, never delete the user's runtime DB.
- Review requirement ownership against task criteria and gather current results for C3; do not duplicate the same tests per deliverable or expand into a production-readiness program.

**Requirements covered:** D-1 through D-5, QR-3/QR-4, IR-1; documentation of lifecycle, pricing, and extensibility guarantees.

#### Checkpoint C3: Verify the complete local handoff

**Prerequisites:** 5.1

**Conflicts with:** None

**Required evidence:** Current complete applicable local gates:

```powershell
# Repository root
dotnet build PDD.slnx --configuration Release
dotnet test PDD.slnx --configuration Release --no-build

# From src\pdd-web
npm run build
npm run lint
npm test
```

Also require the real two-process journey, stale-quote/timeout recovery checks, HTTP examples, and documented assessment deliverables from 5.1. Stop temporary check processes and clean only their explicitly owned test files. CI configuration inspection ensures the existing Ubuntu build/test path stays compatible; do not claim a remote run occurred.

**Review:** Independent user review of cross-slice interactions: quote/submit/update parity; preserved receipt after later mutations; uncertainty and unsaved-draft handling; conditional-write/unique-key evidence; README accuracy and scope discipline. Reuse fresh test evidence.

**Completion condition:** All tracker prerequisites and this checkpoint's own evidence/review pass; no blocking correctness/requirement/regression finding remains. Document any approved residual limitation without implying comprehensive or production assurance.

**On failure:** Apply section 3's repair limit and escalation policy. A scope cut requires user approval, not just a README edit.

**Approval:** User accepts the local assessment handoff; this does not authorize commits, publication, or deployment.

**Local commit boundary:** Lifecycle and final handoff may be separate coherent increments, only when requested by the user; inspect each authorized staged increment against current evidence.

## 5. Single Status Tracker

**Status values:** `[ ]` Not started | `[~]` In progress | `[x]` Complete

| Task | Description | Prerequisites | Conflicts | Status |
|------|-------------|---------------|-----------|--------|
| 1.1 | Establish the minimal menu/network spine | None | None | [x] |
| 1.2 | Prove exact live quotes through the complete multi-line draft builder | 1.1 | None | [x] |
| C1 | Stabilize quote contracts; independently user-approved | 1.2 | None | [x] |
| 3.1 | Prove durable submission/status HTTP contracts using C1 contracts | C1 | None | [x] |
| 3.2 | Integrate browser submission/recovery against 3.1's proven contracts | 3.1 | None | [x] |
| C2 | Stabilize persistence/retries; independently user-approved | 3.2 | None | [x] |
| 4.1 | Integrate cancellation/advancement using C2 contracts | C2 | None | [ ] |
| 4.2 | Integrate replacement updates using C1 pricing and 4.1's mutation pattern | 4.1 | None | [ ] |
| 5.1 | Finish README, write-up, AI note, and delivery evidence | 4.2 | None | [ ] |
| C3 | Verify and independently review the complete local handoff | 5.1 | None | [ ] |

An entry is ready only when its prerequisites are complete, no conflict is active, and no unresolved decision blocks it. Checkpoints are not complete just because their prerequisite tasks finish. If a new decision appears, record its owner/affected IDs in Overview and annotate only those tracker descriptions; do not mark blocked/unverified work complete.

## 6. Deferred Assurance and Release Criteria

**Deferred coverage:** As approved in LLD 8.4, no automated component rendering, browser-reload recovery, production networking/load, schema upgrades, exhaustive transport permutations, performance suite, or coverage quota. Manual browser checks leave presentation/interaction regressions less protected than an automated UI suite; HTTP concurrency tests plus SQL inspection explain mechanisms, not exhaustive scheduling proof. Do not defer any explicit LLD 8.1/8.2 invariant row by treating the test strategy as optional.

**Release criteria:** C3 is the authoritative local release gate, including the full applicable commands, manual integration evidence, and assessment documentation. Update its evidence and the single tracker during execution rather than maintaining a second delivery checklist.

**Execution handoff:** Report what actually works, any remaining blockers/approved cuts, current evidence, any explicitly authorized local commits, and uncommitted changes with their reasons. Under the user's standing rule, uncommitted completed work is expected until a commit is requested. Remote publication remains the user's responsibility.

**Outside this delivery:** Only the exclusions and deferred assurance above; any additional removal of approved functionality needs a user decision. Design/contract questions or repeated failed repair rounds pause only affected work.

This plan aims to reduce implementation overhead through bounded slices, shared contracts, and reused verification. It does not guarantee AIC savings or establish production readiness.
