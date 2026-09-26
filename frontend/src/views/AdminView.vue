<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, ApiError } from '../composables/useApi'
import { user as currentUser } from '../composables/useAuth'
import type { SlotDay } from '../composables/useTime'
import MonthCalendar from '../components/MonthCalendar.vue'

interface AdminUser {
  id: string; name: string; email: string; phone: string | null; role: 'Admin' | 'Student'
}

const year = ref(new Date().getFullYear())
const month = ref(new Date().getMonth() + 1)
const days = ref<SlotDay[]>([])
const loading = ref(true)
const lastBackup = ref<string | null>(null)
const restoring = ref(false)
const backingUp = ref(false)
const confirmOpen = ref(false)
const error = ref('')
const restoreMessage = ref('')
const restoreError = ref(false)
const route = useRoute()
const router = useRouter()
const googleConnected = ref(false)
const googleNeedsReconnect = ref(false)
const googleLoading = ref(true)
const googleNotice = ref('')
const googleNoticeError = ref(false)
const googleDisconnectOpen = ref(false)
const googleDisconnecting = ref(false)
const googleNoticeWarning = ref(false)

const people = ref<AdminUser[]>([])
const drafts = ref<Record<string, { name: string; email: string; phone: string }>>({})
const peopleError = ref('')
const savingId = ref('')

async function loadPeople() {
  try {
    const rows = await api<AdminUser[]>('/admin/users')
    people.value = rows
    // editable copies — the inputs own their own state until Save is pressed
    drafts.value = Object.fromEntries(rows.map(r => [r.id, {
      name: r.name, email: r.email, phone: r.phone ?? '',
    }]))
  } catch (e: unknown) {
    peopleError.value = e instanceof ApiError ? e.message : 'Could not load people.'
  }
}

async function savePerson(id: string) {
  const draft = drafts.value[id]
  if (!draft) return
  savingId.value = id
  peopleError.value = ''
  try {
    const updated = await api<AdminUser>(`/admin/users/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name: draft.name, email: draft.email, phone: draft.phone }),
    })
    const i = people.value.findIndex(p => p.id === id)
    if (i >= 0) people.value[i] = updated
    drafts.value[id] = { name: updated.name, email: updated.email, phone: updated.phone ?? '' }
    // the session badge is only refreshed at boot, so a self-rename has to be
    // pushed into the shared auth ref by hand
    if (updated.id === currentUser.value?.id) currentUser.value = { ...currentUser.value, ...updated }
  } catch (e: unknown) {
    peopleError.value = e instanceof ApiError ? e.message : 'Save failed.'
  } finally {
    savingId.value = ''
  }
}

function connectGoogle() {
  window.location.href = '/api/auth/google/start'
}

async function disconnectGoogle() {
  googleDisconnecting.value = true
  googleNotice.value = ''
  googleNoticeError.value = false
  googleNoticeWarning.value = false
  try {
    const result = await api<{ remoteRevoked: boolean }>('/admin/google', { method: 'DELETE' })
    googleDisconnectOpen.value = false
    googleConnected.value = false
    googleNeedsReconnect.value = false
    googleNotice.value = result.remoteRevoked
      ? 'Google disconnected. New bookings use the fallback link.'
      : 'Local Google access was removed, but Google did not confirm revocation. Remove Lesson Booking from Google Account Settings → Security → Third-party connections.'
    googleNoticeError.value = false
    googleNoticeWarning.value = !result.remoteRevoked
    await loadGoogleStatus()
  } catch (e: unknown) {
    googleNotice.value = e instanceof Error ? e.message : 'Could not disconnect Google.'
    googleNoticeError.value = true
    googleNoticeWarning.value = false
  } finally {
    googleDisconnecting.value = false
  }
}

async function loadGoogleStatus() {
  try {
    const res = await api<{ connected: boolean; needsReconnect: boolean }>('/admin/google/status')
    googleConnected.value = res.connected
    googleNeedsReconnect.value = res.needsReconnect
  } catch { googleConnected.value = false; googleNeedsReconnect.value = false }
  finally { googleLoading.value = false }
}

async function backupNow() {
  backingUp.value = true
  try {
    const res = await api<{ lastBackupUtc: string }>('/admin/backups/run', { method: 'POST' })
    lastBackup.value = res.lastBackupUtc
  } catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Backup failed'
  } finally { backingUp.value = false }
}

// days with existing bookings can't be (un)blocked — cancel/see calendar instead
function toggleable(day: SlotDay) {
  return day.state === 'Bookable' || day.state === 'Blocked' || day.state === 'Cutoff'
}

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

async function doRestore() {
  restoring.value = true
  restoreMessage.value = ''
  restoreError.value = false
  try {
    await api('/admin/backups/restore', { method: 'POST' })
    restoreMessage.value = 'Restore accepted — the app is restarting onto the backup.'
  } catch (e: unknown) {
    restoreError.value = true
    restoreMessage.value = e instanceof ApiError ? e.message : 'Restore failed.'
  } finally {
    restoring.value = false
    confirmOpen.value = false
  }
}

onMounted(() => {
  load(); loadBackup(); loadGoogleStatus(); loadPeople()
  const g = route.query.google
  if (g === 'connected') { googleNotice.value = 'Google connected — new lessons get Meet links.'; googleNoticeError.value = false }
  else if (g === 'error') { googleNotice.value = 'Google connect failed — please try again.'; googleNoticeError.value = true }
  if (g !== undefined) void router.replace({ query: { ...route.query, google: undefined } })
})
</script>

<template>
  <div>
    <h1 class="mb-4 text-xl font-semibold text-highlighted">Admin — block days</h1>

    <UAlert
      v-if="!googleLoading && googleNotice"
      :color="googleNoticeError ? 'error' : googleNoticeWarning ? 'warning' : 'success'"
      :icon="googleNoticeError ? 'i-lucide-circle-alert' : googleNoticeWarning ? 'i-lucide-triangle-alert' : 'i-lucide-check'"
      variant="subtle"
      :title="googleNotice"
      class="mb-4"
    />

    <UAlert
      v-if="!googleLoading && googleNeedsReconnect"
      color="error" variant="subtle" icon="i-lucide-circle-alert"
      title="Reconnect Google"
      description="Your stored Google permission needs attention. Reconnect to request the narrower calendar.events.owned permission, or disconnect to remove local access."
      class="mb-4"
    />

    <UCard
      v-if="!googleLoading && (googleConnected || googleNeedsReconnect)"
      variant="outline"
      class="mb-4"
    >
      <template #header>
        <div class="flex items-center justify-between gap-4">
          <h2 class="font-semibold text-highlighted">Google Calendar</h2>
          <UBadge
            :color="googleNeedsReconnect ? 'warning' : 'success'"
            variant="soft"
          >
            {{ googleNeedsReconnect ? 'Reconnect required' : 'Connected' }}
          </UBadge>
        </div>
      </template>

      <p class="text-muted">
        {{ googleNeedsReconnect
          ? 'Reconnect for new Meet links, or disconnect to remove local Google access.'
          : 'Create and remove lesson events with Meet links on your primary calendar.' }}
      </p>
      <p class="mt-3 text-sm text-muted"><strong>Required permission:</strong> calendar.events.owned</p>

      <template #footer>
        <div class="flex flex-wrap items-center justify-between gap-3">
          <ULink to="/privacy" class="text-sm text-primary">Privacy policy</ULink>
          <div class="flex flex-wrap gap-2">
            <UButton
              v-if="googleNeedsReconnect"
              color="primary"
              icon="i-lucide-refresh-cw"
              @click="connectGoogle"
            >
              Reconnect Google
            </UButton>
            <UButton
              color="error"
              variant="soft"
              icon="i-lucide-unlink"
              @click="googleDisconnectOpen = true"
            >
              Disconnect Google
            </UButton>
          </div>
        </div>
      </template>
    </UCard>

    <div v-if="!googleLoading && !googleConnected && !googleNeedsReconnect" class="mb-4">
      <UButton color="primary" icon="i-lucide-calendar-plus" @click="connectGoogle">
        Connect Google
      </UButton>
    </div>

    <UCard variant="outline" class="mb-6">
      <template #header>
        <h2 class="font-semibold text-highlighted">People</h2>
        <p class="text-muted text-sm">
          These names show on the shared calendar, in WhatsApp reminders and on the Meet invites.
        </p>
      </template>

      <UAlert
        v-if="peopleError"
        color="error" variant="subtle" icon="i-lucide-circle-alert"
        :title="peopleError" class="mb-4"
      />

      <div v-if="!people.length && !peopleError" class="text-muted text-sm">Loading…</div>

      <div v-for="p in people" :key="p.id" :data-user-email="p.email" class="border-default border-b pb-4 last:border-b-0 last:pb-0">
        <div class="flex items-center gap-2">
          <UBadge
            :color="p.role === 'Admin' ? 'primary' : 'neutral'"
            variant="subtle" size="sm"
            :data-role="p.role"
          >{{ p.role }}</UBadge>
          <span class="text-muted truncate text-sm">{{ p.email }}</span>
        </div>

        <div class="mt-2 grid gap-2 sm:grid-cols-[1fr_1fr_1fr_auto]">
          <UInput
            v-model="drafts[p.id]!.name"
            placeholder="Full name"
            aria-label="Full name"
            maxlength="100"
            class="min-w-0"
          />
          <UInput
            v-model="drafts[p.id]!.email"
            type="email"
            placeholder="Email"
            aria-label="Email"
            class="min-w-0"
          />
          <UInput
            v-model="drafts[p.id]!.phone"
            type="tel"
            placeholder="+447700900123"
            aria-label="WhatsApp number"
            class="min-w-0"
          />
          <UButton
            color="primary" icon="i-lucide-check"
            :loading="savingId === p.id"
            :data-user-id="p.id"
            @click="savePerson(p.id)"
          >Save</UButton>
        </div>
      </div>
    </UCard>

    <MonthCalendar
      :year="year" :month="month" :days="days" :loading="loading" :error="error"
      error-title="Couldn't apply the change" :interactive="toggleable" :heading-level="2"
      @shift="shift" @select="toggle"
    >
      <template #cell="{ day }">
        <UBadge v-if="day.state === 'Blocked'" color="error" variant="soft" size="sm" class="mt-1">blocked</UBadge>
      </template>
    </MonthCalendar>

    <UCard variant="outline" class="mt-6">
      <template #header>
        <h2 class="font-semibold text-highlighted">Backups</h2>
      </template>

      <p class="text-muted">
        Last R2 backup: {{ lastBackup ? new Date(lastBackup).toLocaleString() : 'none yet' }}
      </p>

      <UAlert
        v-if="restoreMessage"
        :color="restoreError ? 'error' : 'success'"
        variant="subtle"
        :icon="restoreError ? 'i-lucide-circle-alert' : 'i-lucide-check'"
        :title="restoreMessage"
        class="mt-3"
      />

      <template #footer>
        <div class="flex flex-wrap justify-end gap-2">
          <UButton color="neutral" variant="soft" icon="i-lucide-cloud-upload" :loading="backingUp" @click="backupNow">
            Back up now
          </UButton>
          <UButton color="error" variant="soft" icon="i-lucide-database-backup" :loading="restoring" @click="confirmOpen = true">
            Restore latest backup
          </UButton>
        </div>
      </template>
    </UCard>

    <UModal :open="googleDisconnectOpen" @update:open="googleDisconnectOpen = $event">
      <template #content>
        <UCard variant="naked">
          <template #header>
            <h2 class="text-lg font-semibold text-highlighted">Disconnect Google Calendar?</h2>
          </template>

          <p class="text-muted">Lesson Booking will lose permission to create or remove Calendar events.</p>
          <UAlert
            color="warning"
            variant="subtle"
            icon="i-lucide-info"
            title="Existing events are preserved"
            description="Your Google Calendar events and Meet links stay in your Google account. New bookings use the configured fallback link until you reconnect."
            class="mt-4"
          />

          <template #footer>
            <div class="flex justify-end gap-2">
              <UButton color="neutral" variant="soft" @click="googleDisconnectOpen = false">Cancel</UButton>
              <UButton
                color="error"
                icon="i-lucide-unlink"
                :loading="googleDisconnecting"
                @click="disconnectGoogle"
              >
                Disconnect Google
              </UButton>
            </div>
          </template>
        </UCard>
      </template>
    </UModal>

    <UModal :open="confirmOpen" @update:open="confirmOpen = $event">
      <template #content>
        <UCard variant="naked">
          <template #header>
            <h2 class="text-lg font-semibold text-highlighted">Restore latest backup?</h2>
          </template>
          <UAlert
            color="warning" variant="subtle" icon="i-lucide-triangle-alert"
            title="The app will restart"
            description="The current database is replaced with the newest R2 snapshot and the site restarts. Any bookings made since that backup are lost."
          />
          <template #footer>
            <div class="flex justify-end gap-2">
              <UButton color="neutral" variant="soft" @click="confirmOpen = false">Cancel</UButton>
              <UButton color="error" icon="i-lucide-database-backup" :loading="restoring" @click="doRestore">Restore and restart</UButton>
            </div>
          </template>
        </UCard>
      </template>
    </UModal>
  </div>
</template>
