import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, getMenu, quoteOrder, RequestTimeoutError } from '../../src/pdd-web/client/api'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('API requests', () => {
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
