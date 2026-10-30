import { useState } from 'react';
import type { FormEvent } from 'react';
import { mutate } from '../../api';
import type { ManagedEvent } from '../../api';

// Both supported event cities use UTC+08:00, independent of the visitor's timezone.
function localInput(instant: string) { return new Date(new Date(instant).getTime() + 8 * 60 * 60 * 1000).toISOString().slice(0, 16); }

export function EventEditor({ event, organisationId, onSaved, onCancel }: {
  event?: ManagedEvent; organisationId: string; onSaved: (event: ManagedEvent) => void; onCancel: () => void;
}) {
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); setPending(true); setError('');
    const fields = new FormData(e.currentTarget);
    const value = (name: string) => String(fields.get(name) || '');
    try {
      if (!/^\d{1,7}(?:\.\d{1,2})?$/.test(value('price'))) throw new Error('Enter a price with at most two decimal places.');
      const [whole, fraction = ''] = value('price').split('.');
      const details = {
        title: value('title'), description: value('description'), category: value('category'), city: value('city'), venue: value('venue'),
        startsAt: event && value('startsAt') === localInput(event.startsAt) ? event.startsAt : new Date(`${value('startsAt')}:00+08:00`).toISOString(),
        endsAt: event && value('endsAt') === localInput(event.endsAt) ? event.endsAt : new Date(`${value('endsAt')}:00+08:00`).toISOString(),
        timezone: 'Asia/Kuala_Lumpur', currency: 'MYR', priceMinor: Number(whole) * 100 + Number(fraction.padEnd(2, '0')),
        capacity: Number(value('capacity')), imageUrl: value('imageUrl') || null,
      };
      const saved = event
        ? await mutate<ManagedEvent>(`/api/organiser/events/${event.id}`, details, 'PUT')
        : await mutate<ManagedEvent>('/api/organiser/events', { organisationId, event: details });
      onSaved(saved);
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to save. Please try again.'); }
    finally { setPending(false); }
  }

  return <form className="event-editor" onSubmit={submit}>
    <label className="wide">Event title<input name="title" required maxLength={160} defaultValue={event?.title} /></label>
    <label className="wide">Description<textarea name="description" rows={4} required maxLength={4000} defaultValue={event?.description} /></label>
    <div className="filter-field"><label htmlFor="event-category">Category</label><select id="event-category" name="category" defaultValue={event?.category || 'community'}>
      <option value="community">Community</option><option value="workshop">Workshop</option><option value="music">Music</option>
      <option value="food">Food & drink</option><option value="arts">Arts & culture</option></select></div>
    <div className="filter-field"><label htmlFor="event-city">City</label><select id="event-city" name="city" defaultValue={event?.city || 'Kuala Lumpur'}><option>Kuala Lumpur</option><option>George Town</option></select></div>
    <label className="wide">Venue<input name="venue" required maxLength={200} defaultValue={event?.venue} /></label>
    <label>Starts at<input name="startsAt" type="datetime-local" required defaultValue={event && localInput(event.startsAt)} /></label>
    <label>Ends at<input name="endsAt" type="datetime-local" required defaultValue={event && localInput(event.endsAt)} /></label>
    <p className="field-help wide">Times are in Malaysia time (UTC+08:00).</p>
    <label>Ticket price (RM)<input name="price" type="number" min="0" max="1000000" step="0.01" required defaultValue={event ? (event.priceMinor / 100).toFixed(2) : '0.00'} /></label>
    <label>Capacity<input name="capacity" type="number" min="1" max="1000000" step="1" required defaultValue={event?.capacity || 30} /></label>
    <label className="wide">Image URL (optional)<input name="imageUrl" type="url" placeholder="https://…" maxLength={2048} defaultValue={event?.imageUrl || ''} /></label>
    <p className="field-help wide">Leave the image empty to use Gather’s illustrated event poster.</p>
    {error && <p className="form-error wide" role="alert">{error}</p>}
    <div className="editor-actions wide"><button className="account-button" type="button" disabled={pending} onClick={onCancel}>Cancel</button>
      <button className="primary" disabled={pending} type="submit">{pending ? 'Saving…' : event ? 'Save changes' : 'Create draft'}</button></div>
  </form>;
}
