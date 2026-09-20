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
  if (res.status === 401) throw new UnauthorizedError()
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }))
    throw new ApiError(body.error ?? 'Request failed')
  }
  return res.json() as Promise<T>
}
