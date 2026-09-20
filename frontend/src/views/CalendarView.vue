<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api } from '../composables/useApi'
import { type SlotDay } from '../composables/useTime'
import BookingModal from '../components/BookingModal.vue'

const year = ref(new Date().getFullYear())
const month = ref(new Date().getMonth() + 1)
const days = ref<SlotDay[]>([])
const error = ref('')
const booking = ref<SlotDay | null>(null)

const stateClass: Record<SlotDay['state'], string> = {
  Bookable: 'bg-green-100 hover:bg-green-200 cursor-pointer',
  Booked: 'bg-blue-100',
  Combined: 'bg-purple-100',
  Sunday: 'bg-gray-100 text-gray-400',
  Blocked: 'bg-red-50 text-gray-400',
  Past: 'bg-gray-100 text-gray-300',
  Cutoff: 'bg-yellow-50 text-gray-400',
}

const grid = computed(() => {
  const first = new Date(year.value, month.value - 1, 1)
  const offset = (first.getDay() + 6) % 7 // Monday-first
  const cells: (SlotDay | null)[] = Array(offset).fill(null)
  return [...cells, ...days.value]
})

// stale-response guard: rapid month shifts must not let an older
// response overwrite a newer one
let loadSeq = 0
async function load() {
  const seq = ++loadSeq
  error.value = ''
  try {
    const d = await api<SlotDay[]>(`/slots?year=${year.value}&month=${month.value}`)
    if (seq === loadSeq) days.value = d
  }
  catch (e: unknown) {
    if (seq === loadSeq) error.value = e instanceof Error ? e.message : 'Load failed'
  }
}

function shift(delta: number) {
  const d = new Date(year.value, month.value - 1 + delta, 1)
  year.value = d.getFullYear(); month.value = d.getMonth() + 1
  load()
}

function openBooking(day: SlotDay) { if (day.canBook) booking.value = day }

const monthLabel = computed(() =>
  new Date(year.value, month.value - 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' }))

const combinedThisMonth = computed(() => days.value.some(d => d.studentNames.length >= 2))

onMounted(load)
</script>

<template>
  <div>
    <div class="mb-4 flex items-center justify-between">
      <UButton icon="i-lucide-chevron-left" variant="ghost" aria-label="Previous month" @click="shift(-1)" />
      <h1 class="text-xl font-bold">{{ monthLabel }}</h1>
      <UButton icon="i-lucide-chevron-right" variant="ghost" aria-label="Next month" @click="shift(1)" />
    </div>
    <p v-if="error" class="mb-3 text-red-500">{{ error }}</p>
    <div class="grid grid-cols-7 gap-1 text-center text-xs">
      <div v-for="d in ['Mon','Tue','Wed','Thu','Fri','Sat','Sun']" :key="d" class="p-2 font-semibold">{{ d }}</div>
      <template v-for="(day, i) in grid" :key="i">
        <div v-if="!day" />
        <div v-else class="min-h-20 rounded-lg p-2" :class="stateClass[day.state]" :data-date="day.date" @click="openBooking(day)">
          <div class="font-bold">{{ day.date.slice(-2) }}</div>
          <div v-for="name in day.studentNames" :key="name" class="truncate text-[11px]">{{ name }}</div>
          <div v-if="day.state === 'Combined'" class="text-[11px] font-semibold">combined</div>
        </div>
      </template>
    </div>
    <BookingModal v-model:open="booking" :day="booking" @booked="load" />

    <UCard v-if="!combinedThisMonth" class="mt-6 text-sm text-gray-500">
      No combined lesson yet this month — the shared calendar keeps us honest.
    </UCard>
  </div>
</template>
