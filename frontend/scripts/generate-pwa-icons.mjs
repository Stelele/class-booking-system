// Generates PWA icons (192, 512, maskable-512) from an inline SVG.
// Brand: dark slate tile + green calendar glyph — matches the Nuxt UI theme.
import sharp from 'sharp'
import { mkdirSync } from 'node:fs'

const svg = (pad) => `
<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
  <rect width="512" height="512" rx="${pad > 0 ? 0 : 96}" fill="#0f172a"/>
  <g transform="translate(${64 + pad}, ${64 + pad}) scale(${(512 - 2 * (64 + pad)) / 384})">
    <rect x="32" y="64" width="320" height="288" rx="32" fill="#22c55e"/>
    <rect x="32" y="64" width="320" height="72" rx="32" fill="#16a34a"/>
    <rect x="96" y="24" width="32" height="72" rx="16" fill="#e2e8f0"/>
    <rect x="256" y="24" width="32" height="72" rx="16" fill="#e2e8f0"/>
    <circle cx="128" cy="216" r="24" fill="#0f172a"/>
    <circle cx="224" cy="216" r="24" fill="#0f172a"/>
    <circle cx="320" cy="216" r="24" fill="#0f172a"/>
    <circle cx="128" cy="288" r="24" fill="#0f172a"/>
    <circle cx="224" cy="288" r="24" fill="#0f172a"/>
  </g>
</svg>`

mkdirSync('public', { recursive: true })

const targets = [
  { file: 'public/pwa-192x192.png', size: 192, pad: 0 },
  { file: 'public/pwa-512x512.png', size: 512, pad: 0 },
  // maskable needs ~20% safe zone on all sides
  { file: 'public/pwa-maskable-512x512.png', size: 512, pad: 52 },
]

for (const t of targets) {
  await sharp(Buffer.from(svg(t.pad))).resize(t.size, t.size).png().toFile(t.file)
  console.log('wrote', t.file)
}
