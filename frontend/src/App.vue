<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import type { NavigationMenuItem } from '@nuxt/ui'
import { user, logout } from './composables/useAuth'

const route = useRoute()

const navItems = computed<NavigationMenuItem[]>(() => {
  const items: NavigationMenuItem[] = [
    { label: 'Calendar', to: '/calendar', active: route.path === '/calendar' },
    { label: 'My Lessons', to: '/mine', active: route.path === '/mine' },
  ]
  if (user.value?.role === 'Admin')
    items.push({ label: 'Admin', to: '/admin', active: route.path === '/admin' })
  return items
})

const footerItems: NavigationMenuItem[] = [
  { label: 'Privacy', to: '/privacy' },
  { label: 'Terms', to: '/terms' },
]
</script>

<template>
  <UApp>
    <div class="min-h-screen flex flex-col bg-default">
      <UHeader title="Lesson Booking" to="/calendar">
        <UNavigationMenu v-if="user" :items="navItems" />

        <template #right>
          <template v-if="user">
            <UBadge color="neutral" variant="subtle" icon="i-lucide-user">{{ user.name }}</UBadge>
            <UButton color="neutral" variant="ghost" @click="logout()">Log out</UButton>
          </template>
          <UButton v-else to="/login" icon="i-lucide-log-in">Log in</UButton>
        </template>

        <template #body>
          <UNavigationMenu v-if="user" :items="navItems" orientation="vertical" />
        </template>
      </UHeader>

      <main class="flex-1">
        <UContainer class="py-6">
          <RouterView />
        </UContainer>
      </main>

      <UFooter>
        <template #left>
          <p class="text-muted text-sm">Evening programming lessons · 20:30 Harare</p>
        </template>
        <UNavigationMenu :items="footerItems" variant="link" />
      </UFooter>
    </div>
  </UApp>
</template>
