import type { MenuDefinition, OrderInput, PriceQuote } from './types'

export class ApiError extends Error {
  readonly status: number
  readonly problem: { detail?: string; title?: string }

  constructor(status: number, problem: { detail?: string; title?: string }) {
    super(problem.detail ?? problem.title ?? `Request failed (${status}).`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

export class RequestTimeoutError extends Error {
  constructor() {
    super('The request timed out. Check your connection and try again.')
    this.name = 'RequestTimeoutError'
  }
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const timeout = new AbortController()
  const timer = setTimeout(() => timeout.abort(), 10_000)
  const signal = AbortSignal.any([timeout.signal, ...(options.signal ? [options.signal] : [])])
  try {
    const response = await fetch(path, { ...options, signal })
    if (!response.ok) {
      let problem: unknown
      if (response.headers.get('content-type')?.includes('json')) {
        try {
          problem = await response.json()
        } catch (error) {
          if (!(error instanceof SyntaxError)) throw error
          throw new ApiError(response.status, { detail: `Request failed (${response.status}); the error response was not valid JSON.` })
        }
      }
      const detail = typeof problem === 'object' && problem !== null && 'detail' in problem && typeof problem.detail === 'string'
        ? problem.detail : undefined
      const title = typeof problem === 'object' && problem !== null && 'title' in problem && typeof problem.title === 'string'
        ? problem.title : undefined
      throw new ApiError(response.status, { detail, title })
    }
    return await response.json() as T
  } catch (error) {
    if (timeout.signal.aborted && !options.signal?.aborted) throw new RequestTimeoutError()
    throw error
  } finally {
    clearTimeout(timer)
  }
}

export function getMenu(signal?: AbortSignal): Promise<MenuDefinition> {
  return request('/api/menu', { signal })
}

export function quoteOrder(input: OrderInput, signal?: AbortSignal): Promise<PriceQuote> {
  return request('/api/quote', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
    signal,
  })
}
