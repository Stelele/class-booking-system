<script setup lang="ts">
const sections = [
  {
    title: 'Lesson times',
    body: 'Lessons run 20:30–22:30 Africa/Harare time, Monday to Saturday. The calendar shows the time in your own timezone.',
  },
  {
    title: 'Booking',
    body: 'Book any available day from the calendar. If you both book the same day, that becomes a combined lesson.',
  },
  {
    title: 'Cancelling and rescheduling',
    body: 'You can cancel or reschedule any booking at any time from My Lessons. Bookings close 30 minutes before a lesson starts.',
  },
]
</script>

<template>
  <div class="space-y-4">
    <h1 class="text-2xl font-bold text-highlighted">Terms</h1>

    <UCard v-for="s in sections" :key="s.title" variant="outline">
      <template #header>
        <h2 class="font-semibold text-highlighted">{{ s.title }}</h2>
      </template>
      <p class="text-muted">{{ s.body }}</p>
    </UCard>
  </div>
</template>
