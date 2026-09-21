<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { requestCode, verifyCode } from '../composables/useAuth'
import { ApiError } from '../composables/useApi'

const state = reactive({ email: '', code: '' })
const sent = ref(false)
const error = ref('')
const busy = ref(false)
const router = useRouter()

async function step1() {
  busy.value = true; error.value = ''
  try {
    await requestCode(state.email)
    sent.value = true
  }
  catch (e: unknown) { error.value = e instanceof Error ? e.message : 'Failed to send code' }
  finally { busy.value = false }
}

async function step2() {
  busy.value = true; error.value = ''
  try {
    await verifyCode(state.email, state.code)
    router.push('/calendar')
  }
  catch (e: unknown) {
    error.value = e instanceof ApiError ? e.message : 'Invalid or expired code'
  }
  finally { busy.value = false }
}
</script>

<template>
  <UCard class="mx-auto max-w-sm" variant="outline">
    <template #header>
      <h1 class="text-xl font-semibold text-highlighted">Log in</h1>
    </template>

    <UForm v-if="!sent" :state="state" @submit="step1">
      <UFormField label="Email" name="email" required>
        <UInput v-model="state.email" type="email" placeholder="Your email" icon="i-lucide-mail" class="w-full" />
      </UFormField>
      <UButton type="submit" :loading="busy" block class="mt-4">Send code</UButton>
    </UForm>

    <template v-else>
      <UAlert
        color="info"
        variant="subtle"
        icon="i-lucide-mail-check"
        title="Code sent"
        :description="`We emailed a code to ${state.email}. It expires in 10 minutes.`"
        class="mb-4"
      />
      <UForm :state="state" @submit="step2">
        <UFormField label="Login code" name="code" required hint="6 digits">
          <UInput v-model="state.code" inputmode="numeric" maxlength="6" placeholder="6-digit code" icon="i-lucide-key-round" class="w-full" />
        </UFormField>
        <UButton type="submit" :loading="busy" block class="mt-4">Verify</UButton>
      </UForm>
    </template>

    <template v-if="error" #footer>
      <UAlert color="error" variant="subtle" icon="i-lucide-circle-alert" :title="error" />
    </template>
  </UCard>
</template>
