// Recovery for a client running a stale build.
//
// The deploy replaces the whole dist, so every content-hashed chunk name
// changes. A tab that loaded the previous index.html still asks for the
// previous chunk names, which the origin no longer has — and the lazy route
// import dies with "Failed to fetch dynamically imported module". A reload is
// the only thing that can pull the tab back onto the current build, so do it
// once: a build that is *still* missing a chunk is genuinely broken, and
// reloading again would trap the user in a loop with no way back.

const GUARD_KEY = 'class-booking:chunk-reload'

// Safari/Firefox/Chromium each word the same failure differently.
const CHUNK_LOAD_PATTERNS = [
  /failed to fetch dynamically imported module/i,
  /error loading dynamically imported module/i,
  /importing a module script failed/i,
  /error loading module script/i,
]

export function isChunkLoadError(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error)
  return CHUNK_LOAD_PATTERNS.some(pattern => pattern.test(message))
}

/** Reload at most once per tab session. Returns true if a reload was issued. */
export function attemptRecovery(): boolean {
  if (sessionStorage.getItem(GUARD_KEY))
    return false

  sessionStorage.setItem(GUARD_KEY, '1')
  console.warn('[chunkRecovery] stale build detected — reloading to pick up the current build')
  window.location.reload()
  return true
}
