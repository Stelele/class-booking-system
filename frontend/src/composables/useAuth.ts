import { ref } from 'vue'
import { api } from './useApi'

export interface Me { id: string; name: string; email: string; role: 'Admin' | 'Student' }

export const user = ref<Me | null>(null)

// main.ts mounts the app inside loadMe().finally(), so while this promise is
// pending there is no UI at all. Bound the wait, otherwise a backend that
// accepts the connection and never answers leaves every visitor on a blank
// page. A timeout degrades to "logged out" and the login screen renders.
const BOOT_TIMEOUT_MS = 10_000

export async function loadMe() {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), BOOT_TIMEOUT_MS)
  try { user.value = await api<Me>('/auth/me', { signal: controller.signal }) }
  catch { user.value = null }
  finally { clearTimeout(timer) }
}

export async function requestCode(email: string) {
  await api('/auth/request-code', { method: 'POST', body: JSON.stringify({ email }) })
}

export function startGoogleLogin() {
  window.location.href = '/api/auth/google/login/start'
}

export async function verifyCode(email: string, code: string) {
  user.value = await api<Me>('/auth/verify', { method: 'POST', body: JSON.stringify({ email, code }) })
}

export async function logout() {
  await api('/auth/logout', { method: 'POST' }).catch(() => {})
  user.value = null
}
