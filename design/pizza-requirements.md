# Pizza-Driven Development - Requirements

## 1. Overview

An anonymous customer builds a pizza order, sees its total change, and submits it to a restaurant. The customer can check its status and change or cancel it until the kitchen starts it.

Source of truth: `docs\Senior_Software_Developer_—_Take-Home_(Pizza-Driven_Development).docx` (local-only and intentionally git-ignored). This document captures the assessment's requirements and approved business-rule clarifications, not implementation choices. Clarifications are recorded as assumptions below.

The assessment has a three-hour timebox. Correct pricing and a defensible order lifecycle take priority over breadth of features; polish is secondary.

## 2. Core Concepts

| Concept | Meaning |
|---------|---------|
| Order | One or more pizza line items, with an optional tip and applicable promotion. |
| Line item | A pizza size, quantity, and zero or more toppings. |
| Topping category | Determines the topping's charge as a percentage of the base pizza price. |
| Promo code | A discount with eligibility and combination rules. |
| Order status | The restaurant's progress on a submitted order. |

## 3. Functional Requirements

### 3.1 Order Builder

- **FR-1:** The customer SHALL be able to add, remove, and modify line items, including their sizes, quantities, and toppings.
- **FR-2:** The total SHALL update automatically whenever the order changes, including tip or promo changes. The displayed total SHALL match what the restaurant charges for that order.
- **FR-3:** On request, the system SHALL produce a written description containing the quantity, size, and toppings of each line item.
- **FR-4:** Invalid input SHALL produce a clear customer-facing message, including invalid tip or promo input.

### 3.2 Pricing

**FR-5:** Pricing SHALL follow these rules:

| Item | Rule |
|------|------|
| Small pizza | $5.00 |
| Medium pizza | $7.00 |
| Large pizza | $12.00 |
| Each basic topping | 8% of the base pizza price |
| Each premium topping | 11% of the base pizza price |
| Tax | 6.5% of the discounted pizza-and-topping subtotal, excluding the voluntary tip |
| Tip | Customer-entered dollar amount or percentage of the discounted pizza-and-topping subtotal before tax |
| `ILOVEPIZZA` | 10% off the undiscounted pizza-and-topping subtotal, excluding tax and tip |
| `SAVE5` | $5.00 off when the undiscounted pizza-and-topping subtotal, excluding tax and tip, is strictly greater than $20.00 |
| Promo combinations | `SAVE5` and `ILOVEPIZZA` SHALL NOT be combined |
| Promo validation | Codes SHALL match the printed code exactly; an incorrect or ineligible code SHALL produce a clear error |
| Customer-facing amounts | Dollars and whole cents |

**Basic toppings:** pepperoni, sausage, extra cheese, onions, black olives, green peppers.

**Premium toppings:** bacon, chicken, garlic, artichoke hearts, sun-dried tomatoes, pineapple.

### 3.3 Order Lifecycle

- **FR-6:** The customer SHALL be able to submit an order and receive an order ID.
- **FR-7:** The customer SHALL be able to query the order's status: **Not Started**, **Started**, **Ready for Pickup**, or **Delivered**.
- **FR-8:** The customer SHALL be able to update or cancel an order only while it is **Not Started**. Successful cancellation SHALL be reported as **Cancelled**, and the order SHALL NOT progress further.
- **FR-9:** The restaurant side SHALL advance order status. Restaurant activity SHALL be simulated, and how to exercise the simulation SHALL be documented.
- **FR-10:** Retrying the same submission after a timeout SHALL return the same order ID without creating another order. When cancellation and kitchen start compete, the first successful action SHALL win and the other SHALL be rejected; both SHALL NOT succeed. These guarantees SHALL be documented.

### 3.4 Extensibility

- **FR-11:** Adding a size, topping, topping category, or promo code SHALL NOT require changes to core pricing logic.
- **FR-12:** Extensibility SHALL accommodate promotions with their own conditions and conflicts with other promotions.

## 4. Service Interface Requirements

**IR-1:** The order service SHALL run as a separate process from the order builder and communicate with it over the network.

| Required operation | Customer-visible outcome |
|--------------------|--------------------------|
| Submit order | Receive an order ID |
| Query order status | See the restaurant's progress or a Cancelled outcome |
| Update order | Change an order only while Not Started |
| Cancel order | Cancel an order only while Not Started |

No particular interface style or endpoint names are prescribed by the brief.

## 5. Quality and Verification

- **QR-1:** Pricing SHALL use exact arithmetic and consistent rounding, with one source of truth for the total.
- **QR-2:** The service SHALL validate customer input and enforce permitted order-state changes.
- **QR-3:** Automated tests SHALL cover the invariants considered most important, rather than only happy paths. The README SHALL identify those invariants, explain their selection, and state what was not covered.
- **QR-4:** Scope and complexity SHALL remain proportionate to the timebox. A working subset with an honest cut list is preferable to broken breadth.

## 6. Deliverables

| ID | Deliverable | Required content |
|----|-------------|------------------|
| D-1 | Hosted git repository | Submit through a git repository host, not a zip or tar bundle. |
| D-2 | README | Local run instructions for builder and service, and test instructions, using one or two commands each; a short API description with example requests; an explicit Assumptions list; tested invariants, why they were selected, and coverage omissions. |
| D-3 | Approximately one-page design write-up | Explain money representation, total calculation, pricing ownership, and why; extension to sizes, categories, and conditional/conflicting promos; lifecycle guarantees for submission retries and cancellation racing kitchen start; deliberate cuts or fakes and what would come next. |
| D-4 | AI usage note | Name tools and their uses; give one example of AI output rejected or corrected; identify one behavior or invariant independently verified and how. |
| D-5 | Automated tests | Demonstrate the selected important invariants. |

Evaluation covers data modeling, pricing, service design, correctness, testing, extensibility, judgment (including commit history), communication, and effective, verified AI use. Visual design and deployment are not evaluated. Easy local startup is valued; setup conveniences are a bonus, not a requirement.

## 7. Scope Boundaries

Assume one restaurant and anonymous customers.

Out of scope: authentication, customer accounts, real payments, real kitchen or delivery integration, menu administration UI, and multiple locations.

## 8. Confirmed Assumptions

The following approved business-rule assumptions SHALL also be recorded in the README's Assumptions section.

| Area | Approved assumption |
|------|---------------------|
| Menu and promo definitions | Menu prices and promo rules are fixed during ordering. Runtime changes to those definitions are outside this assessment's scope. |
| Tax | Apply the brief's 6.5% rate to the food subtotal after the promo discount. Tips are voluntary and excluded from the taxable amount. |
| Percentage tips | Calculate from the food subtotal after the promo discount and before tax. |
| Rounding | Round to the nearest cent, with halfway amounts rounded up. Round each topping charge per pizza, the order-level discount, tax, and percentage tip before combining amounts, so the displayed breakdown adds up to the final charge. |
| Dollar tips | Round entered dollar tips to whole cents using the same rounding rule. |
| Tip range | Accept nonnegative dollar tips and percentages, including zero, without an arbitrary percentage cap. Reject negative or nonnumeric tips with a clear message. |
| Promo bases | Both promo discounts and SAVE5 eligibility use the pizza-and-topping subtotal before discounts, tax, or tip. |
| Promo codes | Require an exact match to the printed code, including case and whitespace. Apply a valid code or show a clear validation error; no additional promo workflow is required. |
| Promo combinations | Accept one optional promo code per order. All promos are mutually exclusive; supporting combinations of future promos is outside this version. Each new promo still owns its eligibility and discount rule. |
| Draft saves | Before submission, Save Changes updates only the current browser draft. The API provides validation and pricing but does not persist a draft or expose it to the kitchen. Submit Order creates the persisted order; later updates remain allowed while Not Started. |
| Submission retries | Retrying the same submission attempt after a timeout returns the same order ID and does not create another order. A deliberate new submission receives a new ID even when its contents are identical. |
| Cancellation versus kitchen start | The first successful action wins and the other is rejected. If cancellation succeeds, the kitchen cannot start the order; if kitchen start succeeds, cancellation is rejected with a clear message. |
| After cancellation | A status lookup returns a clear Cancelled outcome. Cancellation is terminal, not another kitchen-progress stage. |
