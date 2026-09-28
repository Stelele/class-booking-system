import { ref } from 'vue'
import { api } from './useApi'

/**
 * Web Push opt-in.
 *
 * iOS rules this design cannot work around:
 *  - `pushManager.subscribe` does not exist until the site has been added to
 *    the Home Screen and launched from that icon.
 *  - `Notification.requestPermission` must be called from a real user gesture;
 *    calling it on mount is silently ignored.
 *  - Safari revokes permission if the service worker receives a push and does
 *    not immediately show a notification (see sw.ts).
 *
 * Anyone who cannot grant permission simply falls back to email — the backend
 * only pushes when a subscription exists.
 */

export type PushPermission = 'unsupported' | 'default' | 'granted' | 'denied'

export const pushPermission = ref<PushPermission>('default')
export const pushSupported = ref(false)
/** True in incognito / private windows, where Chrome has no Push API. */
export const pushBlocked = ref(false)
export const pushSubscribed = ref(false)
/** iOS user is in a browser tab and must install to the Home Screen first. */
export const needsIosInstall = ref(false)
export const pushBusy = ref(false)
export const pushMessage = ref('')

/** iPadOS 13+ reports as Mac; the touch-point count is the reliable tell. */
export const isIos = () =>
  /iPad|iPhone|iPod/.test(navigator.userAgent)
  || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)

/**
 * Chrome refuses the Push API entirely in incognito / private windows and says
 * so only in the console. Detecting it lets the UI explain the dead end
 * instead of failing with an opaque error.
 */
export const isPrivateWindow = () => /\bIncognito\b/.test(navigator.userAgent)

const isStandalone = () =>
  window.matchMedia?.('(display-mode: standalone)').matches
  ?? (navigator as { standalone?: boolean }).standalone === true

/** VAPID public keys are base64url; the Push API wants raw bytes. */
function base64UrlToUint8Array(value: string): Uint8Array {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/')
  const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4)
  const raw = atob(padded)
  return Uint8Array.from(raw, c => c.charCodeAt(0))
}

async function currentSubscription(): Promise<PushSubscription | null> {
  if (!('serviceWorker' in navigator) || !('PushManager' in window)) return null
  const registration = await navigator.serviceWorker.getRegistration()
  if (!registration) return null
  return registration.pushManager.getSubscription()
}

async function syncFromBrowser() {
  const sub = await currentSubscription()
  pushSubscribed.value = sub !== null
  if (sub) {
    // Re-register after a deploy that may have lost the local subscription.
    await api('/notifications/push-subscription', {
      method: 'POST',
      // isIos tells the backend that this device's display cannot be
      // confirmed, so it sends the email copy too.
      body: JSON.stringify({ ...sub.toJSON(), isIos: isIos() }),
    }).catch(() => {})
  }
}

export async function initPush() {
  pushSupported.value = 'serviceWorker' in navigator && 'PushManager' in window
    && 'Notification' in window
  pushBlocked.value = isPrivateWindow()
  needsIosInstall.value = isIos() && !isStandalone()

  if (!pushSupported.value) {
    pushPermission.value = 'unsupported'
    return
  }

  pushPermission.value = Notification.permission as PushPermission
  // Keep the backend's view in step with this device.
  await syncFromBrowser().catch(() => {})
}

/**
 * Must be called synchronously from a click handler — iOS rejects a
 * permission request that is not tied to a user gesture.
 */
export async function enablePush() {
  if (!pushSupported.value) return
  pushBusy.value = true
  pushMessage.value = ''
  try {
    if (needsIosInstall.value) {
      pushMessage.value = 'Add this site to your Home Screen first, then open it from the icon.'
      return
    }

    if (pushBlocked.value) {
      pushMessage.value = 'Your browser does not allow notifications in a private or '
        + 'incognito window. Open the site in a normal window, or we will email you.'
      return
    }

    const permission = await Notification.requestPermission()
    pushPermission.value = permission as PushPermission
    if (permission !== 'granted') {
      pushMessage.value = 'Notifications were blocked in your browser settings.'
      return
    }

    const { vapidPublicKey, enabled } = await api<{ vapidPublicKey: string | null; enabled: boolean }>(
      '/notifications/push-key',
    )
    if (!enabled || !vapidPublicKey) {
      pushMessage.value = 'Push is not configured on the server yet.'
      return
    }

    const registration = await navigator.serviceWorker.ready
    const sub = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: base64UrlToUint8Array(vapidPublicKey),
    }).catch((err: unknown) => {
      // NotSupportedError here is almost always a private/incognito window.
      pushBlocked.value = err instanceof DOMException && err.name === 'NotSupportedError'
      throw err
    })

    await api('/notifications/push-subscription', {
      method: 'POST',
      body: JSON.stringify({ ...sub.toJSON(), isIos: isIos() }),
    })
    pushSubscribed.value = true
    pushMessage.value = 'Reminders are on.'
  }
  catch (err) {
    pushMessage.value = pushBlocked.value
      ? 'This browser window cannot use notifications (private browsing). '
        + 'Open the site in a normal window, or we will email you.'
      : err instanceof Error ? err.message : 'Could not enable reminders.'
  }
  finally {
    pushBusy.value = false
  }
}

export async function disablePush() {
  pushBusy.value = true
  pushMessage.value = ''
  try {
    const sub = await currentSubscription()
    if (sub) await sub.unsubscribe()
    await api('/notifications/push-subscription', { method: 'DELETE' }).catch(() => {})
    pushSubscribed.value = false
    pushMessage.value = 'Reminders are off. We will email you instead.'
  }
  finally {
    pushBusy.value = false
  }
}
