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

function nthWeekdayOfNextMonth(weekday: 0 | 4, n: number): string {
  const { year, month } = nextMonthParts()
  const d = new Date(Date.UTC(year, month - 1, 1))
  while (d.getUTCDay() !== weekday) d.setUTCDate(d.getUTCDate() + 1)
  d.setUTCDate(d.getUTCDate() + (n - 1) * 7)
  return d.toISOString().slice(0, 10)
}

async function gotoNextMonth(page: Page) {
  await page.getByRole('button', { name: 'Next month' }).click()
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
