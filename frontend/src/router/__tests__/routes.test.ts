import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { routes } from '../index'

// A stale bookmark, a mistyped URL, or a route renamed out from under a link
// must not strand the user on a blank page. The catch-all is the last route,
// so the guard below is as much about what it must NOT shadow.
const router = createRouter({ history: createMemoryHistory(), routes })

describe('unknown routes', () => {
  it('redirects an unknown path home', async () => {
    await router.push('/this-route-does-not-exist')
    expect(router.currentRoute.value.path).toBe('/')
  })

  it('redirects an unknown nested path home', async () => {
    await router.push('/deeply/nested/nonsense')
    expect(router.currentRoute.value.path).toBe('/')
  })

  it('redirects a path that only looks like a real one', async () => {
    await router.push('/calendar/2026-09-30')
    expect(router.currentRoute.value.path).toBe('/')
  })
})

describe('the catch-all does not shadow real routes', () => {
  it.each([
    '/',
    '/login',
    '/calendar',
    '/mine',
    '/lessons',
    '/admin',
    '/privacy',
    '/terms',
  ])('keeps %s resolving to itself', (path) => {
    expect(router.resolve(path).path).toBe(path)
  })

  // resolve().path echoes the requested path back even when the catch-all is
  // what actually matched, so the assertion above passes for a route that was
  // never registered. The matched record is what tells the two apart.
  it.each(['/calendar', '/mine', '/lessons', '/admin'])('%s is a real route, not the catch-all', (path) => {
    expect(router.resolve(path).matched.map(r => r.path)).toEqual([path])
  })
})
