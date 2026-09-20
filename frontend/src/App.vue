<template>
  <UApp>
    <div class="min-h-screen flex flex-col">
      <header class="border-b border-gray-200 bg-white">
        <nav class="mx-auto flex max-w-4xl items-center gap-4 p-4">
          <RouterLink to="/calendar" class="font-bold">Lesson Booking</RouterLink>
          <template v-if="user">
            <RouterLink to="/calendar">Calendar</RouterLink>
            <RouterLink to="/mine">My Lessons</RouterLink>
            <RouterLink v-if="user.role === 'Admin'" to="/admin">Admin</RouterLink>
            <span class="ml-auto text-sm text-gray-500">{{ user.name }}</span>
            <UButton variant="ghost" @click="logout()">Log out</UButton>
          </template>
          <UButton v-else class="ml-auto" to="/login">Log in</UButton>
        </nav>
      </header>
      <main class="mx-auto w-full max-w-4xl flex-1 p-4"><RouterView /></main>
      <footer class="mx-auto w-full max-w-4xl p-4 text-sm text-gray-400">
        <RouterLink to="/privacy">Privacy</RouterLink> ·
        <RouterLink to="/terms">Terms</RouterLink>
      </footer>
    </div>
  </UApp>
</template>

<script setup lang="ts">
import { user, logout } from './composables/useAuth'
</script>
