<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api } from '../composables/useApi'
import { type SlotDay } from '../composables/useTime'
import BookingModal from '../components/BookingModal.vue'

const year = ref(new Date().getFullYear())
const month = ref(new Date().getMonth() + 1)
const days = ref<SlotDay[]>([])
const loading = ref(true)
const error = ref('')
const booking = ref<SlotDay | null>(null)

// theme tokens (dark-mode aware) — the occupancy grid itself is the one
// documented exception: no Nuxt UI equivalent for an events calendar
const stateClass: Record<SlotDay['state'], string> = {
  Bookable: 'bg-success/10 hover:bg-success/20 cursor-pointer ring ring-success/15',
  Booked: 'bg-info/10 hover:bg-info/20 cursor-pointer ring ring-info/15',
  Combined: 'bg-primary/10 hover:bg-primary/20 cursor-pointer ring ring-primary/20',
  Sunday: 'bg-elevated/50 text-dimmed',
  Blocked: 'bg-error/10 text-error ring ring-error/15',
  Past: 'bg-elevated/50 text-dimmed/60',
  Cutoff: 'bg-warning/10 text-warning ring ring-warning/15',
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

const monthLabel = computed(() =>
  new Date(year.value, month.value - 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' }))

const combinedThisMonth = computed(() => days.value.some(d => d.studentNames.length >= 2))

onMounted(load)
</script>

<template>
  <div>
    <div class="mb-4 flex items-center justify-between">
      <UButton icon="i-lucide-chevron-left" color="neutral" variant="ghost" aria-label="Previous month" @click="shift(-1)" />
      <h1 class="text-xl font-semibold text-highlighted">{{ monthLabel }}</h1>
      <UButton icon="i-lucide-chevron-right" color="neutral" variant="ghost" aria-label="Next month" @click="shift(1)" />
    </div>

    <UAlert
      v-if="error"
      color="error" variant="subtle" icon="i-lucide-circle-alert"
      title="Couldn't load the calendar" :description="error"
      class="mb-4"
    />

    <div v-if="loading" class="grid grid-cols-7 gap-1">
      <USkeleton v-for="i in 35" :key="i" class="min-h-20" />
    </div>

    <div v-else class="grid grid-cols-7 gap-1 text-center text-sm">
      <div v-for="d in ['Mon','Tue','Wed','Thu','Fri','Sat','Sun']" :key="d" class="p-2 text-xs font-semibold text-muted uppercase">{{ d }}</div>
      <template v-for="(day, i) in grid" :key="i">
        <div v-if="!day" />
        <component
          :is="day?.canBook ? 'button' : 'div'"
          v-else
          class="min-h-20 rounded-lg p-2 text-left"
          :class="stateClass[day.state]"
          :data-date="day.date"
          @click="openBooking(day)"
        >
          <div class="font-semibold">{{ day.date.slice(-2) }}</div>
          <div v-if="day.studentNames.length" class="mt-1 flex flex-col items-start gap-0.5">
            <UBadge v-for="name in day.studentNames" :key="name" color="neutral" variant="subtle" size="sm" class="max-w-full truncate">
              {{ name }}
            </UBadge>
          </div>
          <UBadge v-if="day.state === 'Combined'" color="primary" variant="soft" size="sm" class="mt-1">combined</UBadge>
        </component>
      </template>
    </div>

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
