<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, ApiError } from '../composables/useApi'
import { formatDayLocal, todayInZone, type SlotDay } from '../composables/useTime'

// Lessons are defined in Harare time. The teacher reads the page in Harare, so
// the card must not mix a browser-zone time with the "Harare" label beside it.
const LESSON_ZONE = 'Africa/Harare'

interface TeacherLessonStudent {
  bookingId: string
  name: string
}

interface TeacherLesson {
  date: string
  startUtc: string
  endUtc: string
  localTime: string
  students: TeacherLessonStudent[]
  meetLink: string | null
  isCombined: boolean
  canCancel: boolean
  canReschedule: boolean
}

const lessons = ref<TeacherLesson[]>([])
const loading = ref(true)
const error = ref('')
const message = ref('')
const actionError = ref('')

// Set while a cancel is in flight, so the card can show which student is being
// removed and the buttons cannot be double-submitted.
const cancellingId = ref<string | null>(null)
const confirmingCancel = ref<TeacherLessonStudent | null>(null)

const rescheduling = ref<{ student: TeacherLessonStudent; from: string } | null>(null)
const rescheduleDays = ref<SlotDay[]>([])
const rescheduleError = ref('')

async function load() {
  error.value = ''
  try {
    lessons.value = await api<TeacherLesson[]>('/admin/bookings')
  }
  catch (e: unknown) {
    error.value = e instanceof Error ? e.message : 'Could not load lessons.'
  }
  finally {
    loading.value = false
  }
}

// "Tonight" reads better than a date when the lesson is today, which is the one
// day a teacher most often needs to join from this page. "Today" is resolved in
// the lesson's zone, not the browser's or UTC's.
function dayLabel(lesson: TeacherLesson): string {
  if (lesson.date === todayInZone(LESSON_ZONE)) return 'Tonight'
  return formatDayLocal(lesson.startUtc, LESSON_ZONE)
}

const missingLink = computed(() => lessons.value.filter(l => !l.meetLink).length)

// Whether the student being cancelled shares the day with someone else — that
// decides whether the room survives, so the dialog has to say so.
const cancellingIsCombined = computed(() => {
  const student = confirmingCancel.value
  if (!student) return false
  return lessons.value.some(l => l.isCombined && l.students.some(s => s.bookingId === student.bookingId))
})

// A combined lesson holds two bookings. Cancelling by lesson would take the
// other student with them, so the action always targets one student's id.
async function cancelConfirmed() {
  const student = confirmingCancel.value
  if (!student) return
  cancellingId.value = student.bookingId
  actionError.value = ''
  message.value = ''
  try {
    await api(`/bookings/${student.bookingId}`, { method: 'DELETE' })
    confirmingCancel.value = null
    message.value = `${student.name}'s lesson cancelled.`
    await load()
  }
  catch (e: unknown) {
    actionError.value = e instanceof ApiError ? e.message : 'Could not cancel that lesson.'
  }
  finally {
    cancellingId.value = null
  }
}

// The picker spans this month and the next, so a booking can be moved to a free
// day later this month as well as one next month. /mine only offers the next
// month; the teacher is scheduling around real availability, not just their own.
const rescheduleMonths = computed(() => {
  const now = new Date()
  const thisMonth = new Date(now.getFullYear(), now.getMonth(), 1)
  const nextMonth = new Date(now.getFullYear(), now.getMonth() + 1, 1)
  return [thisMonth, nextMonth]
})

const monthLabel = (m: Date) =>
  m.toLocaleDateString('en-GB', { month: 'long', timeZone: LESSON_ZONE })

// "2026-12" — the yyyy-mm prefix a SlotDay.date starts with, matching the
// /slots?year=&month= request the days came from. Built from the local parts
// rather than a formatted date so it cannot disagree with that request.
const monthPrefix = (m: Date) =>
  `${m.getFullYear()}-${String(m.getMonth() + 1).padStart(2, '0')}`

async function loadSlotsForReschedule() {
  rescheduleError.value = ''
  try {
    const loaded = await Promise.all(rescheduleMonths.value.map(m =>
      api<SlotDay[]>(`/slots?year=${m.getFullYear()}&month=${m.getMonth() + 1}`)))
    // sorted, so the grid reads chronologically across the month boundary
    rescheduleDays.value = loaded.flat().sort((a, b) => a.date.localeCompare(b.date))
  }
  catch (e: unknown) {
    rescheduleError.value = e instanceof Error ? e.message : 'Could not load days.'
  }
}

async function openReschedule(student: TeacherLessonStudent, from: string) {
  rescheduling.value = { student, from }
  actionError.value = ''
  message.value = ''
  await loadSlotsForReschedule()
}

const bookableDays = () => rescheduleDays.value.filter(d => d.canBook)

// Grouped under month headings so a day is unambiguous about which month it
// belongs to — a bare "03" next to "December" and "January" is not.
const bookableByMonth = () =>
  rescheduleMonths.value
    .map(m => ({
      key: `${m.getFullYear()}-${m.getMonth() + 1}`,
      label: monthLabel(m),
      days: bookableDays().filter(d => d.date.startsWith(`${monthPrefix(m)}-`)),
    }))
    .filter(g => g.days.length > 0)

async function rescheduleTo(date: string) {
  if (!rescheduling.value) return
  const { student } = rescheduling.value
  rescheduleError.value = ''
  try {
    await api(`/bookings/${student.bookingId}/reschedule`, {
      method: 'POST', body: JSON.stringify({ newDate: date }),
    })
    rescheduling.value = null
    message.value = `${student.name}'s lesson moved to ${date}.`
    await load()
  }
  catch (e: unknown) {
    rescheduleError.value = e instanceof ApiError ? e.message : 'Could not move to that day.'
  }
}

onMounted(load)
</script>

<template>
  <div>
    <h1 class="mb-1 text-xl font-semibold text-highlighted">Lessons to teach</h1>
    <p class="mb-4 text-muted">
      Every upcoming booked lesson, with the join link.
    </p>

    <UAlert
      v-if="error" color="error" variant="subtle" icon="i-lucide-circle-alert"
      :title="error" class="mb-4"
    />
    <UAlert
      v-else-if="actionError" color="error" variant="subtle" icon="i-lucide-circle-alert"
      :title="actionError" class="mb-4"
    />
    <UAlert
      v-else-if="message" color="success" variant="subtle" icon="i-lucide-check"
      :title="message" class="mb-4"
    />

    <UAlert
      v-if="missingLink > 0"
      color="warning" variant="subtle" icon="i-lucide-triangle-alert"
      class="mb-4"
      title="Some lessons have no join link yet"
      :description="`${missingLink} lesson${missingLink === 1 ? '' : 's'} cannot be joined from here. Connect Google Calendar on the Admin page so a new Meet link is created per lesson.`"
    />

    <div v-if="loading" class="space-y-3">
      <USkeleton v-for="i in 3" :key="i" class="h-32 w-full" />
    </div>

    <div v-else-if="lessons.length" class="space-y-3">
      <UCard v-for="l in lessons" :key="l.date" variant="outline" :data-lesson-date="l.date">
        <template #header>
          <div class="flex flex-wrap items-center justify-between gap-2">
            <p class="font-semibold text-highlighted">
              {{ dayLabel(l) }} · {{ l.localTime }} Harare
            </p>
            <UBadge
              v-if="l.isCombined"
              color="primary" variant="soft" size="sm" icon="i-lucide-users"
            >
              combined
            </UBadge>
          </div>
        </template>

        <ul class="space-y-2">
          <li
            v-for="s in l.students"
            :key="s.bookingId"
            class="flex flex-wrap items-center justify-between gap-2"
            :data-student="s.name"
          >
            <div class="flex flex-wrap items-center gap-2">
              <UBadge color="neutral" variant="subtle" size="sm" class="max-w-full truncate">
                {{ s.name }}
              </UBadge>
              <USkeleton v-if="cancellingId === s.bookingId" class="h-4 w-16" />
            </div>

            <div v-if="l.canCancel || l.canReschedule" class="flex gap-1">
              <UButton
                v-if="l.canReschedule"
                color="neutral" variant="ghost" size="sm" icon="i-lucide-calendar-cog"
                :data-reschedule-student="s.name"
                :disabled="cancellingId === s.bookingId"
                @click="openReschedule(s, l.date)"
              >
                Move
              </UButton>
              <UButton
                v-if="l.canCancel"
                color="error" variant="soft" size="sm" icon="i-lucide-x"
                :data-cancel-student="s.name"
                :loading="cancellingId === s.bookingId"
                @click="confirmingCancel = s"
              >
                Cancel
              </UButton>
            </div>
          </li>
        </ul>

        <template #footer>
          <div class="flex justify-end">
            <UButton
              v-if="l.meetLink"
              :to="l.meetLink"
              target="_blank"
              rel="noopener"
              color="primary"
              icon="i-lucide-video"
              :data-join-date="l.date"
            >
              Join lesson
            </UButton>
            <UBadge v-else color="warning" variant="soft" icon="i-lucide-video-off">
              no link yet
            </UBadge>
          </div>
        </template>
      </UCard>
    </div>

    <UEmpty
      v-else-if="!error && !loading"
      icon="i-lucide-calendar-off"
      title="No upcoming lessons"
      description="Nobody has booked a lesson yet."
    />

    <!-- Cancel is per student, so the dialog names them: on a combined lesson
         "Cancel this lesson" would be ambiguous about who is losing it. -->
    <UModal :open="!!confirmingCancel" @update:open="confirmingCancel = null">
      <template #content>
        <UCard variant="naked">
          <template #header>
            <h2 class="text-lg font-semibold text-highlighted">
              Cancel {{ confirmingCancel?.name }}'s lesson?
            </h2>
            <p class="text-muted text-sm">
              They will be told, and the booking will be released.
              <template v-if="cancellingIsCombined">
                If another student shares the day, the lesson and its Meet room stay for them.
              </template>
            </p>
          </template>

          <template #footer>
            <div class="flex justify-end gap-2">
              <UButton color="neutral" variant="soft" @click="confirmingCancel = null">
                Keep lesson
              </UButton>
              <UButton
                color="error"
                icon="i-lucide-x"
                :loading="cancellingId === confirmingCancel?.bookingId"
                @click="cancelConfirmed"
              >
                Cancel lesson
              </UButton>
            </div>
          </template>
        </UCard>
      </template>
    </UModal>

    <UModal :open="!!rescheduling" @update:open="rescheduling = null">
      <template #content>
        <UCard variant="naked">
          <template #header>
            <h2 class="text-lg font-semibold text-highlighted">Move to which day?</h2>
            <p class="text-muted text-sm">
              {{ rescheduling?.student.name }} · next month's bookable days.
            </p>
          </template>

          <UAlert
            v-if="rescheduleError" color="error" variant="subtle" icon="i-lucide-circle-alert"
            :title="rescheduleError" class="mb-4"
          />

          <div v-if="bookableDays().length" class="space-y-4">
            <div v-for="group in bookableByMonth()" :key="group.key">
              <p class="mb-2 text-xs font-semibold uppercase text-muted">{{ group.label }}</p>
              <div class="grid grid-cols-4 gap-2">
                <UButton
                  v-for="d in group.days" :key="d.date"
                  color="neutral" variant="outline"
                  :data-date="d.date"
                  @click="rescheduleTo(d.date)"
                >
                  {{ d.date.slice(-2) }}
                </UButton>
              </div>
            </div>
          </div>
          <UEmpty
            v-else-if="!rescheduleError"
            icon="i-lucide-calendar-x"
            title="No bookable days left next month"
            size="sm"
          />
        </UCard>
      </template>
    </UModal>
  </div>
</template>