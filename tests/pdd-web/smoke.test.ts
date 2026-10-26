import { describe, expect, it } from 'vitest'
import App from '../../src/pdd-web/client/App'

describe('scaffolding', () => {
  it('resolves client modules from the tests directory', () => {
    expect(typeof App).toBe('function')
  })
})
