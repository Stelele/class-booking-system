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

// sessionStorage is the guard that survives the reload, so it is the one that
// stops a reload loop. It is not always available — Safari private browsing
// and blocked third-party storage both throw on access — and recovery must
// never be the thing that fails there, so fall back to a per-document flag.
// That is a weaker guard: without storage a permanently missing chunk would
// reload once per document. Acceptable, because it needs blocked storage AND
// a broken build at the same time, whereas the fallback it rescues is a
// blocked-storage user hitting an ordinary deploy.
let reloadedThisDocument = false

function alreadyReloaded(): boolean {
  if (reloadedThisDocument) return true
  try {
    return sessionStorage.getItem(GUARD_KEY) !== null
  }
  catch {
    return false
  }
}

function rememberReload(): void {
  reloadedThisDocument = true
  try {
    sessionStorage.setItem(GUARD_KEY, '1')
  }
  catch {
    // reloadedThisDocument still holds for the life of this document
  }
}

/** Reload at most once per tab session. Returns true if a reload was issued. */
export function attemptRecovery(): boolean {
  if (alreadyReloaded()) return false

  rememberReload()
  console.warn('[chunkRecovery] stale build detected — reloading to pick up the current build')
  window.location.reload()
  return true
}
