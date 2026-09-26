import { expect, test, type Page } from '@playwright/test'

const API = 'http://localhost:8080'

async function login(page: Page, email: string) {
  await page.goto('/login')
  await page.getByPlaceholder('Your email').fill(email)
  await page.getByRole('button', { name: 'Send code' }).click()
  await page.getByPlaceholder('6-digit code').waitFor()
  const res = await page.request.get(`${API}/api/test/latest-code/${email}`)
  expect(res.ok()).toBeTruthy()
  const { code } = await res.json()
  await page.getByPlaceholder('6-digit code').fill(code)
  await page.getByRole('button', { name: 'Verify' }).click()
  await page.waitForURL('**/calendar')
}

// The calendar renders one month at a time (defaults to the current month), and
// journeys must book well ahead — so every test navigates to NEXT month and
// picks a weekday inside it. The nth weekday of next month is always ≥ 8 days
// out, safely past the 30-minute booking cutoff, and always visible in the
// displayed grid. UTC-only math keeps the yyyy-MM-dd string stable.
function nextMonthParts(): { year: number; month: number } {
  const now = new Date()
  return now.getMonth() === 11
    ? { year: now.getFullYear() + 1, month: 1 }
    : { year: now.getFullYear(), month: now.getMonth() + 2 }
}

// 0 = Sunday, 4 = Thursday, 5 = Friday. Every month has at least four of each
// weekday, so n <= 4 always lands inside the displayed month — which is what
// gotoNextMonth reaches. The guard turns "n too large" into a named error
// instead of a silently missing [data-date] locator.
function nthWeekdayOfNextMonth(weekday: 0 | 4 | 5, n: number): string {
  const { year, month } = nextMonthParts()
  const d = new Date(Date.UTC(year, month - 1, 1))
  while (d.getUTCDay() !== weekday) d.setUTCDate(d.getUTCDate() + 1)
  d.setUTCDate(d.getUTCDate() + (n - 1) * 7)
  if (d.getUTCMonth() !== month - 1)
    throw new Error(`nthWeekdayOfNextMonth(${weekday}, ${n}) fell outside next month — use n <= 4.`)
  return d.toISOString().slice(0, 10)
}

async function gotoNextMonth(page: Page) {
  await page.getByRole('button', { name: 'Next month' }).click()

}

// The disconnect modal's confirm button carries the same label as the card
// button that opens it, and the modal re-renders while the status fetch
// settles — so `.last()` could resolve onto an element Vue then swapped out
// ("element is not stable" / "detached from the DOM"). Scope to the dialog and
// wait for it to mount so the click target is stable.
async function openModalDialog(page: Page) {
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  return dialog
}

async function confirmDisconnect(page: Page) {
  const dialog = await openModalDialog(page)
  await dialog.getByRole('button', { name: 'Disconnect Google' }).click()
}

async function cancelDialog(page: Page) {
  const dialog = await openModalDialog(page)
  await dialog.getByRole('button', { name: 'Cancel' }).click()
}

test('student logs in, books, sees name on shared calendar', async ({ page }) => {
  await login(page, 'studenta@example.com')
  await gotoNextMonth(page)
  const date = nthWeekdayOfNextMonth(4, 1) // 1st Thursday
  const cell = page.locator(`[data-date="${date}"]`)
  await cell.click()
  await page.getByRole('button', { name: 'Confirm booking' }).click()
  await expect(cell).toContainText('Student A', { timeout: 10_000 })
  // Booked cells intentionally stay clickable — that's how the second
  // student joins for a combined lesson
  await expect(cell).toHaveClass(/cursor-pointer/)
})

test('second student same day shows combined', async ({ page }) => {
  await login(page, 'studenta@example.com')
  await gotoNextMonth(page)
  const date = nthWeekdayOfNextMonth(4, 2) // 2nd Thursday
  await page.locator(`[data-date="${date}"]`).click()
  await page.getByRole('button', { name: 'Confirm booking' }).click()
  await expect(page.locator(`[data-date="${date}"]`)).toContainText('Student A')

  // Booked days stay bookable on purpose — that's how the second student
  // joins and the day becomes a combined lesson
  const page2 = await page.context().newPage()
  await login(page2, 'studentb@example.com')
  await gotoNextMonth(page2)
  const cell2 = page2.locator(`[data-date="${date}"]`)
  await cell2.click()
  await page2.getByRole('button', { name: 'Confirm booking' }).click()
  await expect(cell2).toContainText('combined')
  await expect(cell2).toContainText('Student B')
})

test('cancel and reschedule from my lessons', async ({ page }) => {
  await login(page, 'studentb@example.com')
  await gotoNextMonth(page)
  const date = nthWeekdayOfNextMonth(4, 3) // 3rd Thursday
  await page.locator(`[data-date="${date}"]`).click()
  await page.getByRole('button', { name: 'Confirm booking' }).click()
  await expect(page.locator(`[data-date="${date}"]`)).toContainText('Student B')

  await page.getByRole('link', { name: 'My Lessons' }).click()
  await page.waitForURL('**/mine')

  // RESCHEDULE: the modal lists next month's bookable days — pick the first.
  // A move keeps the card count constant but adds a "moved from" marker.
  await page.getByRole('button', { name: 'Reschedule' }).last().click()
  await page.locator('button[data-date]').first().click()
  await expect(page.getByText('Moved!')).toBeVisible()
  // "Moved!" renders before the list reload finishes — gate on the NEW DOM
  await expect(page.getByText('moved from')).toBeVisible()

  // CANCEL everything studentb holds. The settle wait avoids sampling the
  // button count mid-re-render (a 0 there would skip the whole loop); the
  // toHaveCount after each click auto-waits through the reload.
  for (let i = 0; i < 6; i++) {
    await page.waitForTimeout(200)
    const n = await page.getByRole('button', { name: 'Cancel' }).count()
    if (n === 0) break
    await page.getByRole('button', { name: 'Cancel' }).first().click()
    await expect(page.getByRole('button', { name: 'Cancel' })).toHaveCount(n - 1)
  }
  await expect(page.getByRole('button', { name: 'Cancel' })).toHaveCount(0)
  await expect(page.getByText('moved from')).toHaveCount(0)
  await expect(page.getByText('No upcoming lessons')).toBeVisible()
})

test('admin calendar renders the same shared grid as the calendar page', async ({ page }) => {
  await login(page, 'teacher@example.com')
  await gotoNextMonth(page)

  const { year, month } = nextMonthParts()
  const label = new Date(Date.UTC(year, month - 1, 1))
    .toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })

  const gridShape = async () => {
    const grid = page.getByRole('region', { name: 'Month calendar' })
    await expect(grid.locator('h1, h2')).toHaveText(label) // month finished loading
    const frame = await grid.locator('[data-date]').first().evaluate(cell => {
      const cells = Array.from(cell.parentElement!.children)
      return {
        weekdayHeaders: cells.slice(0, 7).map(c => c.textContent!.trim()),
        leadingBlanks: cells.slice(0, cells.findIndex(c => c.hasAttribute('data-date')))
          .filter(c => !c.hasAttribute('data-date') && c.textContent === '').length,
        cellHeight: cell.className.match(/min-h-\d+/)![0],
        cellPadding: cell.className.match(/rounded-lg/)![0],
      }
    })
    return { label, ...frame }
  }

  const calendar = await gridShape()
  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await gotoNextMonth(page)
  const admin = await gridShape()

  // same component, so same label, same Monday-first offset, same cell size
  expect(admin).toEqual(calendar)
  expect(calendar.weekdayHeaders).toEqual(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'])
  expect(calendar.leadingBlanks).toBe((new Date(Date.UTC(year, month - 1, 1)).getUTCDay() + 6) % 7)
})

test('admin sees Connect Google button when Google is not configured', async ({ page }) => {
  await login(page, 'teacher@example.com')
  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await expect(page.getByRole('button', { name: 'Connect Google', exact: true })).toBeVisible()
  const statusRes = await page.request.get('/api/admin/google/status');
  expect(statusRes.ok()).toBeTruthy();
  const status = await statusRes.json();
  expect(status.connected).toBe(false);
})

test('admin blocks a day; student sees it unbookable; sunday never bookable', async ({ page }) => {
  await login(page, 'teacher@example.com')
  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await gotoNextMonth(page)
  const date = nthWeekdayOfNextMonth(4, 4) // 4th Thursday
  await page.locator(`[data-date="${date}"]`).click()
  await expect(page.locator(`[data-date="${date}"]`)).toContainText('blocked')

  const page2 = await page.context().newPage()
  await login(page2, 'studenta@example.com')
  await gotoNextMonth(page2)
  const cell = page2.locator(`[data-date="${date}"]`)
  await expect(cell).toHaveClass(/bg-error/) // Blocked styling on the shared calendar
  await expect(cell).not.toHaveClass(/cursor-pointer/)

  // a Sunday in the displayed month is never bookable
  const sunday = nthWeekdayOfNextMonth(0, 1) // 1st Sunday
  await expect(page2.locator(`[data-date="${sunday}"]`)).not.toHaveClass(/cursor-pointer/)
})

test('admin disconnects Google and returns to Connect state', async ({ page }) => {
  await login(page, 'teacher@example.com')
  let connected = true
  let deleteRequests = 0

  await page.route('**/api/admin/google/status', route => route.fulfill({
    json: { connected, needsReconnect: false },
  }))
  await page.route('**/api/admin/google', async route => {
    if (route.request().method() !== 'DELETE') return route.fallback()
    deleteRequests += 1
    connected = false
    await route.fulfill({ json: { remoteRevoked: true } })
  })

  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await expect(page.getByText('calendar.events.owned')).toBeVisible()
  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await expect(page.getByText('Disconnect Google Calendar?')).toBeVisible()
  await cancelDialog(page)
  expect(deleteRequests).toBe(0)

  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await confirmDisconnect(page)

  await expect(page.getByText('Google disconnected. New bookings use the fallback link.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Connect Google', exact: true })).toBeVisible()
  expect(deleteRequests).toBe(1)
})

test('admin disconnect warns when local access ends before Google confirms revocation', async ({ page }) => {
  await login(page, 'teacher@example.com')
  // The status mock must follow the DELETE, or the card stays "connected" and
  // the Connect button never renders. Pinned to a static connected:true this
  // assertion was unreachable and only "passed" because getByRole matches
  // names as substrings, so "Connect Google" matched "Disconnect Google".
  let connected = true

  await page.route('**/api/admin/google/status', route => route.fulfill({
    json: { connected, needsReconnect: false },
  }))
  await page.route('**/api/admin/google', async route => {
    if (route.request().method() !== 'DELETE') return route.fallback()
    connected = false
    await route.fulfill({ json: { remoteRevoked: false } })
  })

  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await confirmDisconnect(page)

  await expect(page.getByText(/Google did not confirm revocation/)).toBeVisible()
  await expect(page.getByText(/Google Account Settings/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Connect Google', exact: true })).toBeVisible()
})

test('admin keeps connected state when local disconnect fails', async ({ page }) => {
  await login(page, 'teacher@example.com')
  await page.route('**/api/admin/google/status', route => route.fulfill({
    json: { connected: true, needsReconnect: false },
  }))
  await page.route('**/api/admin/google', route => route.fulfill({
    status: 500,
    json: { error: 'Could not disconnect Google.' },
  }))

  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await confirmDisconnect(page)
  await expect(page.getByText('Disconnect Google Calendar?')).toBeVisible()
  await cancelDialog(page)

  await expect(page.getByText('Could not disconnect Google.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Disconnect Google' })).toBeVisible()
  await expect(page.getByText('Connected')).toBeVisible()
})

test('admin disconnects reconnect-required Google token', async ({ page }) => {
  await login(page, 'teacher@example.com')
  let needsReconnect = true

  await page.route('**/api/admin/google/status', route => route.fulfill({
    json: { connected: false, needsReconnect },
  }))
  await page.route('**/api/admin/google', async route => {
    if (route.request().method() !== 'DELETE') return route.fallback()
    needsReconnect = false
    await route.fulfill({ json: { remoteRevoked: true } })
  })

  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')
  await expect(page.getByText('Reconnect required').first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Reconnect Google' })).toBeVisible()
  await page.getByRole('button', { name: 'Disconnect Google' }).click()
  await confirmDisconnect(page)

  await expect(page.getByRole('button', { name: 'Connect Google', exact: true })).toBeVisible()
  expect(needsReconnect).toBe(false)
})

test('privacy policy discloses Google data and user controls', async ({ page }) => {
  await page.goto('/privacy')

  await expect(page.getByText('Last updated: 25 September 2026')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Google Calendar' })).toBeVisible()
  await expect(page.getByText(/calendar.events.owned/)).toBeVisible()
  await expect(page.getByText(/AES-256-GCM/)).toBeVisible()
  await expect(page.getByText(/revoked-token record/)).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Disconnect and deletion' })).toBeVisible()
  await expect(page.getByText(/Google Account Settings/)).toBeVisible()
})

test('login shows Google option and preserves email fallback', async ({ page }) => {
  await page.goto('/login')

  await expect(page.getByRole('button', { name: 'Continue with Google' })).toBeVisible()
  await expect(page.getByPlaceholder('Your email')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send code' })).toBeVisible()
})

test('login shows sanitized Google callback error', async ({ page }) => {
  await page.goto('/login?google=error')

  await expect(page.getByText('Google sign-in failed. Use the email code below or try again.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Continue with Google' })).toBeVisible()
})

// The headline fix: the admin replaces the "Student A" placeholder with a real
// name and that name is what the shared calendar shows. Renames back at the end
// so the suite stays order-independent on the shared database.
test('admin renames a student and the real name shows on the shared calendar', async ({ page }) => {
  await login(page, 'teacher@example.com')
  await page.getByRole('link', { name: 'Admin' }).click()
  await page.waitForURL('**/admin')

  const row = page.locator('[data-user-email="studenta@example.com"]')
  await row.getByPlaceholder('Full name').fill('Tafadzwa Mugweni')
  await row.getByRole('button', { name: 'Save' }).click()
  await expect(row.getByPlaceholder('Full name')).toHaveValue('Tafadzwa Mugweni')

  // 1st FRIDAY, not a 5th Thursday: a month can have only four Thursdays, which
  // would push the 5th into the following month where gotoNextMonth never lands.
  // Every month has at least four Fridays, and the earlier tests take Thursdays.
  const date = nthWeekdayOfNextMonth(5, 1)
  const student = await page.context().newPage()
  await login(student, 'studenta@example.com')
  await gotoNextMonth(student)
  await student.locator(`[data-date="${date}"]`).click()
  await student.getByRole('button', { name: 'Confirm booking' }).click()
  await expect(student.locator(`[data-date="${date}"]`)).toContainText('Tafadzwa Mugweni')
  await expect(student.locator(`[data-date="${date}"]`)).not.toContainText('Student A')

  // restore the placeholder so the shared DB is left as the suite expects
  await row.getByPlaceholder('Full name').fill('Student A')
  await row.getByRole('button', { name: 'Save' }).click()
  await expect(row.getByPlaceholder('Full name')).toHaveValue('Student A')

  await student.locator(`[data-date="${date}"]`).click()
  await student.getByRole('button', { name: 'Cancel' }).click()
})
