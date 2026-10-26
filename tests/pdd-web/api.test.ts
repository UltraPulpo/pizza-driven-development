import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, cancelOrder, createSubmissionAttempt, getMenu, getOrder, quoteOrder, RequestTimeoutError, submitAttempt, updateOrder } from '../../src/pdd-web/client/api'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('API requests', () => {
  it('sends a complete replacement once without changing tip text', async () => {
    const fetch = vi.fn().mockResolvedValue(Response.json({ status: 'NotStarted' }))
    vi.stubGlobal('fetch', fetch)
    const input = {
      items: [{ sizeId: 'large', quantity: 2, toppingIds: ['bacon'] }],
      tip: { mode: 'dollars' as const, value: '1.0050' }, promoCode: null,
    }
    await updateOrder('order-id', input)
    expect(fetch).toHaveBeenCalledWith('/api/orders/order-id', expect.objectContaining({
      method: 'PUT', body: JSON.stringify(input), headers: { 'Content-Type': 'application/json' },
    }))
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('cancels once and leaves recovery to an explicit status lookup', async () => {
    const fetch = vi.fn().mockResolvedValue(Response.json({ status: 'Cancelled' }))
    vi.stubGlobal('fetch', fetch)
    await expect(cancelOrder('order-id')).resolves.toEqual({ status: 'Cancelled' })
    expect(fetch).toHaveBeenCalledWith('/api/orders/order-id/cancel', expect.objectContaining({ method: 'POST' }))
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('retries a captured body/key unchanged even if the draft changes', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(Response.json({ orderId: 'accepted' })))
    vi.stubGlobal('fetch', fetch)
    const input = {
      items: [{ sizeId: 'small', quantity: 1, toppingIds: [] }],
      tip: { mode: 'dollars' as const, value: '1.0050' }, promoCode: null,
    }
    const attempt = createSubmissionAttempt(input)
    input.tip.value = '2'
    await submitAttempt(attempt)
    await submitAttempt(attempt)
    for (const [path, options] of fetch.mock.calls) {
      expect(path).toBe('/api/orders')
      expect(options).toMatchObject({
        method: 'POST', body: attempt.body,
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key },
      })
      expect(JSON.parse(options.body).tip.value).toBe('1.0050')
    }
    expect(fetch).toHaveBeenCalledTimes(2)
    expect(Object.isFrozen(attempt)).toBe(true)
  })

  it('gives a deliberate new submission a fresh key for identical contents', () => {
    const input = {
      items: [{ sizeId: 'small', quantity: 1, toppingIds: [] }],
      tip: { mode: 'dollars' as const, value: '0' }, promoCode: null,
    }
    const first = createSubmissionAttempt(input)
    const second = createSubmissionAttempt(input)
    expect(first.key).not.toBe(second.key)
    expect(first.body).toBe(second.body)
  })

  it('looks up current state without replaying a submission', async () => {
    const current = { orderId: 'order-id', status: 'NotStarted' }
    const fetch = vi.fn().mockResolvedValue(Response.json(current))
    vi.stubGlobal('fetch', fetch)
    expect(await getOrder('order-id')).toEqual(current)
    expect(fetch).toHaveBeenCalledWith('/api/orders/order-id', expect.not.objectContaining({ method: 'POST' }))
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('quotes the order with raw tip text unchanged and no client prices', async () => {
    const fetch = vi.fn().mockResolvedValue(Response.json({ totalCents: 634 }))
    vi.stubGlobal('fetch', fetch)
    const input = {
      items: [{ sizeId: 'small', quantity: 1, toppingIds: [] }],
      tip: { mode: 'dollars' as const, value: '1.0050' }, promoCode: null,
    }
    expect(await quoteOrder(input)).toEqual({ totalCents: 634 })
    expect(fetch).toHaveBeenCalledWith('/api/quote', expect.objectContaining({
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
    }))
    expect(JSON.parse(fetch.mock.calls[0][1].body).tip.value).toBe('1.0050')
  })

  it('loads the menu from the service', async () => {
    const menu = { sizes: [], categories: [], toppings: [] }
    const fetch = vi.fn().mockResolvedValue(Response.json(menu))
    vi.stubGlobal('fetch', fetch)
    expect(await getMenu()).toEqual(menu)
    expect(fetch).toHaveBeenCalledWith('/api/menu', expect.objectContaining({ signal: expect.any(AbortSignal) }))
  })

  it('preserves Problem Details and status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(
      { title: 'Invalid input', detail: 'Select a known size.' }, { status: 400 },
    )))
    await expect(getMenu()).rejects.toMatchObject({
      name: 'ApiError', status: 400, message: 'Select a known size.',
      problem: { detail: 'Select a known size.' },
    })
  })

  it('reports a non-JSON service error as an HTTP error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('proxy unavailable', { status: 503 })))
    await expect(getMenu()).rejects.toBeInstanceOf(ApiError)
  })

  it('keeps network failure distinguishable from an HTTP response', async () => {
    const failure = new TypeError('Network unavailable')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(failure))
    await expect(getMenu()).rejects.toBe(failure)
  })

  it('times out a request after ten seconds', async () => {
    vi.useFakeTimers()
    vi.stubGlobal('fetch', vi.fn((_path: string, options: RequestInit) =>
      new Promise<Response>((_resolve, reject) => {
        options.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true })
      })))
    const result = expect(getMenu()).rejects.toBeInstanceOf(RequestTimeoutError)
    await vi.advanceTimersByTimeAsync(10_000)
    await result
  })

  it('preserves caller cancellation instead of reporting a timeout', async () => {
    const controller = new AbortController()
    vi.stubGlobal('fetch', vi.fn((_path: string, options: RequestInit) =>
      new Promise<Response>((_resolve, reject) => {
        options.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true })
      })))
    const result = expect(getMenu(controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
    controller.abort()
    await result
  })
})
