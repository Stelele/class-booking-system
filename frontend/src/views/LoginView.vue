<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { requestCode, verifyCode } from '../composables/useAuth'

const email = ref('')
const code = ref('')
const sent = ref(false)
const error = ref('')
const busy = ref(false)
const router = useRouter()

async function step1() {
  busy.value = true; error.value = ''
  try { await requestCode(email.value); sent.value = true }
  catch (e: unknown) { error.value = e instanceof Error ? e.message : 'Failed' }
  finally { busy.value = false }
}

async function step2() {
  busy.value = true; error.value = ''
  try { await verifyCode(email.value, code.value); router.push('/calendar') }
  catch { error.value = 'Invalid or expired code' }
  finally { busy.value = false }
}
</script>

<template>
  <UCard class="mx-auto max-w-sm">
    <h1 class="mb-4 text-xl font-bold">Log in</h1>
    <form v-if="!sent" class="space-y-3" @submit.prevent="step1">
      <UInput v-model="email" type="email" placeholder="Your email" required class="w-full" />
      <UButton type="submit" :loading="busy" block>Send code</UButton>
    </form>
    <form v-else class="space-y-3" @submit.prevent="step2">
      <p class="text-sm text-gray-500">Code sent to {{ email }} (check email/WhatsApp).</p>
      <UInput v-model="code" inputmode="numeric" maxlength="6" placeholder="6-digit code" required class="w-full" />
      <UButton type="submit" :loading="busy" block>Verify</UButton>
    </form>
    <p v-if="error" class="mt-3 text-sm text-red-500">{{ error }}</p>
  </UCard>
</template>
