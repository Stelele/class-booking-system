import { describe, expect, it, vi } from 'vitest'
import { formatLocal, dualTimeLabel, todayInZone } from '../useTime'

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

  // A UTC "today" names the wrong day between midnight and the zone's offset.
  // Harare is UTC+2, so the window that matters is 00:00-02:00 local.
  it('today in a zone is that zone date, not the UTC one', () => {
    vi.useFakeTimers()
    try {
      // 2026-10-05T22:30Z is already 2026-10-06 00:30 in Harare
      vi.setSystemTime(new Date('2026-10-05T22:30:00Z'))
      expect(todayInZone('Africa/Harare')).toBe('2026-10-06')
      expect(todayInZone('Europe/London')).toBe('2026-10-05')
      expect(new Date().toISOString().slice(0, 10)).toBe('2026-10-05') // the trap
    }
    finally {
      vi.useRealTimers()
    }
  })
  it('today in a zone agrees with UTC when no offset crosses midnight', () => {
    vi.useFakeTimers()
    try {
      vi.setSystemTime(new Date('2026-10-05T12:00:00Z'))
      expect(todayInZone('Africa/Harare')).toBe('2026-10-05')
    }
    finally {
      vi.useRealTimers()
    }
  })
})
