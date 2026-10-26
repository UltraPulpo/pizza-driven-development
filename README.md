# pizza-driven-development

A local pizza builder (React/TypeScript/Vite) and a separate order service (ASP.NET Core/.NET 10 and SQLite) for the Senior Software Developer take-home exercise. The API owns validation, pricing, descriptions, and order state. The browser only formats returned cents.

The source code is on GitHub at [UltraPulpo/pizza-driven-development](https://github.com/UltraPulpo/pizza-driven-development); clone it to run locally.

## Run locally

Prerequisites:
- a stable .NET 10 SDK (see `global.json`), Node.js 24, and npm.
- Examples use PowerShell from the repository root. On Linux/macOS, use `/` instead of `\` in paths.
- No global tools, hosted database, authentication, or payment setup are needed.

Start the API in one terminal:

```powershell
dotnet run --project src\pdd-api --launch-profile http
```

Start WEB in a second terminal:

```powershell
npm --prefix src\pdd-web ci
npm --prefix src\pdd-web run dev
```

Open <http://localhost:5173>. API listens on <http://localhost:5105>.
Vite forwards `/api` requests to the separate API process. Set `PDD_API_URL` before starting WEB
only if the API uses a different address. Both ports must be free.

API startup loads `src\pdd-api\menu.json` and runs the embedded idempotent schema.
SQLite uses WAL and creates `data\pdd.db` automatically; accepted orders survive API restarts.
The database path resolves against the API content root, not the shell's working directory.
No seed command is required. Stop either process with Ctrl+C.

Build the draft with multiple pizza lines; the quote updates on every change. Submit Order creates an order.
After submission, Update Order replaces the accepted order only while Not Started.
Check Status refreshes explicitly; there is no polling. Start New Order resets the form to a fresh default draft
(one Small pizza, no toppings, $0 tip, no promo) and deliberately begins another submission, even if you re-enter identical contents.

## Tests and local checks

From the repository root:

```powershell
dotnet test PDD.slnx
npm --prefix src\pdd-web test
```

WEB tests type-check the test sources before running Vitest. API HTTP tests use unique,
ignored file-backed SQLite databases and clean only their own DB/WAL/SHM files, not the runtime database.

The complete local release checks are:

```powershell
dotnet build PDD.slnx --configuration Release
dotnet test PDD.slnx --configuration Release --no-build
npm --prefix src\pdd-web run build
npm --prefix src\pdd-web run lint
npm --prefix src\pdd-web test
```

Existing CI builds/tests API and WEB on Ubuntu with .NET from `global.json` and Node 24.
Local checks are not a claim that a new remote CI run occurred.

### Invariants tested, and why

| Area | Automated evidence and reason |
|------|-------------------------------|
| Exact prices | Every size and topping category, quantities, multiple lines, no toppings/zero tip, descriptions, and component sums: the restaurant's actual charge, not just the grand total. |
| Rounding | Small pizza tax is 33 cents; dollar tip `"1.005"` is 101 cents; half-cent toppings round per pizza before quantity; the reference order below totals exactly 3,123 cents. |
| Validation and promos | Malformed/missing input, duplicate toppings, numeric overflow, exact codes, and SAVE5 at 2,000/2,001 cents. Percent tips above 100 are allowed. Invalid input never creates or replaces an order. |
| Extensibility | New menu data prices without algorithm edits; a test-only Large-required promo distinguishes equal subtotals. Startup rejects invalid menu data and duplicate promo codes. |
| Charge parity | Quote, submit, update, and GET keep the same input, breakdown, and description. Quoting writes no order. |
| Retry identity | Lost acknowledgement plus restart, concurrent matching/conflicting submissions, and new keys. Original receipts replay after cancel/update without changing state or adding rows. |
| Lifecycle atomicity | Adjacent stages only, terminal states, stale expected status, repeated cancel, and both forced race orderings plus overlap. Cancel/start has one winner; an update cannot commit after start. |
| Failure visibility | SQLite write contention gives 503 plus a warning; missing tables or corrupt JSON give a safe 500 plus an error log. Client tests cover raw tip text, immutable attempts, error classification, timeouts, and no automatic writes. |

Agent-run browser smoke checks through the real Vite proxy covered line editing, tips, promos, pending/failed quotes, lost submit/update/cancel acknowledgements, and stale status. They predate the Update Order button and locked-form reset, and are neither automated UI coverage nor the candidate's own verification.

Not covered: component rendering, browser reload recovery, every transport/JSON permutation, production load, schema upgrades, or every concurrent schedule. There is no coverage target. Race tests force both orderings; the SQL constraints, not repeated random runs, provide the guarantee.

## API

All added endpoints are under `/api`. Money fields are integer cents in camelCase JSON.
Tip `value` is decimal **text**, not a JSON number. Status wire values are `NotStarted`,
`Started`, `ReadyForPickup`, `Delivered`, and `Cancelled`.

| Method and route | Request / result |
|------------------|------------------|
| `GET /api/health` | Startup/SQLite health. |
| `GET /api/menu` | Sizes, topping categories, and toppings with stable IDs. |
| `POST /api/quote` | Complete order input; returns priced lines, subtotal, discount, taxable subtotal, tax, tip, total, description. No persistence. |
| `POST /api/orders` | Complete order input and one GUID `Idempotency-Key` header. Returns 201 `{ orderId, quote }` and `Location`, including matching retries. |
| `GET /api/orders/{id}` | `{ orderId, status, order, quote }` from the current stored snapshot. |
| `PUT /api/orders/{id}` | Complete replacement input; returns the updated view only while NotStarted. |
| `POST /api/orders/{id}/cancel` | No body. Returns Cancelled; repeat cancellation is safe. Other progressed states return 409. |
| `POST /api/orders/{id}/advance` | `{ "expectedStatus": "NotStarted" }`, for example. Derives the next stage server-side; repeated/stale expectations return 409. |

### Walkthrough: UI plus terminal

With both processes running (see [Run locally](#run-locally)), open <http://localhost:5173>.
The restaurant side has no UI or timer. A second terminal plays the kitchen with
`scripts\advance-order.ps1`, which reads the order's current status and advances it exactly one stage.
The UI does not poll, so it only learns about kitchen progress when you click **Check Status**
(or when an Update Order/Cancel Order request is refused).

1. **Price an order.** Set Pizza 1 to Large, quantity 2, with Pepperoni and Bacon. Choose a Percent tip of 15
   and promo code `ILOVEPIZZA`. The quote updates on every change: subtotal $28.56, discount $2.86,
   discounted subtotal $25.70, tax $1.67, tip $3.86, **total $31.23**. Each Large pizza costs $12.00 + $0.96 + $1.32.
   Click **Show Description** to see the written order.
2. **Submit.** Click **Submit Order**. The UI shows the Order ID and status Not Started. Copy the ID.
3. **Update before start.** Change the quantity to 1. The Current quote updates immediately, but the Accepted
   total (what the restaurant will charge) does not change until you click **Update Order**.
4. **Kitchen starts; customer is now too late.** Run:

   ```powershell
   .\scripts\advance-order.ps1 <orderId>    # NotStarted -> Started
   ```

   Do **not** click Check Status or reload the page yet. The UI still says Not Started, which simulates a customer
   holding a stale view. Click **Cancel Order** (or edit and click **Update Order**). The API refuses
   (409, "Cancellation is allowed only while the order is Not Started."), the UI refreshes to Started,
   and editing locks. The form resets to the accepted order, discarding your unsaved edit, so it shows
   what the restaurant is making.

   Had you clicked **Check Status** first, the UI would show Started and lock up front the same way: the form
   shows the accepted order, and **Update Order**, **Cancel Order**, and the draft fields are greyed out and
   ignore clicks. That is the UI's guard; the stale click above shows that the API enforces the rule on its own.
5. **Finish the order.** Run the script twice more (`Started -> ReadyForPickup`, `ReadyForPickup -> Delivered`)
   and click **Check Status** after each run; the order stays locked. A further run is refused because Delivered is terminal.
6. **Cancel in time.** Click **Start New Order** (the form resets to a fresh default draft), submit, then click
   **Cancel Order**. The status becomes Cancelled, and the script refuses to advance it.
7. **Unknown submission outcome.** Click **Start New Order** and wait for the Current quote to appear. Stop the API
   (Ctrl+C) and click **Submit Order** without editing the draft (the quote can't refresh while the API is down).
   The UI reports an unknown outcome and locks editing. Restart the API and click **Retry Submission**.
   The retry reuses the captured idempotency key and body, so it yields exactly one order.

The script takes `-ApiUrl` if the API is not on `http://localhost:5105`. If Windows blocks local scripts, run
`powershell -ExecutionPolicy Bypass -File scripts\advance-order.ps1 <orderId>`.

### Lost acknowledgement retry (terminal)

Step 7 covers a request that never reached the API. The harder case is when the order was accepted but the
response was lost. A browser can't easily simulate that, so send the identical request twice:

```powershell
$api = 'http://localhost:5105/api'
$body = '{"items":[{"sizeId":"large","quantity":2,"toppingIds":["pepperoni","bacon"]}],"tip":{"mode":"percent","value":"15"},"promoCode":"ILOVEPIZZA"}'
$headers = @{ 'Idempotency-Key' = [guid]::NewGuid().ToString() }
$first = Invoke-RestMethod "$api/orders" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
$retry = Invoke-RestMethod "$api/orders" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
$retry.orderId -eq $first.orderId    # True: same original receipt, no second order.
```

Reusing the key with a different body returns 409. The receipt is the original accepted quote and still
replays after an update or cancellation. It never restores old contents or reopens the order. Use
`GET /api/orders/{id}` for current state.

### Rejections and uncertain outcomes

Errors use Problem Details with an actionable `detail`: 400 for invalid input, 404 for missing
orders, 409 for forbidden/stale transitions or changed-input key reuse, 503 for SQLite busy
exhaustion, and a safe generic 500 for unexpected failures. Expected validation is not logged
as an error stack trace; busy errors are warnings and unexpected failures are logged with operation context.

For example, this request gets **400**, since exactly $20.00 does not qualify for SAVE5:

```powershell
$invalid = '{"items":[{"sizeId":"small","quantity":4,"toppingIds":[]}],"tip":{"mode":"dollars","value":"0"},"promoCode":"SAVE5"}'
Invoke-RestMethod "$api/quote" -Method Post -ContentType 'application/json' -Body $invalid
```

Representative Problem Details fields:

```json
{"status":400,"detail":"SAVE5 requires a food subtotal over $20.00 before tax and tip."}
```

Trying to cancel an order after kitchen start gets **409**:

```json
{"status":409,"detail":"Cancellation is allowed only while the order is Not Started."}
```

A timeout/network failure/5xx is not proof that a write failed. WEB uses a ten-second request timeout.
Unknown submission outcomes lock edits/new submissions and offer Retry Submission with the captured body/key.
Updates and cancellations are never retried automatically.

## Assumptions

| Area | Decision |
|------|----------|
| Definitions | Menu/prices/promos are fixed during ordering. Menu loads once at startup; no hot reload/admin UI. Every topping category is priced as a percentage of the pizza's base price; a flat-price category would need a pricing change. |
| Money | Nonnegative 32-bit integer cents. Decimal intermediates, checked arithmetic, and clear rejection of unrepresentable values/overflow; no binary floating-point pricing. |
| Rounding | Nearest cent, midpoint away from zero. Round each topping per pizza before quantity, and round discount, tax, dollar tip, and percentage tip before adding the components. |
| Tax | 6.5% of the discounted food subtotal. Voluntary tip is not taxed. |
| Tip | `dollars` or `percent`, with invariant-culture decimal text. Percent uses discounted food subtotal before tax. Nonnegative values including zero and percentages above 100 are supported within numeric capacity; no exponent/thousands notation or silently rounded excess decimal precision. |
| Promo bases | ILOVEPIZZA discounts the undiscounted food subtotal by 10%; SAVE5 discounts 500 cents only when that subtotal exceeds 2000 cents. Tax and tip do not establish eligibility. |
| Promo matching/conflicts | Exact case/whitespace match, one optional code. All promos conflict structurally; unknown/ineligible/empty code strings are errors. Missing/null means no promo; clearing WEB's field sends null. |
| Lines/toppings | Positive 32-bit integer quantity; known IDs; each topping at most once per line. Empty topping list is valid and described as no toppings. Line/topping order is retained. |
| Draft versus accepted | The browser draft is quoted live but never persisted. Submit Order creates the order; after that, Update Order (PUT) replaces the entire accepted input/quote while Not Started. Refreshes and errors keep unsaved edits while the order can still change; once it is confirmed locked (Started or later, or Cancelled), the form resets to the accepted order. Start New Order begins from a fresh default draft: one Small pizza, quantity 1, no toppings, $0 tip, and no promo. |
| Retry identity | A deliberate submit captures a GUID and serialized body once. Server compares serialization of the typed input, preserving line/topping order and raw tip text; `"1.0"` and `"1.00"` are distinct. JSON formatting/property order alone is not identity. New keys create new orders even for identical contents. |
| Persistence/replay | One SQLite row stores immutable original request/receipt separately from the current snapshot. Matching replay returns the original 201 receipt even after update/cancel; GET never reprices an accepted snapshot. Keys have no expiry policy here. |
| Browser lifetime | Drafts and pending attempts are page-memory only. No recovery guarantee after reload/closing the browser. API restarts retain accepted orders/retry data. |
| Lifecycle and races | Advancement is linear with no skipping: Not Started -> Started -> Ready for Pickup -> Delivered. Cancelled (only from Not Started) is an added fifth status. Delivered and Cancelled are terminal; a Cancelled order stays queryable and repeating cancellation returns it unchanged. Atomic predicates decide permission, not a prior read: cancel/start has one winner, and an update may commit before a later start but never after it. |
| Concurrent editors | Last accepted database write wins while Not Started. No merge/version protocol. |
| Scope | One restaurant, anonymous customers, manual advancement. No auth, payments, production deployment, real kitchen/delivery, menu administration, multiple locations, browser storage, polling, or schema migration framework. |

## Design write-up

### Money, total calculation, and pricing ownership

**Money.** All amounts are integer cents (`int`). Topping charges, tax (6.5% of the discounted food subtotal) and percent tips are each rounded half-up to a whole cent where they're created. After that, the total is integer addition: lines → subtotal → discount → + tax + tip. Overflow is caught and reported as a validation error.

**Ownership.** All pricing lives in the server's `PricingService`, which reads its prices from `menu.json`. The builder does no arithmetic. On each edit it shows the result of `POST /api/quote`. Submit and update re-price the order with the same method and store that quote.

**Why.** One implementation means the total shown is the total charged, and the server never trusts a total from the client. The cost is a network round trip on each edit.

### Adding menu items and conditional/conflicting promos

**Sizes and topping categories are data** in `menu.json`, which is checked when the API starts. To add one, add an entry and restart the API. The builder loads its options from `GET /api/menu`, and `PricingService` reads every price from that catalogue, so no code changes.

**Each promo is an `IPromoRule`**: a `Code` plus `GetDiscount(order, subtotal)`. A rule can check its own conditions against the whole order and reject an order with a clear message, as SAVE5 does. Adding one means one class and one line to register it. A test proves this with a made-up rule.

**Conflicts can't happen:** an order holds at most one promo code.

**Limits:** stacking promos, or a topping category with a flat price, would need changes to core pricing.

### Lifecycle, retry after timeout, and cancel versus start

**Lifecycle.** Not Started → Started → Ready for Pickup → Delivered, with no skipped stages. Cancelled is reachable only from Not Started. Every change is one conditional SQL update: `… WHERE id = $id AND status = $expected`. The kitchen is simulated by `POST /api/orders/{id}/advance`, called from a script.

**Retry after a timeout.** Each submission carries a GUID `Idempotency-Key` that is unique in the database. If the outcome is unknown, Retry resends the same key and body and gets the original receipt back (same order ID and total). The same key with a different body gets 409.

**Cancel versus kitchen start.** Both require `status = 'NotStarted'`, and SQLite applies writes one at a time, so exactly one wins. The loser gets 409.

**Guarantees:** one order per key, safe retries, and changes only while Not Started.

### Deliberate cuts/fakes and next work

**Cuts and fakes:**
- **Submit failure handling:** the UI treats every failure other than a 400 as an unknown outcome and offers Retry, even for definite rejections such as 404 or 409.
- **UI polish:** the order builder and status view are plain, unstyled HTML.
- **Kitchen:** it's simulated by a manually run script calling the advance endpoint, not by a background worker or timer.
- **Storage:** the menu is a JSON file loaded at startup and orders are in one local SQLite file. There's no menu admin, migrations or production database.

**Next:**
- **Submit failure handling:** treat 404 and 409 as definite rejections and keep Retry only for outcomes that are really unknown.
- **Order access control:** add customer identity and ownership checks so only the person who placed an order can view, update or cancel it.
- **Kitchen worker:** replace the script with a background service that moves orders through timed stages, using the existing conditional advance.
- **UI tests from start to finish:** automate the submit, retry, lock and reset walkthrough that is currently tested only by hand.

## AI-usage note

**Tools and uses**
- GitHub Copilot CLI in agent mode, mainly with GPT-6.1 Sol and Claude Opus 5.5.
- Custom `SKILL.md` prompts to build the requirements, high-level design, low-level design, and plan docs step by step (in `design\`).
- Implementation in small steps: I reviewed and approved every change before committing. I pushed and opened and merged the PRs myself.
- Adversarial review agents to check plans and the README Assumptions against the code.

**Rejected or corrected output**
- I repeatedly rejected scope creep to keep the work in proportion to the timebox.
- In manual testing I found that the generated "Save Changes" button was misleading: the quote already updates live, so the button seemed to do nothing. I replaced it with "Update Order", shown only after submit. When an order locks, the edits are now discarded and the page resets to the accepted order.

**Independently verified invariant and method**
- I worked out a large pizza with two premium toppings, ILOVEPIZZA promo and a 15% tip by hand, and matched it to the UI total to the cent.
- I cancelled after advancing to Started with the script and confirmed the 409 and the locked UI.
