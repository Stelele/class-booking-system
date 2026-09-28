/// <reference lib="webworker" />
import { clientsClaim } from 'workbox-core'
import { cleanupOutdatedCaches, createHandlerBoundToURL, precacheAndRoute } from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'
import { NetworkFirst, NetworkOnly } from 'workbox-strategies'
import { CacheableResponsePlugin } from 'workbox-cacheable-response'
import { ExpirationPlugin } from 'workbox-expiration'

declare let self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<unknown> }

self.skipWaiting()
clientsClaim()
cleanupOutdatedCaches()
precacheAndRoute(self.__WB_MANIFEST)

// SPA: unknown navigations get the app shell
registerRoute(new NavigationRoute(createHandlerBoundToURL('/index.html'), {
  denylist: [/^\/api\//],
}))

// read-through cache: shared calendar data stays visible offline,
// fresh data wins whenever the network is up
registerRoute(
  ({ url, request }) =>
    request.method === 'GET'
    && (url.pathname === '/api/slots' || /^\/api\/slots\/[^/]+\/ics$/.test(url.pathname)),
  new NetworkFirst({
    cacheName: 'api-cache',
    networkTimeoutSeconds: 4,
    plugins: [
      new CacheableResponsePlugin({ statuses: [200] }),
      new ExpirationPlugin({ maxEntries: 64, maxAgeSeconds: 60 * 60 * 24 * 7 }),
    ],
  }),
)

// never cache mutations — bookings must hit the server
registerRoute(
  ({ url, request }) => url.pathname.startsWith('/api/') && request.method !== 'GET',
  new NetworkOnly(),
)

interface PushPayload {
  title?: string
  body?: string
  url?: string
  tag?: string
}

self.addEventListener('push', (event) => {
  let data: PushPayload = {}
  try {
    data = (event.data?.json() ?? {}) as PushPayload
  }
  catch {
    data = { title: 'Lesson reminder', body: event.data?.text() ?? '' }
  }

  // Safari revokes the notification permission if the service worker receives
  // a push and does not immediately show a visible notification, so this must
  // stay a direct showNotification call — never defer it behind a fetch.
  event.waitUntil(self.registration.showNotification(data.title || 'Lesson reminder', {
    body: data.body || '',
    icon: '/pwa-192x192.png',
    badge: '/pwa-192x192.png',
    tag: data.tag,
    renotify: true,
    data: { url: data.url || '/calendar' },
  }))
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = (event.notification.data as { url?: string } | null)?.url || '/calendar'

  event.waitUntil((async () => {
    const open = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
    for (const client of open) {
      if (new URL(client.url).pathname === url) {
        await client.focus()
        return
      }
    }
    await self.clients.openWindow(url)
  })())
})
