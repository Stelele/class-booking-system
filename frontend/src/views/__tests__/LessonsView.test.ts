import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises, type GlobalMountOptions, type VueWrapper } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import LessonsView from '../LessonsView.vue'

const api = vi.hoisted(() => vi.fn())

vi.mock('../../composables/useApi', async () => {
  const actual = await vi.importActual<typeof import('../../composables/useApi')>(
    '../../composables/useApi',
  )
  return { ...actual, api }
})

function lesson(partial: Record<string, unknown> = {}) {
  return {
    date: '2026-11-19',
    startUtc: '2026-11-19T18:30:00Z',
    endUtc: '2026-11-19T20:30:00Z',
    localTime: '20:30',
    students: [{ bookingId: 'b-1', name: 'Thandi' }],
    meetLink: 'https://meet.google.com/abc-defg-hij',
    isCombined: false,
    canCancel: true,
    canReschedule: true,
    ...partial,
  }
}

// UButton renders a Link, which needs a router present. Typed as
// GlobalMountOptions so the plugin array keeps its tuple shape.
const global: GlobalMountOptions = {
  plugins: [createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  })],
}

// Route by path, not by call order: mockResolvedValueOnce is a queue, so two
// overlapping defaults hand whichever reply was queued first to whichever call
// happens to arrive first — which silently returns slot days as lessons.
function route(lessons: unknown[], slots: unknown[] = []) {
  // path arrives as the first argument; a rest arg keeps the signature honest
  // without depending on how many options a call site passes
  api.mockImplementation((...args: unknown[]) => {
    const path = String(args[0] ?? '')
    return Promise.resolve(path.startsWith('/admin/bookings') ? lessons : slots)
  })
}

// The picker now asks for two months at once, so the stub has to answer each
// query separately or every day lands under one heading.
function routeByMonth(
  lessons: unknown[],
  byQuery: Record<string, unknown[]>,
) {
  api.mockImplementation((...args: unknown[]) => {
    const path = String(args[0] ?? '')
    if (path.startsWith('/admin/bookings')) return Promise.resolve(lessons)
    const m = path.match(/year=(\d{4})&month=(\d{1,2})/)
    const key = m ? `${m[1]}-${Number(m[2])}` : path
    return Promise.resolve(byQuery[key] ?? [])
  })
}

async function mountView(lessons: unknown[], slots: unknown[] = []) {
  route(lessons, slots)
  const wrapper = mount(LessonsView, { global })
  mounted.push(wrapper)
  await flushPromises()
  return wrapper
}

// Every mounted wrapper is torn down after its test. A still-mounted component
// from an earlier test keeps its api mock alive, so a rejection set up by a
// later test is picked up by the stale instance and surfaces as an unhandled
// rejection attributed to the wrong line.
const mounted: VueWrapper[] = []

beforeEach(() => api.mockReset())
afterEach(() => {
  mounted.splice(0).forEach(w => w.unmount())
})

// 20:30 Harare is 18:30 UTC. Between 22:30Z and midnight UTC the Harare date is
// already tomorrow, so a lesson on "today in Harare" is "tomorrow" in UTC. Any
// test that asserts on that lesson has to run under a clock inside that window,
// or a UTC-derived "today" agrees with the Harare one and proves nothing.
const HARARE_MIDNIGHT_UTC = new Date('2026-10-05T22:30:00Z') // 00:30 on 10-06 Harare

// Day helpers for the month picker, which groups by yyyy-mm prefix. Fixed dates
// like '2026-12-03' belong to neither the current nor the next month and would
// be filtered out, so these always resolve relative to today.
const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

const slotRow = (date: string) => ({
  date,
  startUtc: `${date}T18:30:00Z`,
  endUtc: `${date}T20:30:00Z`,
  state: 'Bookable',
  canBook: true,
  reason: null,
  studentNames: [],
  meetLink: null,
})

// UModal teleports its content to document.body, so wrapper.find() never sees
// the dialog — query the document instead. Scoping to the dialog also matters
// because the confirm action shares its label with the card buttons that open
// it, and clicking the wrong one would only re-open the dialog.
function dialog(): HTMLElement {
  const el = document.querySelector<HTMLElement>('[role="dialog"]')
  expect(el, 'an open dialog').not.toBeNull()
  return el!
}

function dialogButton(label: string): HTMLButtonElement {
  const match = [...dialog().querySelectorAll('button')]
    .find(b => b.textContent?.trim() === label)
  expect(match, `"${label}" in the dialog`).toBeDefined()
  return match as HTMLButtonElement
}

async function confirmCancelDialog() {
  dialogButton('Cancel lesson').click()
  await flushPromises()
}

describe('lessons page', () => {
  it('shows each student and a join link pointing at the Meet room', async () => {
    const wrapper = await mountView([lesson()])

    expect(wrapper.text()).toContain('Thandi')
    expect(wrapper.text()).toContain('Harare')
    const join = wrapper.find('[data-join-date="2026-11-19"]')
    expect(join.exists()).toBe(true)
    expect(join.attributes('href')).toBe('https://meet.google.com/abc-defg-hij')
  })

  it('warns instead of offering a dead link when there is no Meet room', async () => {
    const wrapper = await mountView([lesson({ meetLink: null })])

    expect(wrapper.find('[data-join-date]').exists()).toBe(false)
    expect(wrapper.text()).toContain('no link yet')
    expect(wrapper.text()).toContain('no join link yet')
  })

  // The teacher has to know which student they are cancelling: a combined
  // lesson holds two bookings and cancelling the day must not take both.
  it('offers one cancel control per student, naming each one', async () => {
    const wrapper = await mountView([lesson({
      isCombined: true,
      students: [
        { bookingId: 'b-1', name: 'Thandi' },
        { bookingId: 'b-2', name: 'Rudo' },
      ],
    })])

    expect(wrapper.find('[data-cancel-student="Thandi"]').exists()).toBe(true)
    expect(wrapper.find('[data-cancel-student="Rudo"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-cancel-student]')).toHaveLength(2)
  })

  it('cancels only the chosen student, by that student booking id', async () => {
    const wrapper = await mountView([lesson({
      isCombined: true,
      students: [
        { bookingId: 'b-1', name: 'Thandi' },
        { bookingId: 'b-2', name: 'Rudo' },
      ],
    })])

    await wrapper.find('[data-cancel-student="Thandi"]').trigger('click')
    await flushPromises()
    // the dialog names the student, so it is unambiguous on a combined lesson
    expect(dialog().textContent).toContain("Cancel Thandi's lesson?")

    await confirmCancelDialog()
    await flushPromises()

    expect(api).toHaveBeenCalledWith('/bookings/b-1', { method: 'DELETE' })
    expect(api).not.toHaveBeenCalledWith('/bookings/b-2', { method: 'DELETE' })
  })

  it('moves a student booking to the chosen day', async () => {
    const nextMonth = new Date(new Date().getFullYear(), new Date().getMonth() + 1, 3)
    const wrapper = await mountView([lesson()], [slotRow(iso(nextMonth))])

    await wrapper.find('[data-reschedule-student="Thandi"]').trigger('click')
    await flushPromises()
    expect(api).toHaveBeenCalledWith(expect.stringContaining('/slots?year='))

    const target = dialog().querySelector<HTMLElement>('[data-date]')
    expect(target, 'a bookable day inside the move dialog').not.toBeNull()
    const newDate = target!.getAttribute('data-date')
    target!.click()
    await flushPromises()

    const move = api.mock.calls.find(c => typeof c[0] === 'string' && c[0].includes('/reschedule'))
    expect(move).toBeDefined()
    expect(move![0]).toBe('/bookings/b-1/reschedule')
    expect(move![1]).toMatchObject({
      method: 'POST',
      body: JSON.stringify({ newDate }),
    })
  })

  it('reports a failed cancel instead of silently reloading', async () => {
    api.mockImplementation((path: string, init?: RequestInit) =>
      init?.method === 'DELETE'
        ? Promise.reject(new Error('Nope'))
        : Promise.resolve([lesson()]))

    const wrapper = mount(LessonsView, { global })
    mounted.push(wrapper)
    await flushPromises()

    await wrapper.find('[data-cancel-student="Thandi"]').trigger('click')
    await flushPromises()
    await confirmCancelDialog()
    await flushPromises()

    // a plain Error is not an ApiError, so the generic fallback is what shows —
    // the point is that a failure is reported at all, not swallowed
    expect(wrapper.text()).toContain('Could not cancel that lesson')
  })

  // The teacher schedules around real availability, so both this month and next
  // must be offered — next month alone hid free days later this month.
  it('offers bookable days from this month and next, grouped by month', async () => {
    const now = new Date()
    const thisKey = `${now.getFullYear()}-${now.getMonth() + 1}`
    const thisMonth = new Date(now.getFullYear(), now.getMonth(), 10)
    const nextDate = new Date(now.getFullYear(), now.getMonth() + 1, 3)
    const nextKey = `${nextDate.getFullYear()}-${nextDate.getMonth() + 1}`

    routeByMonth([lesson()], {
      [thisKey]: [slotRow(iso(thisMonth))],
      [nextKey]: [slotRow(iso(nextDate))],
    })
    const wrapper = mount(LessonsView, { global })
    await flushPromises()

    await wrapper.find('[data-reschedule-student="Thandi"]').trigger('click')
    await flushPromises()

    // both months were requested
    expect(api.mock.calls.filter(c => String(c[0]).includes('/slots?')).length).toBe(2)
    const picker = dialog()
    // a day from each month is offered, under its own heading
    expect(picker.querySelector(`[data-date="${iso(thisMonth)}"]`)).not.toBeNull()
    expect(picker.querySelector(`[data-date="${iso(nextDate)}"]`)).not.toBeNull()
    const headings = [...picker.querySelectorAll('p')]
      .map(p => p.textContent?.trim() ?? '')
      .filter(t => /^[A-Z][a-z]+$/.test(t))
    expect(headings.length).toBe(2)
  })

  // 20:30 Harare is 18:30 UTC. A card header built from the browser's own clock
  // shows a different time from the "Harare" label printed beside it, so the
  // header must be rendered in the lesson's zone.
  it('formats the header in lesson time, not the browser zone', async () => {
    const startUtc = '2026-11-19T18:30:00Z' // 20:30 Africa/Harare
    const wrapper = await mountView([lesson({ startUtc })])

    const harare = new Intl.DateTimeFormat('en-GB', {
      weekday: 'short', day: 'numeric', month: 'short',
      hour: '2-digit', minute: '2-digit', timeZone: 'Africa/Harare',
    }).format(new Date(startUtc))
    expect(wrapper.text()).toContain(harare)
  })

  // "Tonight" keys off the lesson date, which is a Harare date. Deriving today
  // from toISOString gives the UTC date, which names the wrong day between
  // midnight and 02:00 Harare.
  it('labels tonight\'s lesson Tonight using the lesson zone date', async () => {
    vi.useFakeTimers()
    try {
      vi.setSystemTime(HARARE_MIDNIGHT_UTC)
      // Harare says 10-06; UTC still says 10-05
      const harareToday = '2026-10-06'
      expect(new Date().toISOString().slice(0, 10)).not.toBe(harareToday)

      const wrapper = await mountView([lesson({ date: harareToday })])
      expect(wrapper.text()).toContain('Tonight')
    }
    finally {
      vi.useRealTimers()
    }
  })

  it('shows an empty state when nothing is booked', async () => {
    const wrapper = await mountView([])

    expect(wrapper.text()).toContain('No upcoming lessons')
    expect(wrapper.find('[data-cancel-student]').exists()).toBe(false)
  })

  it('surfaces a load failure', async () => {
    // Reject only the first call. A mock that throws for every call keeps
    // rejecting on behalf of components still mounted from earlier tests, and
    // vitest reports that as a failure of whichever test is running then —
    // which is why this asserts on one deliberate failure, not a standing one.
    let calls = 0
    api.mockImplementation(async () => {
      if (calls++ === 0) throw new Error('Load failed')
      return []
    })

    const wrapper = mount(LessonsView, { global })
    mounted.push(wrapper)
    await flushPromises()

    expect(wrapper.text()).toContain('Load failed')
  })
})