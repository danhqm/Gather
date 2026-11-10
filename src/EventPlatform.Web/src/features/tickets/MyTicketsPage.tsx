import { useEffect, useRef, useState } from 'react';
import { AuthControls } from '../auth/AuthControls';

type Ticket = { id: string; bookingId: string; number: number; admittedAt: string | null; eventId: string;
  title: string; venue: string; city: string; startsAt: string; timezone: string };

export function MyTicketsPage() {
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [reload, setReload] = useState(0);
  const [selected, setSelected] = useState<Ticket>();
  const [qr, setQr] = useState<{ code: string; image: string }>();
  const [qrError, setQrError] = useState('');
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError(''); setTickets([]);
    fetch('/api/me/tickets', { signal: controller.signal }).then(async response => {
      if (response.status === 401) throw new Error('Sign in to see your tickets.');
      if (!response.ok) throw new Error('Unable to load tickets. Please try again.');
      setTickets(await response.json());
    }).catch(e => { if (!controller.signal.aborted) setError(e.message); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [reload]);
  useEffect(() => {
    setQr(undefined); setQrError('');
    if (!selected) { dialog.current?.close(); return; }
    dialog.current?.showModal();
    const controller = new AbortController();
    fetch(`/api/me/tickets/${selected.id}/qr`, { signal: controller.signal }).then(async response => {
      if (!response.ok) throw new Error('Unable to load this ticket. Close and reopen to retry.');
      setQr(await response.json());
    }).catch(e => { if (!controller.signal.aborted) setQrError(e.message); });
    return () => controller.abort();
  }, [selected]);

  return <>
    <header className="site-header"><a className="wordmark" href="/">gather<span>✳</span></a><nav aria-label="Main"><a href="/">Discover events ↗</a></nav>
      <AuthControls onSessionChange={() => { setSelected(undefined); setReload(value => value + 1); }} /></header>
    <main className="booking-page"><span className="eyebrow">PLANS WORTH KEEPING</span><h1>Your tickets.</h1>
      {error && <p className="form-error" role="alert">{error}</p>}
      {loading ? <p role="status">Loading your tickets…</p> : tickets.length ? <div className="ticket-list">{tickets.map(ticket => <article className="ticket-card" key={ticket.id}>
        <span className="eyebrow">ADMISSION {ticket.number} · {ticket.admittedAt ? 'ALREADY USED' : 'READY TO GO'}</span><h2>{ticket.title}</h2>
        <p>{new Intl.DateTimeFormat('en-GB', { timeZone: ticket.timezone, day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit' }).format(new Date(ticket.startsAt))} · {ticket.timezone}</p>
        <p>{ticket.venue}, {ticket.city}</p><div className="booking-actions"><button className="primary" onClick={() => setSelected(ticket)}>Show ticket {ticket.number}</button>
          <a href={`/bookings/${ticket.bookingId}`}>Booking details ↗</a></div>
      </article>)}</div> : !error && <p>No confirmed tickets yet. Find a gathering to join.</p>}
      <button className="text-button" disabled={loading} onClick={() => setReload(value => value + 1)}>Refresh tickets</button>
    </main>
    <dialog ref={dialog} className="ticket-dialog" onClose={() => setSelected(undefined)} aria-labelledby="ticket-title">
      <button className="close-dialog" aria-label="Close ticket" onClick={() => setSelected(undefined)}>×</button>
      <div className="detail-content"><span className="eyebrow">ONE PERSON. ONE ADMISSION.</span><h2 id="ticket-title">{selected?.title}</h2>
        {qrError ? <p role="alert">{qrError}</p> : qr ? <>
          <img className="ticket-qr" src={qr.image} alt={`QR admission code for ticket ${selected?.number}`} />
          <p>Show this QR code at the event. Keep it private: each ticket can be used once.</p>
          <label className="ticket-code">Manual admission code<textarea readOnly value={qr.code} onFocus={e => e.currentTarget.select()} /></label>
        </> : <p role="status">Preparing your ticket…</p>}
      </div>
    </dialog>
  </>;
}
