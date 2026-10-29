export type EventSummary = {
  id: string; title: string; category: string; city: string; currency: string;
  priceMinor: number; startsAt: string; timezone: string; venue: string;
};
export type EventDetail = EventSummary & { description: string; endsAt: string; capacity: number };
export type EventPage = { items: EventSummary[]; total: number; page: number; pageSize: number };

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
