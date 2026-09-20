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

onMounted(load)
</script>

<template>
  <div>
    <h1 class="mb-4 text-xl font-bold">My lessons</h1>
    <p v-if="error" class="mb-3 text-red-500">{{ error }}</p>
    <p v-if="message" class="mb-3 text-sm text-green-600">{{ message }}</p>
    <UCard v-for="b in bookings" :key="b.id" class="mb-3">
      <div class="flex items-center justify-between gap-2">
        <div>
          <p class="font-semibold">{{ formatDayLocal(b.startUtc) }}</p>
          <p class="text-sm text-gray-500">
            <span v-if="b.originalDate">moved from {{ b.originalDate }} · </span>
            <a v-if="b.meetLink" :href="b.meetLink" target="_blank" rel="noopener" class="underline">Meet link</a>
          </p>
        </div>
        <div class="flex gap-2">
          <UButton variant="soft" @click="rescheduling = b; message = ''; loadSlotsForReschedule()">Reschedule</UButton>
          <UButton color="error" variant="soft" @click="cancel(b.id)">Cancel</UButton>
        </div>
      </div>
    </UCard>
    <p v-if="!bookings.length && !error" class="text-gray-500">No upcoming lessons — the calendar is waiting.</p>

    <UModal :open="!!rescheduling" @update:open="rescheduling = null">
      <template #content>
        <div class="p-6">
          <h2 class="mb-3 font-bold">Move to which day?</h2>
          <p v-if="modalError" class="mb-3 text-sm text-red-500">{{ modalError }}</p>
          <div v-if="days.filter(x => x.canBook).length" class="grid grid-cols-4 gap-2">
            <UButton v-for="d in days.filter(x => x.canBook)" :key="d.date" variant="outline" :data-date="d.date" @click="rescheduleTo(d.date)">
              {{ d.date.slice(-2) }}
            </UButton>
          </div>
          <p v-else-if="!modalError" class="text-sm text-gray-400">No bookable days left next month.</p>
          <p class="mt-3 text-xs text-gray-400">Next month's bookable days are shown.</p>
        </div>
      </template>
    </UModal>
  </div>
</template>
