import { useEffect, useRef, useState } from 'react'
import { ApiError, cancelOrder, createSubmissionAttempt, getMenu, getOrder, quoteOrder, submitAttempt, updateOrder } from './api'
import type { MenuDefinition, OrderInput, OrderStatus, OrderView, PriceQuote, SubmissionAttempt, SubmissionReceipt, TipInput } from './types'
import './App.css'

interface DraftLine {
  id: string
  sizeId: string
  quantity: string
  toppingIds: string[]
}

interface Draft {
  items: DraftLine[]
  tip: TipInput
  promoCode: string | null
}

type SubmissionState =
  | { phase: 'draft'; error?: string }
  | { phase: 'sending'; attempt: SubmissionAttempt }
  | { phase: 'unknown'; attempt: SubmissionAttempt; error: string }
  | { phase: 'accepted'; receipt: SubmissionReceipt; current: OrderView | null; checking: boolean;
      action?: 'cancel' | 'update'; error?: string; notice?: string }

const statusLabels: Record<OrderStatus, string> = {
  NotStarted: 'Not Started', Started: 'Started', ReadyForPickup: 'Ready for Pickup',
  Delivered: 'Delivered', Cancelled: 'Cancelled',
}

const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
const dollars = (cents: number) => money.format(cents / 100)

function errorMessage(error: unknown) {
  if (error instanceof TypeError) return 'Could not reach the order service. Check your connection and try again.'
  return error instanceof Error ? error.message : 'The request failed. Please try again.'
}

function toOrderInput(draft: Draft): OrderInput {
  return {
    items: draft.items.map(item => ({
      sizeId: item.sizeId, quantity: Number(item.quantity), toppingIds: item.toppingIds,
    })),
    tip: draft.tip,
    promoCode: draft.promoCode,
  }
}

function App() {
  const [menu, setMenu] = useState<MenuDefinition | null>(null)
  const [menuError, setMenuError] = useState('')
  const [draft, setDraft] = useState<{ order: Draft; revision: number }>({
    order: { items: [], tip: { mode: 'dollars', value: '0' }, promoCode: null }, revision: 0,
  })
  const latestRevision = useRef(0)
  const [quoteResult, setQuoteResult] = useState<{ revision: number; quote?: PriceQuote; error?: string } | null>(null)
  const [showDescription, setShowDescription] = useState(false)
  const [saveRevision, setSaveRevision] = useState<number | null>(null)
  const [submission, setSubmission] = useState<SubmissionState>({ phase: 'draft' })

  useEffect(() => {
    const controller = new AbortController()
    getMenu(controller.signal).then(result => {
      if (controller.signal.aborted) return
      setMenu(result)
      const revision = ++latestRevision.current
      setDraft({
        order: {
          items: [{ id: crypto.randomUUID(), sizeId: result.sizes[0].id, quantity: '1', toppingIds: [] }],
          tip: { mode: 'dollars', value: '0' }, promoCode: null,
        },
        revision,
      })
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setMenuError(errorMessage(error))
    })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!menu) return
    const controller = new AbortController()
    const revision = draft.revision
    quoteOrder(toOrderInput(draft.order), controller.signal).then(quote => {
      if (!controller.signal.aborted && latestRevision.current === revision)
        setQuoteResult({ revision, quote })
    }).catch((error: unknown) => {
      if (!controller.signal.aborted && latestRevision.current === revision)
        setQuoteResult({ revision, error: errorMessage(error) })
    })
    return () => controller.abort()
  }, [draft, menu])

  function changeDraft(order: Draft, saving = false) {
    const revision = ++latestRevision.current
    setDraft({ order, revision })
    setShowDescription(false)
    setSaveRevision(saving ? revision : null)
  }

  function changeLine(id: string, changes: Partial<DraftLine>) {
    changeDraft({ ...draft.order, items: draft.order.items.map(item => item.id === id ? { ...item, ...changes } : item) })
  }

  const currentResult = quoteResult?.revision === draft.revision ? quoteResult : null
  const currentQuote = currentResult?.quote
  const accepted = submission.phase === 'accepted' ? submission : null
  const currentStatusKnown = accepted !== null && accepted.current !== null && !accepted.checking && !accepted.action && !accepted.error
  const canEditDraft = submission.phase === 'draft' || (currentStatusKnown && accepted?.current?.status === 'NotStarted')

  async function checkStatus(receipt: SubmissionReceipt, previous: OrderView | null = null, notice?: string) {
    setSubmission({ phase: 'accepted', receipt, current: previous, checking: true, notice })
    try {
      const current = await getOrder(receipt.orderId)
      setSubmission({ phase: 'accepted', receipt, current, checking: false, notice })
    } catch (error) {
      setSubmission({ phase: 'accepted', receipt, current: previous, checking: false, error: errorMessage(error), notice })
    }
  }

  async function mutateAccepted(action: 'cancel' | 'update', receipt: SubmissionReceipt, previous: OrderView) {
    setSubmission({ phase: 'accepted', receipt, current: previous, checking: false, action })
    try {
      const current = action === 'cancel' ? await cancelOrder(receipt.orderId)
        : await updateOrder(receipt.orderId, toOrderInput(draft.order))
      setSubmission({ phase: 'accepted', receipt, current, checking: false })
    } catch (error) {
      const operation = action === 'cancel' ? 'Cancellation' : 'Update'
      const notice = error instanceof ApiError && error.status < 500
        ? `${operation} rejected: ${error.message}`
        : `${operation} response could not be confirmed: ${errorMessage(error)}`
      await checkStatus(receipt, previous, notice)
    }
  }

  async function sendSubmission(attempt: SubmissionAttempt) {
    setSubmission({ phase: 'sending', attempt })
    let receipt: SubmissionReceipt
    try {
      receipt = await submitAttempt(attempt)
    } catch (error) {
      if (error instanceof ApiError && error.status === 400)
        setSubmission({ phase: 'draft', error: `Order not accepted: ${error.message}` })
      else
        setSubmission({ phase: 'unknown', attempt, error: errorMessage(error) })
      return
    }
    await checkStatus(receipt)
  }

  return (
    <main>
      <h1>Pizza builder</h1>
      {menuError && <p role="alert">{menuError}</p>}
      {!menu && !menuError && <p role="status">Loading menu...</p>}
      {menu && <>
        <fieldset disabled={!canEditDraft}>
          <legend>Order draft</legend>
        {draft.order.items.map((item, index) => <fieldset key={item.id}>
          <legend>Pizza {index + 1}</legend>
          <label>Size <select value={item.sizeId} onChange={event => changeLine(item.id, { sizeId: event.target.value })}>
            {menu.sizes.map(size => <option key={size.id} value={size.id}>{size.name}</option>)}
          </select></label>
          <label>Quantity <input inputMode="numeric" value={item.quantity}
            onChange={event => changeLine(item.id, { quantity: event.target.value })} /></label>
          <fieldset className="toppings">
            <legend>Toppings</legend>
            {menu.toppings.map(topping => <label key={topping.id}>
              <input type="checkbox" checked={item.toppingIds.includes(topping.id)}
                onChange={event => changeLine(item.id, {
                  toppingIds: event.target.checked ? [...item.toppingIds, topping.id] : item.toppingIds.filter(id => id !== topping.id),
                })} />
              {topping.name}
            </label>)}
          </fieldset>
          <button type="button" onClick={() => changeDraft({
            ...draft.order, items: draft.order.items.filter(line => line.id !== item.id),
          })}>Remove pizza {index + 1}</button>
        </fieldset>)}
        <button type="button" onClick={() => changeDraft({
          ...draft.order,
          items: [...draft.order.items, { id: crypto.randomUUID(), sizeId: menu.sizes[0].id, quantity: '1', toppingIds: [] }],
        })}>Add pizza</button>

        <fieldset>
          <legend>Tip and promo</legend>
          <label>Tip mode <select value={draft.order.tip.mode} onChange={event => changeDraft({
            ...draft.order, tip: { ...draft.order.tip, mode: event.target.value === 'percent' ? 'percent' : 'dollars' },
          })}>
            <option value="dollars">Dollars</option>
            <option value="percent">Percent</option>
          </select></label>
          <label>Tip value <input inputMode="decimal" value={draft.order.tip.value} onChange={event => changeDraft({
            ...draft.order, tip: { ...draft.order.tip, value: event.target.value },
          })} /></label>
          <label>Promo code <input value={draft.order.promoCode ?? ''} onChange={event => changeDraft({
            ...draft.order, promoCode: event.target.value === '' ? null : event.target.value,
          })} /></label>
          <p>One optional code: ILOVEPIZZA or SAVE5. Percentage tips use the discounted food subtotal before tax.</p>
        </fieldset>
        {(submission.phase === 'draft' || accepted) && <button type="button"
          disabled={!!accepted && (!currentStatusKnown || !currentQuote || accepted.current?.status !== 'NotStarted')}
          onClick={() => {
            if (accepted?.current) void mutateAccepted('update', accepted.receipt, accepted.current)
            else changeDraft(draft.order, true)
          }}>Save Changes</button>}
        {submission.phase === 'draft' && <>
          <button type="button" disabled={!currentQuote} onClick={() => {
            void sendSubmission(createSubmissionAttempt(toOrderInput(draft.order)))
          }}>Submit Order</button>
          <p>Changes are only a browser draft until you submit the order.</p>
        </>}
        {accepted && <p>Draft edits are separate from the accepted order shown below.</p>}
        </fieldset>

        {submission.phase === 'draft' && submission.error && <p role="alert">{submission.error}</p>}
        {submission.phase === 'sending' && <p role="status">Submitting order. Editing is locked until the outcome is known.</p>}
        {submission.phase === 'unknown' && <section aria-label="Submission recovery">
          <p role="alert">Submission outcome unknown. {submission.error} Retry the same submission to recover its outcome.</p>
          <button type="button" onClick={() => { void sendSubmission(submission.attempt) }}>Retry Submission</button>
        </section>}
        {accepted && <section aria-label="Accepted order">
          <h2>Order accepted</h2>
          <p>Order ID: {accepted.receipt.orderId}</p>
          {accepted.checking && <p role="status">Checking current status. Editing is locked.</p>}
          {accepted.action && <p role="status">{accepted.action === 'cancel' ? 'Cancelling order...' : 'Saving changes...'}</p>}
          {accepted.notice && <p role="alert">{accepted.notice}</p>}
          {accepted.error && <p role="alert">Order accepted, but current status is unavailable. {accepted.error} Use Check Status to recover.</p>}
          {accepted.current ? <>
            <p>{currentStatusKnown ? 'Current' : 'Last known'} status: {statusLabels[accepted.current.status]}</p>
            <p>Accepted total: {dollars(accepted.current.quote.totalCents)}</p>
            <p>{accepted.current.quote.description}</p>
          </> : <p>Original receipt total: {dollars(accepted.receipt.quote.totalCents)}</p>}
          <button type="button" disabled={accepted.checking || !!accepted.action} onClick={() => { void checkStatus(accepted.receipt, accepted.current) }}>Check Status</button>
          <button type="button" disabled={!currentStatusKnown || accepted.current?.status !== 'NotStarted'} onClick={() => {
            if (accepted.current) void mutateAccepted('cancel', accepted.receipt, accepted.current)
          }}>Cancel Order</button>
          <button type="button" disabled={!currentStatusKnown} onClick={() => setSubmission({ phase: 'draft' })}>Start New Order</button>
        </section>}

        <section aria-label="Current quote" aria-live="polite">
          <h2>Current quote</h2>
          {!currentResult && <p role="status">Updating total...</p>}
          {currentResult?.error && <p role="alert">{currentResult.error}</p>}
          {currentQuote && <>
            <ul>
              {currentQuote.lines.map((line, index) => <li key={index}>
                {line.quantity} x {line.sizeName}: base {dollars(line.basePriceCents)}
                {line.toppings.map(topping => ` + ${topping.name} ${dollars(topping.chargeCents)}`).join('')}
                {' = '}{dollars(line.unitPriceCents)} each; line total {dollars(line.lineTotalCents)}
              </li>)}
            </ul>
            <dl>
              <dt>Food subtotal</dt><dd>{dollars(currentQuote.subtotalCents)}</dd>
              <dt>Discount</dt><dd>{dollars(currentQuote.discountCents)}</dd>
              <dt>Discounted food subtotal</dt><dd>{dollars(currentQuote.taxableSubtotalCents)}</dd>
              <dt>Tax</dt><dd>{dollars(currentQuote.taxCents)}</dd>
              <dt>Tip</dt><dd>{dollars(currentQuote.tipCents)}</dd>
              <dt><strong>Total</strong></dt><dd><strong>{dollars(currentQuote.totalCents)}</strong></dd>
            </dl>
          </>}
          <button type="button" disabled={!currentQuote} onClick={() => setShowDescription(!showDescription)}>
            {showDescription ? 'Hide Description' : 'Show Description'}
          </button>
          {showDescription && currentQuote && <p>{currentQuote.description}</p>}
          {submission.phase === 'draft' && currentQuote && saveRevision === draft.revision &&
            <p role="status">Draft validated. Not submitted to the restaurant.</p>}
        </section>
      </>}
    </main>
  )
}

export default App
