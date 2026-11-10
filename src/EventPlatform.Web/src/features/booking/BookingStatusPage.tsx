import { useEffect, useState } from 'react';
import type { Booking } from '../../api';
import { AuthControls } from '../auth/AuthControls';
import { CheckoutButton } from './CheckoutButton';

export function BookingStatusPage({ bookingId }: { bookingId: string }) {
  const [booking, setBooking] = useState<Booking>();
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    let timer: number | undefined;
    setBooking(undefined); setLoading(true); setError('');
    async function load() {
      try {
        const response = await fetch(`/api/bookings/${bookingId}`, { signal: controller.signal });
        if (response.status === 401) throw new Error('Sign in to the account that made this reservation.');
        if ([403, 404].includes(response.status)) throw new Error('This booking is not available to your account.');
        if (!response.ok) throw new Error('Unable to load your booking. Please try again.');
        const current: Booking = await response.json();
        setBooking(current); setError('');
        if (['Held', 'Expired', 'PaymentReview'].includes(current.state)) timer = window.setTimeout(load, 5000);
      } catch (e) { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : 'Unable to load booking.'); }
      finally { if (!controller.signal.aborted) setLoading(false); }
    }
    load();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [bookingId, reload]);

  const heading = booking?.state === 'Confirmed' ? 'You’re on the list.' : booking?.state === 'Held' ? 'Your place is on hold.'
    : booking?.state === 'PaymentReview' ? 'We’re reviewing your payment.' : 'Your booking';
  return <>
    <header className="site-header"><a className="wordmark" href="/">gather<span>✳</span></a>
      <nav aria-label="Main"><a href="/">Discover events ↗</a></nav><AuthControls onSessionChange={() => setReload(value => value + 1)} /></header>
    <main className="booking-page"><span className="eyebrow">YOUR NEXT GOOD PLAN</span><h1>{heading}</h1>
      {error && <p className="form-error" role="alert">{error}</p>}
      {loading ? <p role="status">Checking your booking…</p> : booking && <>
        <p className="booking-state" role="status">{booking.state}</p>
        <dl className="booking-facts"><div><dt>Admissions</dt><dd>{booking.quantity}</dd></div>
          <div><dt>Total</dt><dd>{new Intl.NumberFormat('en-MY', { style: 'currency', currency: booking.currency }).format(booking.totalMinor / 100)}</dd></div>
          <div><dt>Booking reference</dt><dd className="booking-reference">{booking.bookingId}</dd></div></dl>
        {booking.state === 'Confirmed' && <><p>Your demo booking is confirmed. Bring your QR ticket for admission.</p><p className="booking-actions"><a className="primary" href="/tickets">View my tickets ↗</a></p></>}
        {booking.state === 'Held' && <><p>Checkout returns do not confirm payment. This page updates when payment confirmation arrives.</p><CheckoutButton booking={booking} /></>}
        {booking.state === 'PaymentReview' && <p>Tickets were not issued. A refund has been queued for review or processing; don’t pay again for this booking.</p>}
        {booking.state === 'Expired' && <p>This hold has expired. If you completed sandbox checkout, wait for payment confirmation before making another booking.</p>}
        {booking.state === 'Cancelled' && <p>This reservation has been cancelled.</p>}
      </>}
      <div className="booking-actions"><button className="account-button" disabled={loading} onClick={() => setReload(value => value + 1)}>Refresh booking status</button><a href="/">Find another gathering ↗</a></div>
    </main>
  </>;
}
