export interface SlotDay {
  date: string
  startUtc: string
  endUtc: string
  state: 'Bookable' | 'Booked' | 'Combined' | 'Sunday' | 'Blocked' | 'Past' | 'Cutoff'
  canBook: boolean
  reason: string | null
  studentNames: string[]
  meetLink: string | null
}

export function formatLocal(utcIso: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('en-GB', {
    hour: '2-digit', minute: '2-digit', ...(timeZone ? { timeZone } : {}),
  }).format(new Date(utcIso))
}

export function formatDayLocal(utcIso: string, timeZone?: string): string {
  return new Intl.DateTimeFormat('en-GB', {
    weekday: 'short', day: 'numeric', month: 'short',
    hour: '2-digit', minute: '2-digit', ...(timeZone ? { timeZone } : {}),
  }).format(new Date(utcIso))
}

/** "19:30 your time · 20:30 Harare" — viewer zone automatic via Intl. */
export function dualTimeLabel(utcIso: string): string {
  return `${formatLocal(utcIso)} your time · ${formatLocal(utcIso, 'Africa/Harare')} Harare`
}
