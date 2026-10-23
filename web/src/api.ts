import type { Session } from './types';
let csrf = '';
export class ApiError extends Error { constructor(message: string, public status: number) { super(message); } }
export async function api<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch('/api' + path, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin',
    headers: body === undefined ? { Accept: 'application/json' } : { Accept: 'application/json', 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf },
    body: body === undefined ? undefined : JSON.stringify(body) });
  if (!response.ok) {
    let message = response.status === 401 ? 'Your demo session has ended. Start a fresh workspace.' : response.status === 403 ? 'This role cannot perform that action.' : response.status === 429 ? 'Too many requests. Please wait a minute and try again.' : 'The request could not be completed. Please try again.';
    try { const problem = await response.json(); message = problem.errors ? Object.values(problem.errors as Record<string, string[]>).flat().join(' ') : problem.detail || problem.title || message; } catch { /* A status-only response still has a useful message. */ }
    throw new ApiError(message, response.status);
  }
  return response.json() as Promise<T>;
}
export async function session(): Promise<Session> {
  let result: Session;
  try { result = await api<Session>('/session'); }
  catch (e) { if (!(e instanceof ApiError) || e.status !== 401) throw e; result = await api<Session>('/session'); }
  csrf = result.csrfToken; return result;
}
