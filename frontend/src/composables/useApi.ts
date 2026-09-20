export class UnauthorizedError extends Error {
  constructor() { super('unauthorized') }
}
export class ApiError extends Error {}

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const { headers: extraHeaders, ...rest } = init ?? {}
  const res = await fetch(`/api${path}`, {
    credentials: 'include',
    ...rest,
    headers: { 'Content-Type': 'application/json', ...(extraHeaders ?? {}) },
  })
  if (res.status === 401) {
    // session gone server-side — drop local state so guards route to /login
    const { user } = await import('./useAuth')
    user.value = null
    throw new UnauthorizedError()
  }
  if (res.status === 204) return undefined as T
  if (!res.ok) {
    const body: unknown = await res.json().catch(() => null)
    const message =
      body && typeof body === 'object' && 'error' in body && typeof body.error === 'string'
        ? body.error
        : res.statusText
    throw new ApiError(message || 'Request failed')
  }
  return res.json() as Promise<T>
}
