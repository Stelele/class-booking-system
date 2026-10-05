import { createRouter, createWebHistory } from 'vue-router'
import { user } from '../composables/useAuth'
import { attemptRecovery, isChunkLoadError } from './chunkRecovery'

declare module 'vue-router' {
  interface RouteMeta { auth?: boolean; admin?: boolean }
}

export const routes = [
  { path: '/', component: () => import('../views/HomeView.vue') },
  { path: '/login', component: () => import('../views/LoginView.vue') },
  { path: '/calendar', component: () => import('../views/CalendarView.vue'), meta: { auth: true } },
  { path: '/mine', component: () => import('../views/MyLessonsView.vue'), meta: { auth: true } },
  // Teacher only: the teacher has no bookings of their own, so this is the only
  // page that shows them a booked day and lets them join it.
  { path: '/lessons', component: () => import('../views/LessonsView.vue'), meta: { auth: true, admin: true } },
  { path: '/admin', component: () => import('../views/AdminView.vue'), meta: { auth: true, admin: true } },
  { path: '/privacy', component: () => import('../views/PrivacyView.vue') },
  { path: '/terms', component: () => import('../views/TermsView.vue') },
  // Last, so it cannot shadow a real route. Without it an unmatched path
  // renders the layout around an empty <main> with no explanation — and the
  // PWA's start_url (/calendar) would open blank forever if that route were
  // ever renamed, with no way back but reinstalling.
  { path: '/:pathMatch(.*)*', redirect: '/' },
]

const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach((to) => {
  if (to.meta.auth && !user.value) return '/login'
  if (to.meta.admin && user.value?.role !== 'Admin') return '/calendar'
})

// A lazy route import is the only place version skew surfaces: the entry
// chunk loaded fine at page load, then the chunk for the route the user just
// opened is gone. Reload onto the current build rather than leaving them on a
// dead route — see chunkRecovery.ts for why this fires at most once.
router.onError((error) => {
  if (isChunkLoadError(error) && attemptRecovery()) return
  // Nothing left to try: either it is not version skew, or the reload did not
  // fix it. Surfacing it beats silently rendering a blank route.
  console.error('[router] navigation failed', error)
})

export default router
