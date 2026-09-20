<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, ApiError } from '../composables/useApi'
import type { SlotDay } from '../composables/useTime'

const year = ref(new Date().getFullYear())
const month = ref(new Date().getMonth() + 1)
const days = ref<SlotDay[]>([])
const lastBackup = ref<string | null>(null)
const restoring = ref(false)
const error = ref('')
const restoreMessage = ref('')

const cellClass: Record<SlotDay['state'], string> = {
  Bookable: 'bg-green-50 hover:bg-green-100',
  Booked: 'bg-blue-100',
  Combined: 'bg-purple-100',
  Sunday: 'bg-gray-100 text-gray-400',
  Blocked: 'bg-red-100 text-red-700',
  Past: 'bg-gray-100 text-gray-300',
  Cutoff: 'bg-yellow-50 text-gray-400',
}

// days with existing bookings can't be (un)blocked — cancel/see calendar instead
function toggleable(day: SlotDay) {
  return day.state === 'Bookable' || day.state === 'Blocked' || day.state === 'Cutoff'
}

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
let loadSeq = 0

async function loadBackup() {
  try {
    const res = await api<{ lastBackupUtc: string | null }>('/admin/backups')
    lastBackup.value = res.lastBackupUtc
  } catch { lastBackup.value = null }
}
function shift(delta: number) {
  const d = new Date(year.value, month.value - 1 + delta, 1)
  year.value = d.getFullYear(); month.value = d.getMonth() + 1
  load()
}
async function toggle(day: SlotDay) {
  if (!toggleable(day)) return
  try {
    if (day.state === 'Blocked') {
      await api(`/admin/blocked-days/${day.date}`, { method: 'DELETE' })
    } else {
      await api('/admin/blocked-days', { method: 'POST', body: JSON.stringify({ date: day.date, reason: 'unavailable' }) })
    }
    await load()
  } catch (e: unknown) { error.value = e instanceof Error ? e.message : 'Toggle failed' }
}
async function restore() {
  if (!window.confirm('Restore latest R2 backup? The app will restart.')) return
  restoring.value = true
  restoreMessage.value = ''
  try {
    await api('/admin/backups/restore', { method: 'POST' })
    restoreMessage.value = 'Restore accepted — the app is restarting onto the backup.'
  } catch (e: unknown) {
    restoreMessage.value = e instanceof ApiError ? e.message : 'Restore failed.'
  } finally { restoring.value = false }
}
onMounted(() => { load(); loadBackup() })
</script>

<template>
  <div>
    <h1 class="mb-4 text-xl font-bold">Admin — block days</h1>
    <div class="mb-4 flex items-center justify-between">
      <UButton icon="i-lucide-chevron-left" variant="ghost" aria-label="Previous month" @click="shift(-1)" />
      <span class="font-semibold">{{ year }}-{{ String(month).padStart(2, '0') }}</span>
      <UButton icon="i-lucide-chevron-right" variant="ghost" aria-label="Next month" @click="shift(1)" />
    </div>
    <p v-if="error" class="mb-3 text-red-500">{{ error }}</p>
    <div class="grid grid-cols-7 gap-1 text-center text-sm">
      <div v-for="day in days" :key="day.date" :data-date="day.date"
           class="min-h-16 rounded p-2"
           :class="[cellClass[day.state], toggleable(day) ? 'cursor-pointer' : '']"
           @click="toggle(day)">
        {{ day.date.slice(-2) }}
        <div v-if="day.state === 'Blocked'" class="text-[10px]">blocked</div>
      </div>
    </div>
    <UCard class="mt-6">
      <p class="text-sm">Last R2 backup: {{ lastBackup ? new Date(lastBackup).toLocaleString() : 'none yet' }}</p>
      <p v-if="restoreMessage" class="mt-2 text-sm text-blue-600">{{ restoreMessage }}</p>
      <UButton class="mt-2" color="error" variant="soft" :loading="restoring" @click="restore">Restore latest backup</UButton>
    </UCard>
  </div>
</template>
