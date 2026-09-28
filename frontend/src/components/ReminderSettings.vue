<script setup lang="ts">
import { onMounted } from 'vue'
import {
  disablePush, enablePush, initPush, needsIosInstall,
  pushBlocked, pushBusy, pushMessage, pushPermission, pushSubscribed, pushSupported,
} from '../composables/usePushNotifications'

onMounted(initPush)
</script>

<template>
  <UCard variant="outline" class="mt-6">
    <template #header>
      <p class="font-semibold text-highlighted">Lesson reminders</p>
      <p class="text-muted text-sm">
        Remind me before each lesson. Free — nothing is charged per message.
      </p>
    </template>

    <div class="space-y-3">
      <UAlert
        v-if="!pushSupported"
        color="neutral" variant="subtle" icon="i-lucide-info"
        title="This browser cannot show notifications"
        description="We will email you with every reminder instead."
      />

      <UAlert
        v-else-if="needsIosInstall"
        color="warning" variant="subtle" icon="i-lucide-info"
        title="Add this app to your Home Screen"
        description="On iPhone and iPad, notifications only work once the app is installed. Tap the Share button, choose “Add to Home Screen”, then open Lessons from its icon. We will email you until you do."
      />

      <UAlert
        v-else-if="pushBlocked"
        color="warning" variant="subtle" icon="i-lucide-eye-off"
        title="This window cannot send notifications"
        description="Browsers block notifications in private and incognito windows. Open the site in a normal window to turn reminders on — or we will email you."
      />

      <UAlert
        v-else-if="pushPermission === 'denied'"
        color="warning" variant="subtle" icon="i-lucide-triangle-alert"
        title="Notifications are blocked"
        description="Re-enable them in your browser's site settings, or we will keep emailing you."
      />

      <UAlert
        v-else-if="pushSubscribed"
        color="success" variant="subtle" icon="i-lucide-bell-ring"
        title="Reminders are on"
        description="You'll get a notification on this device. Email is only used if the notification cannot be delivered."
      />

      <UAlert
        v-else
        color="primary" variant="subtle" icon="i-lucide-bell"
        title="Turn on notifications"
        description="Get a nudge each morning and 30 minutes before a lesson. Until then, reminders arrive by email."
      />

      <p v-if="pushMessage" class="text-muted text-sm" role="status">{{ pushMessage }}</p>

      <div v-if="pushSupported && !needsIosInstall && !pushBlocked" class="flex flex-wrap gap-2">
        <UButton
          v-if="!pushSubscribed"
          icon="i-lucide-bell" :loading="pushBusy"
          :disabled="pushPermission === 'denied'"
          data-test="enable-push"
          @click="enablePush()"
        >
          Turn on reminders
        </UButton>
        <UButton
          v-else
          color="neutral" variant="soft" icon="i-lucide-bell-off"
          :loading="pushBusy" data-test="disable-push"
          @click="disablePush()"
        >
          Turn off reminders
        </UButton>
      </div>
    </div>
  </UCard>
</template>
