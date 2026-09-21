<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, ApiError } from '../composables/useApi'
import { formatDayLocal, type SlotDay } from '../composables/useTime'

interface MyBooking {
  id: string; date: string; startUtc: string; originalDate: string | null; meetLink: string | null
}
const bookings = ref<MyBooking[]>([])
const days = ref<SlotDay[]>([])
const rescheduling = ref<MyBooking | null>(null)
const message = ref('')
const error = ref('')
const modalError = ref('')

async function load() {
  try { bookings.value = await api<MyBooking[]>('/bookings/mine') }
  catch (e: unknown) { error.value = e instanceof Error ? e.message : 'Load failed' }
}

async function cancel(id: string) {
  message.value = ''; error.value = ''
  try {
    await api(`/bookings/${id}`, { method: 'DELETE' })
    message.value = 'Cancelled.'
    await load()
  } catch (e: unknown) { error.value = e instanceof Error ? e.message : 'Cancel failed' }
}

async function loadSlotsForReschedule() {
  modalError.value = ''
  try {
    const next = new Date(new Date().getFullYear(), new Date().getMonth() + 1, 1)
    days.value = await api<SlotDay[]>(`/slots?year=${next.getFullYear()}&month=${next.getMonth() + 1}`)
  } catch (e: unknown) { modalError.value = e instanceof Error ? e.message : 'Could not load days.' }
}

async function rescheduleTo(date: string) {
  if (!rescheduling.value) return
  modalError.value = ''
  try {
    await api(`/bookings/${rescheduling.value.id}/reschedule`, { method: 'POST', body: JSON.stringify({ newDate: date }) })
    rescheduling.value = null
    message.value = 'Moved!'
    error.value = ''
    await load()
  } catch (e: unknown) {
    modalError.value = e instanceof ApiError ? e.message : 'Could not move to that day.'
  }
}

const bookableDays = () => days.value.filter(x => x.canBook)

onMounted(load)
</script>

<template>
  <div>
    <h1 class="mb-4 text-xl font-semibold text-highlighted">My lessons</h1>

    <UAlert v-if="error" color="error" variant="subtle" icon="i-lucide-circle-alert" :title="error" class="mb-4" />
    <UAlert v-else-if="message" color="success" variant="subtle" icon="i-lucide-check" :title="message" class="mb-4" />

    <div v-if="bookings.length" class="space-y-3">
      <UCard v-for="b in bookings" :key="b.id" variant="outline">
        <template #header>
          <p class="font-semibold text-highlighted">{{ formatDayLocal(b.startUtc) }}</p>
        </template>

        <div class="flex flex-wrap items-center gap-2 text-muted">
          <UBadge v-if="b.originalDate" color="warning" variant="subtle" icon="i-lucide-arrow-right-left" size="sm">
            moved from {{ b.originalDate }}
          </UBadge>
          <ULink
            v-if="b.meetLink"
            :to="b.meetLink" target="_blank"
            icon="i-lucide-video"
            class="text-primary"
          >Meet link</ULink>
        </div>

        <template #footer>
          <div class="flex justify-end gap-2">
            <UButton color="neutral" variant="soft" icon="i-lucide-calendar-cog" @click="rescheduling = b; message = ''; loadSlotsForReschedule()">Reschedule</UButton>
            <UButton color="error" variant="soft" icon="i-lucide-x" @click="cancel(b.id)">Cancel</UButton>
          </div>
        </template>
      </UCard>
    </div>

    <UEmpty
      v-else-if="!error"
      icon="i-lucide-calendar-off"
      title="No upcoming lessons"
      description="The calendar is waiting."
      :actions="[{ label: 'Browse calendar', to: '/calendar', variant: 'subtle', color: 'neutral', icon: 'i-lucide-calendar' }]"
    />

    <UModal :open="!!rescheduling" @update:open="rescheduling = null">
      <template #content>
        <UCard variant="naked">
          <template #header>
            <h2 class="text-lg font-semibold text-highlighted">Move to which day?</h2>
            <p class="text-muted text-sm">Next month's bookable days are shown.</p>
          </template>

          <UAlert v-if="modalError" color="error" variant="subtle" icon="i-lucide-circle-alert" :title="modalError" class="mb-4" />

          <div v-if="bookableDays().length" class="grid grid-cols-4 gap-2">
            <UButton
              v-for="d in bookableDays()" :key="d.date"
              color="neutral" variant="outline"
              :data-date="d.date"
              @click="rescheduleTo(d.date)"
            >
              {{ d.date.slice(-2) }}
            </UButton>
          </div>
          <UEmpty
            v-else-if="!modalError"
            icon="i-lucide-calendar-x"
            title="No bookable days left next month"
            size="sm"
          />
        </UCard>
      </template>
    </UModal>
  </div>
</template>
