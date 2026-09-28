// Generates PWA icons from an inline SVG.
// Brand: dark slate tile + green calendar glyph — matches the Nuxt UI theme.
//
// Two families:
//  - `rounded: false` — web-manifest icons; Android masks them itself.
//  - `rounded: true`  — apple-touch-icon. iOS ignores the web manifest for
//    Home Screen icons, requires a full-bleed square, and applies its own
//    rounding — so shipping pre-rounded art here gets rounded twice.
import sharp from 'sharp'
import { mkdirSync } from 'node:fs'

const svg = (pad, rounded) => `
<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
  <rect width="512" height="512" rx="${rounded ? 96 : 0}" fill="#0f172a"/>
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

// Every size iOS may ask for, plus the legacy precomposed variant.
const APPLE_SIZES = [57, 60, 72, 76, 114, 120, 144, 152, 167, 180]
const MICROSOFT_SIZES = [70, 144, 150, 310]

const targets = [
  { file: 'public/pwa-192x192.png', size: 192, pad: 0, rounded: false },
  { file: 'public/pwa-512x512.png', size: 512, pad: 0, rounded: false },
  // maskable needs ~20% safe zone on all sides
  { file: 'public/pwa-maskable-512x512.png', size: 512, pad: 52, rounded: false },

  // apple-touch-icon: full-bleed square, no transparency, no pre-rounding.
  ...APPLE_SIZES.map(size => ({ file: `public/apple-icon-${size}x${size}.png`, size, pad: 0, rounded: true })),
  { file: 'public/apple-icon-precomposed.png', size: 180, pad: 0, rounded: true },

  // Windows tiles (browserconfig.xml)
  ...MICROSOFT_SIZES.map(size => ({ file: `public/ms-icon-${size}x${size}.png`, size, pad: 0, rounded: false })),
]

for (const t of targets) {
  // flatten onto the brand colour so iOS never sees an alpha channel
  const png = await sharp(Buffer.from(svg(t.pad, t.rounded)))
    .resize(t.size, t.size)
    .flatten({ background: '#0f172a' })
    .png()
    .toBuffer()
  await sharp(png).toFile(t.file)
  console.log('wrote', t.file)
}
