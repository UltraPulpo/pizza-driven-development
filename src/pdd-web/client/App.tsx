import { useEffect, useRef, useState } from 'react'
import { getMenu, quoteOrder } from './api'
import type { MenuDefinition, OrderInput, PriceQuote, TipInput } from './types'
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

  return (
    <main>
      <h1>Pizza builder</h1>
      {menuError && <p role="alert">{menuError}</p>}
      {!menu && !menuError && <p role="status">Loading menu...</p>}
      {menu && <>
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
        <button type="button" onClick={() => changeDraft(draft.order, true)}>Save Changes</button>
        <p>Changes are only a browser draft; nothing has been submitted to the restaurant.</p>

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
          {currentQuote && saveRevision === draft.revision && <p role="status">Draft validated. Not submitted to the restaurant.</p>}
        </section>
      </>}
    </main>
  )
}

export default App
