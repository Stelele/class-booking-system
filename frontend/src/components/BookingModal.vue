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
      <div class="p-6">
        <h2 class="mb-2 text-lg font-bold">Book {{ day?.date }}</h2>
        <p class="mb-1 text-gray-600">{{ day ? formatDayLocal(day.startUtc) : '' }}</p>
        <p class="mb-4 text-sm text-gray-500">{{ day ? dualTimeLabel(day.startUtc) : '' }}</p>
        <p v-if="error" class="mb-3 text-sm text-red-500">{{ error }}</p>
        <div class="flex gap-2">
          <UButton :loading="busy" @click="book">Confirm booking</UButton>
          <UButton variant="soft" @click="open = null">Cancel</UButton>
        </div>
      </div>
    </template>
  </UModal>
</template>
