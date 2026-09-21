<script setup lang="ts">
const sections = [
  {
    title: 'What we store',
    body: 'We store your email address, WhatsApp number and your lesson bookings. Nothing else.',
  },
  {
    title: 'How it is used',
    body: 'Your data is used only to run lessons and send you login codes and reminders. It is never sold or shared with anyone else.',
  },
  {
    title: 'Backups',
    body: 'The booking database is backed up nightly to encrypted storage (Cloudflare R2).',
  },
]
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-2xl font-bold text-highlighted">Privacy Policy</h1>

    <UCard v-for="s in sections" :key="s.title" variant="outline">
      <template #header>
        <h2 class="font-semibold text-highlighted">{{ s.title }}</h2>
      </template>
      <p class="text-muted">{{ s.body }}</p>
    </UCard>

    <UCard variant="subtle">
      <p class="text-muted">Questions? Contact <ULink to="mailto:giftmugweni@gmail.com" class="text-primary">giftmugweni@gmail.com</ULink></p>
    </UCard>
  </div>
</template>
