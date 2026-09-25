import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import ui from '@nuxt/ui/vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    vue(),
    ui(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.ico', 'pwa-192x192.png', 'pwa-512x512.png', 'pwa-maskable-512x512.png'],
      manifest: {
        name: 'Lesson Booking',
        short_name: 'Lessons',
        description: 'Shared booking calendar for evening programming lessons (20:30 Harare, Mon–Sat).',
        theme_color: '#0f172a',
        background_color: '#0f172a',
        display: 'standalone',
        start_url: '/calendar',
        icons: [
          { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' },
          { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png' },
          { src: 'pwa-maskable-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // SPA: unknown navigations get the app shell
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//],
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
        runtimeCaching: [
          {
            // read-through cache: shared calendar data stays visible offline,
            // fresh data wins whenever the network is up
            urlPattern: ({ url, request }) =>
              request.method === 'GET'
              && (url.pathname === '/api/slots'
                || /^\/api\/slots\/[^/]+\/ics$/.test(url.pathname)),
            handler: 'NetworkFirst',
            options: {
              cacheName: 'api-cache',
              networkTimeoutSeconds: 4,
              expiration: { maxEntries: 64, maxAgeSeconds: 60 * 60 * 24 * 7 },
              cacheableResponse: { statuses: [200] },
            },
          },
          {
            // never cache mutations — bookings must hit the server
            urlPattern: ({ url, request }) =>
              url.pathname.startsWith('/api/') && request.method !== 'GET',
            handler: 'NetworkOnly',
          },
        ],
      },
      devOptions: { enabled: false }, // dev + E2E stay SW-free
    }),
  ],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: { proxy: { '/api': 'http://localhost:8080' } },
})
