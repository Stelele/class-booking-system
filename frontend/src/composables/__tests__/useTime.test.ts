import { describe, expect, it } from 'vitest'
import { formatLocal, dualTimeLabel } from '../useTime'

describe('useTime', () => {
  it('shows viewer-zone time — BST example', () => {
    expect(formatLocal('2026-09-22T18:30:00Z', 'Europe/London')).toBe('19:30')
    expect(formatLocal('2026-09-22T18:30:00Z', 'Africa/Harare')).toBe('20:30')
  })
  it('winter — GMT example', () => {
    expect(formatLocal('2026-12-03T18:30:00Z', 'Europe/London')).toBe('18:30')
  })
  it('dual label contains both zones', () => {
    const label = dualTimeLabel('2026-09-22T18:30:00Z')
    expect(label).toContain('20:30 Harare')
    expect(label).toContain('your time')
  })
})
