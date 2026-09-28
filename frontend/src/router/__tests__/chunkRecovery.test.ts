import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { isChunkLoadError, attemptRecovery } from '../chunkRecovery'

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

  it('reloads the tab so a stale build can pick up the current entry chunk', () => {
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })

    expect(attemptRecovery()).toBe(true)
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('refuses a second reload in the same tab session so a genuinely broken build cannot loop', () => {
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })

    expect(attemptRecovery()).toBe(true)
    expect(attemptRecovery()).toBe(false)
    expect(reload).toHaveBeenCalledTimes(1)
  })
})
