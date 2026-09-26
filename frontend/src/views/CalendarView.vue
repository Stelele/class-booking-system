<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api } from '../composables/useApi'
import { type SlotDay } from '../composables/useTime'
import BookingModal from '../components/BookingModal.vue'
import MonthCalendar from '../components/MonthCalendar.vue'

const year = ref(new Date().getFullYear())
const month = ref(new Date().getMonth() + 1)
const days = ref<SlotDay[]>([])
const loading = ref(true)
const error = ref('')
const booking = ref<SlotDay | null>(null)

// stale-response guard: rapid month shifts must not let an older
// response overwrite a newer one
let loadSeq = 0
async function load() {
  const seq = ++loadSeq
  error.value = ''
  try {
    const d = await api<SlotDay[]>(`/slots?year=${year.value}&month=${month.value}`)
    if (seq === loadSeq) { days.value = d; loading.value = false }
  }
  catch (e: unknown) {
    if (seq === loadSeq) { error.value = e instanceof Error ? e.message : 'Load failed'; loading.value = false }
  }
}

function shift(delta: number) {
  const d = new Date(year.value, month.value - 1 + delta, 1)
  year.value = d.getFullYear(); month.value = d.getMonth() + 1
  load()
}

function openBooking(day: SlotDay) { if (day.canBook) booking.value = day }

const combinedThisMonth = computed(() => days.value.some(d => d.studentNames.length >= 2))

onMounted(load)
</script>

<template>
  <div>
    <MonthCalendar
      :year="year" :month="month" :days="days" :loading="loading" :error="error"
      :interactive="(day: SlotDay) => day.canBook"
      @shift="shift" @select="openBooking"
    >
      <template #cell="{ day }">
        <div v-if="day.studentNames.length" class="mt-1 flex flex-col items-start gap-0.5">
          <UBadge v-for="name in day.studentNames" :key="name" color="neutral" variant="subtle" size="sm" class="max-w-full truncate">
            {{ name }}
          </UBadge>
        </div>
        <UBadge v-if="day.state === 'Combined'" color="primary" variant="soft" size="sm" class="mt-1">combined</UBadge>
      </template>
    </MonthCalendar>

    <BookingModal v-model:open="booking" :day="booking" @booked="load" />

    <UAlert
      v-if="!loading && !combinedThisMonth"
      color="info" variant="subtle" icon="i-lucide-users"
      title="No combined lesson yet this month"
      description="When you both book the same day it becomes a combined lesson — the shared calendar keeps us honest."
      class="mt-6"
    />
  </div>
</template>
