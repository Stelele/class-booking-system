<script setup lang="ts">
import { computed } from 'vue'
import type { SlotDay } from '../composables/useTime'

const props = withDefaults(defineProps<{
  year: number
  month: number
  days: SlotDay[]
  loading: boolean
  error?: string
  errorTitle?: string
  interactive?: (day: SlotDay) => boolean
  headingLevel?: 1 | 2
}>(), { error: '', errorTitle: "Couldn't load the calendar", headingLevel: 1 })

const emit = defineEmits<{
  shift: [delta: number]
  select: [day: SlotDay]
}>()

const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']

// theme tokens (dark-mode aware) — the occupancy grid itself is the one
// documented exception: no Nuxt UI equivalent for an events calendar
const stateClass: Record<SlotDay['state'], string> = {
  Bookable: 'bg-success/10 ring ring-success/15',
  Booked: 'bg-info/10 ring ring-info/15',
  Combined: 'bg-primary/10 ring ring-primary/20',
  Sunday: 'bg-elevated/50 text-dimmed',
  Blocked: 'bg-error/10 text-error ring ring-error/15',
  Past: 'bg-elevated/50 text-dimmed/60',
  Cutoff: 'bg-warning/10 text-warning ring ring-warning/15',
}

// only applied when the page says the day is actionable, so a non-clickable
// day never shows a pointer or hover tint
const actionableClass: Record<SlotDay['state'], string> = {
  Bookable: 'cursor-pointer hover:bg-success/20',
  Booked: 'cursor-pointer hover:bg-info/20',
  Combined: 'cursor-pointer hover:bg-primary/20',
  Sunday: '',
  Blocked: 'cursor-pointer hover:bg-error/20',
  Past: '',
  Cutoff: 'cursor-pointer hover:bg-warning/20',
}

const monthLabel = computed(() =>
  new Date(props.year, props.month - 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' }))

const grid = computed<(SlotDay | null)[]>(() => {
  const first = new Date(props.year, props.month - 1, 1)
  const offset = (first.getDay() + 6) % 7 // Monday-first
  return [...Array(offset).fill(null), ...props.days]
})

function isInteractive(day: SlotDay) {
  return props.interactive?.(day) ?? false
}
</script>

<template>
  <div role="region" aria-label="Month calendar">
    <div class="mb-4 flex items-center justify-between">
      <UButton icon="i-lucide-chevron-left" color="neutral" variant="ghost" aria-label="Previous month" @click="emit('shift', -1)" />
      <component :is="`h${headingLevel}`" class="text-xl font-semibold text-highlighted">{{ monthLabel }}</component>
      <UButton icon="i-lucide-chevron-right" color="neutral" variant="ghost" aria-label="Next month" @click="emit('shift', 1)" />
    </div>

    <UAlert
      v-if="error"
      color="error" variant="subtle" icon="i-lucide-circle-alert"
      :title="errorTitle" :description="error"
      class="mb-4"
    />

    <div v-if="loading" class="grid grid-cols-7 gap-1">
      <USkeleton v-for="i in 35" :key="i" class="min-h-20" />
    </div>

    <div v-else class="grid grid-cols-7 gap-1 text-center text-sm">
      <div v-for="d in weekdays" :key="d" class="p-2 text-xs font-semibold text-muted uppercase">{{ d }}</div>
      <template v-for="(day, i) in grid" :key="i">
        <div v-if="!day" />
        <component
          :is="isInteractive(day) ? 'button' : 'div'"
          v-else
          type="button"
          class="min-h-20 rounded-lg p-2 text-left"
          :class="[stateClass[day.state], isInteractive(day) ? actionableClass[day.state] : '']"
          :data-date="day.date"
          @click="emit('select', day)"
        >
          <div class="font-semibold">{{ day.date.slice(-2) }}</div>
          <slot name="cell" :day="day" />
        </component>
      </template>
    </div>
  </div>
</template>
