import { afterEach, describe, expect, it, vi } from 'vitest'
import { loadMe, user } from '../useAuth'

// Regression: main.ts mounts the app inside loadMe().finally(), so while this
// promise is pending the page shows nothing at all. A backend that accepts the
// connection and never answers therefore blanks the site indefinitely.
describe('loadMe', () => {
  afterEach(() => { vi.restoreAllMocks(); user.value = null })

  it('settles as logged-out when /auth/me never responds', async () => {
    vi.useFakeTimers()
    // models a hung server: the request never gets a response, but the abort
    // signal still cancels it — which is exactly how the browser's fetch behaves
    vi.stubGlobal('fetch', (_url: string, init?: RequestInit) => new Promise((_res, rej) => {
      init?.signal?.addEventListener('abort', () =>
        rej(Object.assign(new Error('aborted'), { name: 'AbortError' })))
    }))

    const done = loadMe()
    await vi.advanceTimersByTimeAsync(30_000)

    await expect(done).resolves.toBeUndefined()
    expect(user.value).toBeNull()
    vi.useRealTimers()
  })

  it('passes an abort signal so the request can be cancelled', async () => {
    const seen: (AbortSignal | undefined)[] = []
    vi.stubGlobal('fetch', (_url: string, init?: RequestInit) => {
      seen.push(init?.signal ?? undefined)
      return Promise.reject(Object.assign(new Error('aborted'), { name: 'AbortError' }))
    })

    await loadMe()

    expect(seen[0]).toBeInstanceOf(AbortSignal)
    expect(user.value).toBeNull()
  })

  it('still populates the user on a normal response', async () => {
    const me = { id: 'u1', name: 'Gift Mugweni', email: 'gift@example.com', role: 'Admin' as const }
    vi.stubGlobal('fetch', () => Promise.resolve(
      new Response(JSON.stringify(me), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    ))

    await loadMe()

    expect(user.value).toEqual(me)
  })
})
