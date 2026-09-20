import { createRouter, createWebHistory } from 'vue-router'
import { user } from '../composables/useAuth'

declare module 'vue-router' {
  interface RouteMeta { auth?: boolean; admin?: boolean }
}

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: () => import('../views/HomeView.vue') },
    { path: '/login', component: () => import('../views/LoginView.vue') },
    { path: '/calendar', component: () => import('../views/CalendarView.vue'), meta: { auth: true } },
    { path: '/mine', component: () => import('../views/MyLessonsView.vue'), meta: { auth: true } },
    { path: '/admin', component: () => import('../views/AdminView.vue'), meta: { auth: true, admin: true } },
    { path: '/privacy', component: () => import('../views/PrivacyView.vue') },
    { path: '/terms', component: () => import('../views/TermsView.vue') },
  ],
})

router.beforeEach((to) => {
  if (to.meta.auth && !user.value) return '/login'
  if (to.meta.admin && user.value?.role !== 'Admin') return '/calendar'
})

export default router
