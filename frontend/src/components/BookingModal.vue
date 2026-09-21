<script setup lang="ts">
import { ref } from 'vue'
import { api, ApiError } from '../composables/useApi'
import { dualTimeLabel, formatDayLocal, type SlotDay } from '../composables/useTime'

const props = defineProps<{ day: SlotDay | null }>()
const emit = defineEmits<{ booked: [] }>()
const busy = ref(false)
const error = ref('')

const open = defineModel<SlotDay | null>('open')

async function book() {
  if (!props.day) return
  busy.value = true
  error.value = ''
  try {
    await api('/bookings', { method: 'POST', body: JSON.stringify({ date: props.day.date }) })
    open.value = null
    emit('booked')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Booking failed'
  } finally { busy.value = false }
}
</script>

<template>
  <UModal :open="!!day" @update:open="open = null">
    <template #content>
      <UCard variant="naked">
        <template #header>
          <div>
            <h2 class="text-lg font-semibold text-highlighted">Book {{ day?.date }}</h2>
            <p class="text-muted">{{ day ? formatDayLocal(day.startUtc) : '' }}</p>
          </div>
        </template>

        <UAlert
          color="neutral" variant="subtle" icon="i-lucide-clock"
          :title="day ? dualTimeLabel(day.startUtc) : ''"
        />

        <UAlert
          v-if="error"
          color="error" variant="subtle" icon="i-lucide-circle-alert"
          :title="error" class="mt-4"
        />

        <template #footer>
          <div class="flex justify-end gap-2">
            <UButton color="neutral" variant="soft" @click="open = null">Cancel</UButton>
            <UButton icon="i-lucide-check" :loading="busy" @click="book">Confirm booking</UButton>
          </div>
        </template>
      </UCard>
    </template>
  </UModal>
</template>
