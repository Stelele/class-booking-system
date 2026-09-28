import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  disablePush, enablePush, initPush,
  needsIosInstall, pushBlocked, pushMessage, pushPermission, pushSubscribed, pushSupported,
} from '../usePushNotifications'

// iOS gates this flow behind the Home Screen and a real user gesture, so the
// guards are the behaviour worth pinning down.
const IPHONE_UA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15'
const DESKTOP_UA = 'Mozilla/5.0 (X11; Linux x86_64)'

interface BrowserStub {
  subscribe: ReturnType<typeof vi.fn>
  unsubscribe: ReturnType<typeof vi.fn>
  getSubscription: ReturnType<typeof vi.fn>
}

function stubBrowser(opts: {
  permission?: NotificationPermission
  existing?: ReturnType<typeof heldSubscription> | null
  ios?: boolean
  platform?: string
  maxTouchPoints?: number
  standalone?: boolean
} = {}): BrowserStub {
  const permission = opts.permission ?? 'default'
  const ios = opts.ios ?? true

  const unsubscribe = opts.existing?.unsubscribe ?? vi.fn().mockResolvedValue(true)
  const getSubscription = vi.fn().mockResolvedValue(opts.existing ?? null)
  const subscribe = vi.fn().mockResolvedValue({
    toJSON: () => ({
      endpoint: 'https://fcm.googleapis.com/fcm/send/new',
      keys: { p256dh: 'k', auth: 'a' },
    }),
    unsubscribe,
  })

  const pushManager = { subscribe, getSubscription, unsubscribe }
  const registration = { pushManager }
  const serviceWorker = {
    getRegistration: vi.fn().mockResolvedValue(registration),
    ready: Promise.resolve(registration),
  }

  vi.stubGlobal('Notification', Object.assign(
    class { static permission = permission },
    { requestPermission: vi.fn().mockResolvedValue(permission) },
  ))
  vi.stubGlobal('PushManager', class {})
  vi.stubGlobal('navigator', {
    userAgent: ios ? IPHONE_UA : DESKTOP_UA,
    platform: opts.platform ?? (ios ? 'iPhone' : 'Linux x86_64'),
    maxTouchPoints: opts.maxTouchPoints ?? (ios ? 5 : 0),
    serviceWorker,
  })
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: opts.standalone ?? false }))

  return { subscribe, unsubscribe, getSubscription }
}

/** A subscription the browser already holds, as PushSubscription.toJSON() would serialise it. */
function heldSubscription() {
  return {
    unsubscribe: vi.fn().mockResolvedValue(true),
    toJSON: () => ({
      endpoint: 'https://fcm.googleapis.com/fcm/send/existing',
      keys: { p256dh: 'k', auth: 'a' },
    }),
  }
}

function jsonResponse(body: unknown, ok = true) {
  return Promise.resolve({
    ok,
    status: ok ? 200 : 500,
    statusText: ok ? 'OK' : 'Error',
    json: () => Promise.resolve(body),
  } as Response)
}

/** Records every /api call and answers the push-key endpoint. */
function stubApi(vapidPublicKey: string | null = 'BA1Yb0') {
  const calls: { url: string; method: string; body: unknown }[] = []
  vi.stubGlobal('fetch', (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? 'GET',
      body: init?.body ? JSON.parse(init.body as string) : undefined,
    })
    return url.includes('push-key')
      ? jsonResponse({ enabled: vapidPublicKey !== null, vapidPublicKey })
      : jsonResponse({ registered: true })
  })
  return calls
}

describe('usePushNotifications', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    pushSubscribed.value = false
    needsIosInstall.value = false
    pushPermission.value = 'default'
    pushSupported.value = false
    pushBlocked.value = false
    pushMessage.value = ''
  })

  it('asks for the Home Screen install on iOS instead of prompting', async () => {
    stubBrowser({ permission: 'default' })
    const calls = stubApi()

    await initPush()
    // Not installed: prompting anyway would be rejected by iOS.
    expect(needsIosInstall.value).toBe(true)
    expect(pushSupported.value).toBe(true)

    await enablePush()
    expect(Notification.requestPermission).not.toHaveBeenCalled()
    expect(calls).toHaveLength(0)
    expect(pushMessage.value).toMatch(/Home Screen/i)
  })

  it('treats an iPad reporting as a Mac with touch as iOS', async () => {
    stubBrowser({ platform: 'MacIntel', maxTouchPoints: 5, standalone: false })
    await initPush()
    expect(needsIosInstall.value).toBe(true)
  })

  it('does not ask for the install once running standalone on iOS', async () => {
    stubBrowser({ permission: 'default', standalone: true })
    await initPush()
    expect(needsIosInstall.value).toBe(false)
  })

  it('subscribes and registers with the backend off iOS', async () => {
    const { subscribe } = stubBrowser({ permission: 'granted', ios: false })
    const calls = stubApi()

    await initPush()
    await enablePush()

    expect(subscribe).toHaveBeenCalledOnce()
    expect(subscribe.mock.calls[0]?.[0]).toMatchObject({ userVisibleOnly: true })
    expect(pushSubscribed.value).toBe(true)

    const register = calls.find(c => c.url.includes('push-subscription'))
    expect(register?.method).toBe('POST')
    expect(register?.body).toMatchObject({
      endpoint: 'https://fcm.googleapis.com/fcm/send/new',
      isIos: false,
    })
  })

  it('tells the backend when the device is iOS so it also emails', async () => {
    // iOS display cannot be confirmed, so the backend sends both channels.
    const { subscribe } = stubBrowser({ permission: 'granted', ios: false })
    const calls = stubApi()
    // Register from a real iPhone user agent after init (so needsIosInstall
    // does not short-circuit the subscribe path).
    const { subscribe: iosSubscribe } = stubBrowser({ permission: 'granted', ios: true, standalone: true })

    await initPush()
    await enablePush()

    const register = calls.find(c => c.url.includes('push-subscription'))
    expect(register?.body).toMatchObject({ isIos: true })
    expect(iosSubscribe).toHaveBeenCalledOnce()
    expect(subscribe).not.toHaveBeenCalled()
  })

  it('does not subscribe when permission is denied', async () => {
    const { subscribe } = stubBrowser({ permission: 'denied', ios: false })
    stubApi()

    await initPush()
    await enablePush()

    expect(subscribe).not.toHaveBeenCalled()
    expect(pushSubscribed.value).toBe(false)
  })

  it('explains that incognito windows cannot use the Push API', async () => {
    // Chrome refuses the Push API in incognito and only says so in the console.
    const { subscribe } = stubBrowser({ permission: 'granted', ios: false })
    vi.stubGlobal('navigator', {
      userAgent: `${DESKTOP_UA} Incognito/1.0`,
      platform: 'Linux x86_64',
      maxTouchPoints: 0,
      serviceWorker: {
        getRegistration: vi.fn().mockResolvedValue({
          pushManager: { subscribe, getSubscription: vi.fn().mockResolvedValue(null) },
        }),
        ready: Promise.resolve({ pushManager: { subscribe } }),
      },
    })
    const calls = stubApi()

    await initPush()
    await enablePush()

    expect(pushBlocked.value).toBe(true)
    expect(subscribe).not.toHaveBeenCalled()
    expect(pushMessage.value).toMatch(/private|incognito/i)
    // No pointless request to the server either.
    expect(calls.filter(c => c.url.includes('push-key'))).toHaveLength(0)
  })

  it('reports unsupported when the browser has no PushManager', async () => {
    vi.stubGlobal('navigator', { userAgent: IPHONE_UA, serviceWorker: {} })

    await initPush()

    expect(pushSupported.value).toBe(false)
    expect(pushPermission.value).toBe('unsupported')
  })

  it('unsubscribes and tells the backend on disable', async () => {
    const existing = heldSubscription()
    stubBrowser({ ios: false, existing })
    const calls = stubApi()

    await initPush()
    await disablePush()

    expect(existing.unsubscribe).toHaveBeenCalledOnce()
    expect(calls.some(c => c.url.includes('push-subscription') && c.method === 'DELETE')).toBe(true)
    expect(pushSubscribed.value).toBe(false)
  })

  it('re-registers a subscription the browser already holds', async () => {
    stubBrowser({ permission: 'granted', ios: false, existing: heldSubscription() })
    const calls = stubApi()

    await initPush()

    expect(pushSubscribed.value).toBe(true)
    expect(calls.find(c => c.url.includes('push-subscription'))?.body)
      .toMatchObject({ endpoint: 'https://fcm.googleapis.com/fcm/send/existing' })
    expect(calls.find(c => c.url.includes('push-subscription'))?.body)
      .toHaveProperty('isIos')
  })

  it('explains when the server has no VAPID key yet', async () => {
    stubBrowser({ permission: 'granted', ios: false })
    stubApi(null)

    await initPush()
    await enablePush()

    expect(pushSubscribed.value).toBe(false)
    expect(pushMessage.value).toMatch(/not configured/i)
  })
})
