<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api } from '../composables/useApi'
import type { SlotDay } from '../composables/useTime'

const year = ref(new Date().getFullYear())
const month = ref(new Date().getMonth() + 1)
const days = ref<SlotDay[]>([])
const lastBackup = ref<string | null>(null)
const restoring = ref(false)
const error = ref('')

async function load() {
  try { days.value = await api<SlotDay[]>(`/slots?year=${year.value}&month=${month.value}`) }
  catch (e: unknown) { error.value = e instanceof Error ? e.message : 'Load failed' }
}
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
  try {
    if (day.state === 'Blocked') {
      await api(`/admin/blocked-days/${day.date}`, { method: 'DELETE' })
    } else if (day.state !== 'Sunday' && day.state !== 'Past') {
      await api('/admin/blocked-days', { method: 'POST', body: JSON.stringify({ date: day.date, reason: 'unavailable' }) })
    }
    await load()
  } catch (e: unknown) { error.value = e instanceof Error ? e.message : 'Toggle failed' }
}
async function restore() {
  if (!window.confirm('Restore latest R2 backup? The app will restart.')) return
  restoring.value = true
  try { await api('/admin/backups/restore', { method: 'POST' }) }
  catch { /* process restarts anyway */ }
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
           class="min-h-16 cursor-pointer rounded p-2"
           :class="day.state === 'Blocked' ? 'bg-red-100 text-red-700' : day.state === 'Sunday' || day.state === 'Past' ? 'bg-gray-100 text-gray-400' : day.state === 'Cutoff' ? 'bg-yellow-50 text-gray-400' : 'bg-green-50 hover:bg-green-100'"
           @click="toggle(day)">
        {{ day.date.slice(-2) }}
        <div v-if="day.state === 'Blocked'" class="text-[10px]">blocked</div>
      </div>
    </div>
    <UCard class="mt-6">
      <p class="text-sm">Last R2 backup: {{ lastBackup ? new Date(lastBackup).toLocaleString() : 'none yet' }}</p>
      <UButton class="mt-2" color="error" variant="soft" :loading="restoring" @click="restore">Restore latest backup</UButton>
    </UCard>
  </div>
</template>
