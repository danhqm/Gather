import { useEffect, useState } from 'react';
import { mutate } from '../../api';
import type { Booking } from '../../api';

export function CheckoutButton({ booking }: { booking: Booking }) {
  const [enabled, setEnabled] = useState<boolean>();
  const [pending, setPending] = useState(false);
  const [error, setError] = useState('');
  const [reload, setReload] = useState(0);
  useEffect(() => {
    if (booking.totalMinor === 0) { setEnabled(true); return; }
    const controller = new AbortController();
    fetch('/api/payments/config', { signal: controller.signal }).then(async response => {
      if (!response.ok) throw new Error('Unable to check checkout availability.');
      setEnabled((await response.json()).sandboxCheckoutEnabled);
    }).catch(e => { if (!controller.signal.aborted) setError(e.message); });
    return () => controller.abort();
  }, [booking.totalMinor, reload]);

  async function checkout() {
    setPending(true); setError('');
    try {
      const result = await mutate<{ url: string | null }>(`/api/bookings/${booking.bookingId}/checkout`, {});
      if (result.url) {
        const destination = new URL(result.url);
        if (destination.protocol !== 'https:' || destination.hostname !== 'checkout.stripe.com') throw new Error('Unexpected checkout destination. Please retry.');
        window.location.assign(destination.href);
      } else window.location.assign(`/bookings/${booking.bookingId}`);
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to start checkout. Retry this booking.'); }
    finally { setPending(false); }
  }

  return <div className="checkout-action">
    {error && <p className="form-error" role="alert">{error}</p>}
    {enabled ? <button className="primary" disabled={pending} onClick={checkout}>
      {pending ? 'Checking your booking…' : booking.totalMinor === 0 ? 'Confirm free booking' : 'Continue to sandbox checkout'}
    </button> : enabled === false ? <p className="field-help">Sandbox checkout is awaiting setup. This is a temporary demo reservation.</p>
      : error ? <button className="text-button" onClick={() => { setError(''); setReload(value => value + 1); }}>Retry checkout availability</button>
      : <p role="status">Checking checkout availability…</p>}
    {booking.totalMinor > 0 && enabled && <p className="field-help">Test payments only. Use a Stripe test card.</p>}
  </div>;
}
