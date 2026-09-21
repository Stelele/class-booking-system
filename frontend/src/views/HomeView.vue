<script setup lang="ts">
import type { ButtonProps } from '@nuxt/ui'

const links: ButtonProps[] = [
  { label: 'Get started', to: '/login', size: 'xl', icon: 'i-lucide-square-play' },
]
</script>

<template>
  <UPageHero
    headline="Programming lessons"
    title="Evening Programming Lessons"
    description="Daily 20:30 Harare · 2 hours · book when you're free, see when the crew is in."
    :links="links"
  />
</template>
