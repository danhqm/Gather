import { useEffect, useRef, useState } from 'react';
import { ApiError, mutate } from '../../api';
import type { Booking, EventDetail } from '../../api';

type SavedRequest = { requestId: string; bookingId?: string };
function savedRequest(eventId: string): SavedRequest {
  try {
    const value = JSON.parse(sessionStorage.getItem(`gather.reservation.${eventId}`) || 'null');
    const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
    if (value && typeof value.requestId === 'string' && uuid.test(value.requestId)
      && (value.bookingId === undefined || typeof value.bookingId === 'string' && uuid.test(value.bookingId))) return value;
  } catch { /* Browser storage may be disabled or contain an older format. */ }
  return { requestId: crypto.randomUUID() };
}

export function ReservationPanel({ event, onSignIn }: { event: EventDetail; onSignIn: () => void }) {
  const [saved, setSaved] = useState(() => savedRequest(event.id));
  const [booking, setBooking] = useState<Booking>();
  const [quantity, setQuantity] = useState(1);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(!!saved.bookingId);
  const [pending, setPending] = useState(false);
  const [remaining, setRemaining] = useState(0);
  const [reload, setReload] = useState(0);
  const receivedAt = useRef(performance.now());

  useEffect(() => {
    try { sessionStorage.setItem(`gather.reservation.${event.id}`, JSON.stringify(saved)); }
    catch { /* shortcut: without storage, reopening cannot recover a hold; its server expiry still applies. */ }
  }, [saved, event.id]);

  useEffect(() => {
    if (!saved.bookingId) { setLoading(false); return; }
    const controller = new AbortController(); setLoading(true); setError('');
    fetch(`/api/bookings/${saved.bookingId}`, { signal: controller.signal }).then(async response => {
      if ([401, 403, 404].includes(response.status)) { setSaved({ requestId: crypto.randomUUID() }); setBooking(undefined); return; }
      if (!response.ok) throw new Error('Unable to check your reservation. Please refresh its status.');
      const current: Booking = await response.json();
      receivedAt.current = performance.now(); setBooking(current);
    }).catch(e => { if (!controller.signal.aborted) setError(e.message); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [saved.bookingId, reload]);

  useEffect(() => {
    if (booking?.state !== 'Held') return;
    let refreshed = false;
    const tick = () => {
      const seconds = Math.max(0, Math.ceil((Date.parse(booking.expiresAt) - Date.parse(booking.serverNow)
        - (performance.now() - receivedAt.current)) / 1000));
      setRemaining(seconds);
      if (seconds === 0 && !refreshed) { refreshed = true; setReload(value => value + 1); }
    };
    tick(); const timer = window.setInterval(tick, 1000);
    return () => clearInterval(timer);
  }, [booking]);

  async function reserve() {
    setPending(true); setError('');
    try {
      const current = await mutate<Booking>(`/api/events/${event.id}/reservations`, { quantity, requestId: saved.requestId });
      receivedAt.current = performance.now(); setBooking(current); setSaved({ ...saved, bookingId: current.bookingId });
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) onSignIn();
      else setError(e instanceof Error ? e.message : 'Unable to reserve. Retry with the same request.');
    } finally { setPending(false); }
  }

  async function cancel() {
    setPending(true); setError('');
    try { setBooking(await mutate<Booking>(`/api/bookings/${booking!.bookingId}/cancel`, {})); }
    catch (e) { setError(e instanceof Error ? e.message : 'Unable to cancel. Please try again.'); }
    finally { setPending(false); }
  }

  const total = new Intl.NumberFormat('en-MY', { style: 'currency', currency: event.currency }).format(
    (booking?.totalMinor ?? event.priceMinor * quantity) / 100);
  return <section className="reservation-panel" aria-label="Ticket reservation">
    <div className="reservation-heading"><h3>{booking ? 'Your reservation' : 'Make it a plan.'}</h3><strong>{total}</strong></div>
    {error && <p className="form-error" role="alert">{error}</p>}
    {loading ? <p role="status">Checking reservation…</p> : booking ? <>
      <p role="status">{booking.quantity} {booking.quantity === 1 ? 'ticket' : 'tickets'} · {booking.state}</p>
      {booking.state === 'Held' ? <>
        <p>Time remaining <strong role="timer" aria-live="off">{String(Math.floor(remaining / 60)).padStart(2, '0')}:{String(remaining % 60).padStart(2, '0')}</strong></p>
        <p className="field-help">Temporary demo reservation. Checkout is not enabled yet.</p>
        <button className="account-button" disabled={pending} onClick={cancel}>{pending ? 'Cancelling…' : 'Cancel reservation'}</button>
      </> : <button className="primary" disabled={pending} onClick={() => { setBooking(undefined); setSaved({ requestId: crypto.randomUUID() }); setError(''); }}>Start a new reservation</button>}
    </> : saved.bookingId ? <p>Check your existing reservation before making another.</p> : <>
      <div className="reserve-actions"><label htmlFor="ticket-quantity">Tickets</label><select id="ticket-quantity" value={quantity} disabled={pending} onChange={e => setQuantity(Number(e.target.value))}>
        {Array.from({ length: Math.min(4, event.capacity) }, (_, i) => i + 1).map(value => <option key={value} value={value}>{value}</option>)}</select>
        <button className="primary" disabled={pending} onClick={reserve}>{pending ? 'Reserving…' : 'Reserve tickets'}</button></div>
      <p className="field-help">A temporary hold for up to four tickets. Sign in to reserve.</p>
    </>}
    {saved.bookingId && <button className="text-button" disabled={loading || pending} onClick={() => setReload(value => value + 1)}>Refresh reservation status</button>}
  </section>;
}
