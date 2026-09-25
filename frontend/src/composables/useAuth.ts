import { ref } from 'vue'
import { api } from './useApi'

export interface Me { id: string; name: string; email: string; role: 'Admin' | 'Student' }

export const user = ref<Me | null>(null)

export async function loadMe() {
  try { user.value = await api<Me>('/auth/me') }
  catch { user.value = null }
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
