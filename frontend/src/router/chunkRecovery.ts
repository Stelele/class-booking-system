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
// never be the thing that fails there.
//
// window.name is the fallback: it needs no storage API and it survives a reload
// in the same tab (verified in Chrome rather than assumed). Without something
// that outlives the document, a chunk still missing after the reload reloads
// forever — every new document starts with a clean in-memory flag and no
// storage to consult. Skipping recovery when storage fails, the other way out,
// would just hand back the opposite failure: no recovery at all.
let reloadedThisDocument = false

/** Has this tab already been recovered? Persisted guard first, then this document. */
function alreadyReloaded(): boolean {
  if (reloadedThisDocument) return true
  try {
    return sessionStorage.getItem(GUARD_KEY) !== null
  }
  catch {
    return window.name === GUARD_KEY
  }
}

/** Arm both guards. Persistence is best-effort; the in-memory flag is not. */
function rememberReload(): void {
  reloadedThisDocument = true
  try {
    sessionStorage.setItem(GUARD_KEY, '1')
  }
  catch {
    try {
      window.name = GUARD_KEY
    }
    catch {
      // nothing durable left to guard with, so the worst case is one reload
      // per document rather than one per call
    }
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
