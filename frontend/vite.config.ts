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
      // Custom service worker: the generateSW strategy cannot host the
      // push / notificationclick handlers that iOS requires.
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      includeAssets: ['favicon.ico', 'pwa-192x192.png', 'pwa-512x512.png', 'pwa-maskable-512x512.png'],
      manifest: {
        name: 'Lesson Booking',
        short_name: 'Lessons',
        description: 'Shared booking calendar for evening programming lessons (20:30 Harare, Mon–Sat).',
        theme_color: '#0f172a',
        background_color: '#0f172a',
        display: 'standalone',
        // Stable identity so iOS does not treat a redeploy as a new app.
        id: '/',
        start_url: '/calendar',
        icons: [
          { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          { src: 'pwa-maskable-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      injectManifest: {
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
      },
      devOptions: { enabled: false }, // dev + E2E stay SW-free
    }),
  ],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: { proxy: { '/api': 'http://localhost:8080' } },
})
