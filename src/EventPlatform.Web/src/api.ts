export type EventSummary = {
  id: string; title: string; category: string; city: string; currency: string;
  priceMinor: number; startsAt: string; timezone: string; venue: string;
  imageUrl: string | null;
};
export type EventDetail = EventSummary & { description: string; endsAt: string; capacity: number };
export type ManagedEvent = EventDetail & { organisationId: string; isPublished: boolean };
export type EventPage = { items: EventSummary[]; total: number; page: number; pageSize: number };
export type Session = { id: string; displayName: string; email: string;
  memberships: { organisationId: string; organisationName: string; role: string }[] };

export async function mutate<T>(path: string, body: unknown, method = 'POST'): Promise<T> {
  // Refresh after identity changes: an anonymous CSRF token cannot authenticate a signed-in mutation.
  const csrf = await get<{ token: string }>('/api/auth/csrf');
  const response = await fetch(path, { method, headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrf.token },
    body: JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(response.status === 401 ? 'Email or password is incorrect.'
      : response.status === 429 ? 'Too many attempts. Please wait a minute and try again.'
      : problem?.detail || 'The request could not be completed. Please try again.');
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
}

export async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, { signal });
  if (!response.ok) throw new Error(response.status === 400
    ? 'Check your filters. The end date must be on or after the start date.'
    : 'We couldn’t load events. Please try again.');
  return response.json() as Promise<T>;
}

export function price(event: EventSummary) {
  return event.priceMinor === 0 ? 'Free entry' : new Intl.NumberFormat('en-MY', {
    style: 'currency', currency: event.currency, maximumFractionDigits: 2,
  }).format(event.priceMinor / 100);
}

export function date(event: EventSummary, options: Intl.DateTimeFormatOptions) {
  return new Intl.DateTimeFormat('en-GB', { ...options, timeZone: event.timezone }).format(new Date(event.startsAt));
}
