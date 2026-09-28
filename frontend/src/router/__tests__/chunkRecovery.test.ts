import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { isChunkLoadError } from '../chunkRecovery'

// attemptRecovery holds a per-document guard, so each test needs its own
// module instance rather than inheriting the previous test's.
const loadRecovery = async () => {
  vi.resetModules()
  return import('../chunkRecovery')
}

describe('isChunkLoadError', () => {
  it('detects the dynamic import failure the browser throws on a stale chunk', () => {
    expect(isChunkLoadError(new TypeError('Failed to fetch dynamically imported module: https://x/assets/MyLessonsView-CFR1dtFa.js'))).toBe(true)
  })

  it('detects Firefox wording for the same failure', () => {
    expect(isChunkLoadError(new TypeError('error loading dynamically imported module: https://x/assets/AdminView.js'))).toBe(true)
  })

  it('detects Safari wording for a module script rejected on MIME type', () => {
    expect(isChunkLoadError(new TypeError('Importing a module script failed.'))).toBe(true)
  })

  it('leaves ordinary navigation errors alone', () => {
    expect(isChunkLoadError(new Error('Missing required param: id'))).toBe(false)
  })

  it('leaves non-Error throwables alone', () => {
    expect(isChunkLoadError(undefined)).toBe(false)
    expect(isChunkLoadError('boom')).toBe(false)
  })
})

describe('attemptRecovery', () => {
  beforeEach(() => {
    sessionStorage.clear()
    vi.spyOn(console, 'warn').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('reloads the tab so a stale build can pick up the current entry chunk', async () => {
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })
    const { attemptRecovery } = await loadRecovery()

    expect(attemptRecovery()).toBe(true)
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('refuses a second reload in the same tab session so a genuinely broken build cannot loop', async () => {
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })
    const { attemptRecovery } = await loadRecovery()

    expect(attemptRecovery()).toBe(true)
    expect(attemptRecovery()).toBe(false)
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('still honours the guard across a reload, since sessionStorage survives one', async () => {
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })
    const first = await loadRecovery()

    expect(first.attemptRecovery()).toBe(true)
    // the reload tears down the document: a fresh module, same sessionStorage
    const second = await loadRecovery()

    expect(second.attemptRecovery()).toBe(false)
    expect(reload).toHaveBeenCalledTimes(1)
  })
})

// Storage is not guaranteed: Safari private browsing and blocked third-party
// storage both make sessionStorage throw. Recovery must not be the one thing
// that depends on it.
describe('attemptRecovery when sessionStorage throws', () => {
  const breakStorage = () => {
    const boom = () => { throw new DOMException('storage is disabled', 'SecurityError') }
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(boom)
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(boom)
  }

  beforeEach(() => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('still reloads', async () => {
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })
    breakStorage()
    const { attemptRecovery } = await loadRecovery()

    expect(attemptRecovery()).toBe(true)
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('does not reload twice within the same document', async () => {
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })
    breakStorage()
    const { attemptRecovery } = await loadRecovery()

    expect(attemptRecovery()).toBe(true)
    expect(attemptRecovery()).toBe(false)
    expect(reload).toHaveBeenCalledTimes(1)
  })
})
