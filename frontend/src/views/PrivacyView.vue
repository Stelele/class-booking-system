<script setup lang="ts">
const sections = [
  {
    title: 'Information we collect',
    body: 'We collect your email address, optional phone or WhatsApp number, short-lived login codes, and lesson bookings. When the teacher connects Google Calendar, we also store an encrypted Google refresh token, the Google event identifier, event time, and Meet link for lesson events.',
  },
  {
    title: 'Google Calendar',
    body: 'Only the teacher can connect Google Calendar. The app requests the calendar.events.owned permission to create and remove lesson events on calendars the teacher owns. It does not read existing event content. The booked student receives the event invitation and any cancellation notice.',
  },
  {
    title: 'How information is used and shared',
    body: 'Information is used to run lessons, authenticate users, display the shared calendar, send login codes and reminders, and create or remove Google Calendar events. We do not use Google data for advertising, profiling, sale, or AI training.',
  },
  {
    title: 'Service providers',
    body: 'The application runs on DigitalOcean. Database backups are stored in Cloudflare R2. Email is delivered through Resend, WhatsApp through Twilio when configured, and Calendar data through Google. These providers process information to provide their services under their own privacy terms.',
  },
  {
    title: 'Security and retention',
    body: 'The site uses HTTPS. Google refresh tokens are encrypted with AES-256-GCM, and the encryption key is stored separately from the booking database. Login codes expire quickly. Booking information remains while the service is operated. Hosting and security systems may process IP address, request time, route, and user-agent information.',
  },
  {
    title: 'Disconnect and deletion',
    body: 'Disconnecting from Admin asks Google to revoke access and always removes the active local token. If Google does not confirm revocation, the app explains how to remove it from Google Account Settings. Existing Calendar events remain in the teacher’s Google account. Historical Cloudflare R2 database backups are not purged immediately, so an old backup may retain an already-encrypted revoked-token record; it cannot authorize Google access. Request account or data deletion by email.',
  },
]
</script>

<template>
  <div class="space-y-4">
    <div>
      <h1 class="text-2xl font-bold text-highlighted">Privacy Policy</h1>
      <p class="mt-1 text-sm text-muted">Last updated: 25 September 2026</p>
    </div>

    <UCard v-for="s in sections" :key="s.title" variant="outline">
      <template #header>
        <h2 class="font-semibold text-highlighted">{{ s.title }}</h2>
      </template>
      <p class="text-muted">{{ s.body }}</p>
    </UCard>

    <UCard variant="subtle">
      <p class="text-muted">Questions or deletion requests? Contact <ULink to="mailto:giftmugweni@gmail.com" class="text-primary">giftmugweni@gmail.com</ULink></p>
    </UCard>
  </div>
</template>
