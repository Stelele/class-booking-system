import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import MonthCalendar from '../MonthCalendar.vue'
import type { SlotDay } from '../../composables/useTime'

function day(partial: Partial<SlotDay> & { date: string }): SlotDay {
  return {
    startUtc: `${partial.date}T18:00:00Z`,
    endUtc: `${partial.date}T19:00:00Z`,
    state: 'Bookable',
    canBook: true,
    reason: null,
    studentNames: [],
    meetLink: null,
    ...partial,
  }
}

const october2026 = [
  day({ date: '2026-10-01', state: 'Bookable' }),
  day({ date: '2026-10-02', state: 'Booked', canBook: false, studentNames: ['Student A'] }),
  day({ date: '2026-10-03', state: 'Blocked', canBook: false, reason: 'unavailable' }),
  day({ date: '2026-10-04', state: 'Sunday', canBook: false }),
]

// real Nuxt UI components are resolved at build time, so they render for real;
// the router keeps UButton's internal Link happy
const global = {
  plugins: [[createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: { template: '<div />' } }] })]],
}

function mountGrid(props: Record<string, unknown>, slots: Record<string, unknown> = {}) {
  return mount(MonthCalendar, {
    props: { year: 2026, month: 10, days: october2026, loading: false, ...props },
    slots,
    global,
  })
}

describe('MonthCalendar', () => {
  it('labels the month the same way on every page', () => {
    expect(mountGrid().text()).toContain('October 2026')
  })

  it('drops to a second-level heading when the page already has an h1', () => {
    const asPageHeading = mountGrid()
    expect(asPageHeading.find('h1').text()).toBe('October 2026')

    const nested = mountGrid({ headingLevel: 2 })
    expect(nested.find('h2').text()).toBe('October 2026')
    expect(nested.find('h1').exists()).toBe(false)
  })

  it('exposes the grid as a labelled landmark', () => {
    const region = mountGrid().find('[role="region"]')
    expect(region.attributes('aria-label')).toBe('Month calendar')
  })

  it('starts the grid on Monday with leading blanks for the weekday offset', () => {
    const grid = mountGrid().find('[data-date="2026-10-01"]').element.parentElement!
    const cells = Array.from(grid.children)
    const firstDayIndex = cells.findIndex(c => c.hasAttribute('data-date'))
    expect(cells.slice(0, 7).map(c => c.textContent?.trim())).toEqual(
      ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'],
    )
    expect(firstDayIndex).toBe(10)
    expect(cells.slice(7, 10).every(c => !c.hasAttribute('data-date') && c.textContent === '')).toBe(true)
  })

  it('keeps days in date order', () => {
    const dates = mountGrid().findAll('[data-date]').map(c => c.attributes('data-date'))
    expect(dates).toEqual(['2026-10-01', '2026-10-02', '2026-10-03', '2026-10-04'])
  })

  it('renders a day as a button only when the parent says it is interactive', () => {
    const wrapper = mountGrid({ interactive: (d: SlotDay) => d.canBook })
    expect(wrapper.find('[data-date="2026-10-01"]').element.tagName).toBe('BUTTON')
    expect(wrapper.find('[data-date="2026-10-01"]').classes()).toContain('cursor-pointer')
    expect(wrapper.find('[data-date="2026-10-04"]').element.tagName).toBe('DIV')
    expect(wrapper.find('[data-date="2026-10-04"]').classes()).not.toContain('cursor-pointer')
  })

  it('applies the same colour treatment per state regardless of page', () => {
    const wrapper = mountGrid()
    expect(wrapper.find('[data-date="2026-10-01"]').classes()).toEqual(
      expect.arrayContaining(['bg-success/10', 'ring-success/15']),
    )
    expect(wrapper.find('[data-date="2026-10-03"]').classes()).toEqual(
      expect.arrayContaining(['bg-error/10', 'text-error']),
    )
  })

  it('emits select with the clicked day', async () => {
    const wrapper = mountGrid({ interactive: () => true })
    await wrapper.find('[data-date="2026-10-03"]').trigger('click')
    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({ date: '2026-10-03' })
  })

  it('emits shift for month navigation', async () => {
    const wrapper = mountGrid()
    await wrapper.find('[aria-label="Previous month"]').trigger('click')
    await wrapper.find('[aria-label="Next month"]').trigger('click')
    expect(wrapper.emitted('shift')).toEqual([[-1], [1]])
  })

  it('delegates per-cell detail to the parent slot, scoped to the day', () => {
    const wrapper = mountGrid({}, { cell: '<span class="detail">{{ params.day.state }}</span>' })
    expect(wrapper.find('[data-date="2026-10-02"]').find('.detail').text()).toBe('Booked')
    expect(wrapper.find('[data-date="2026-10-03"]').find('.detail').text()).toBe('Blocked')
  })

  it('shows a skeleton while loading and the error alert on failure', () => {
    const loading = mountGrid({ loading: true })
    expect(loading.findAll('.animate-pulse')).toHaveLength(35)
    expect(loading.findAll('.animate-pulse')[0].classes()).toContain('min-h-20')
    expect(loading.findAll('[data-date]')).toHaveLength(0)

    const failed = mountGrid({ error: 'boom' })
    expect(failed.text()).toContain('boom')
    expect(failed.text()).toContain("Couldn't load the calendar")
  })
})
