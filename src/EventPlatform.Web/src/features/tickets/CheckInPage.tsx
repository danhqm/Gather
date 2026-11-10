import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { AuthControls } from '../auth/AuthControls';

type CheckInEvent = { id: string; title: string; startsAt: string; timezone: string };
export function CheckInPage() {
  const [events, setEvents] = useState<CheckInEvent[]>([]);
  const [eventId, setEventId] = useState('');
  const [code, setCode] = useState('');
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [loading, setLoading] = useState(true);
  const [pending, setPending] = useState(false);
  const [reload, setReload] = useState(0);
  const [scanning, setScanning] = useState(false);
  const video = useRef<HTMLVideoElement>(null);
  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError(''); setEvents([]); setEventId('');
    fetch('/api/check-in/events', { signal: controller.signal }).then(async response => {
      if (response.status === 401) throw new Error('Sign in with your staff account to check in attendees.');
      if (!response.ok) throw new Error('Unable to load your assigned events.');
      setEvents(await response.json());
    }).catch(e => { if (!controller.signal.aborted) setError(e.message); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [reload]);

  useEffect(() => {
    if (!scanning) return;
    let cancelled = false;
    let controls: { stop(): void } | undefined;
    async function start() {
      try {
        if (!navigator.mediaDevices?.getUserMedia) throw new Error('Camera scanning requires HTTPS or localhost. Paste the admission code below.');
        const { BrowserQRCodeReader } = await import('@zxing/browser');
        if (cancelled) return;
        const reader = new BrowserQRCodeReader();
        controls = await reader.decodeFromVideoDevice(undefined, video.current!, (result, _error, active) => {
          if (result && !cancelled) {
            active.stop(); setCode(result.getText()); setScanning(false); setNotice('Code captured. Choose Check ticket to admit.');
          }
        });
        if (cancelled) controls.stop();
      } catch (e) {
        if (!cancelled) { setScanning(false); setError(e instanceof Error && e.message.startsWith('Camera scanning') ? e.message : 'Camera unavailable. Allow camera access or paste the admission code below.'); }
      }
    }
    start();
    return () => { cancelled = true; controls?.stop(); };
  }, [scanning]);

  async function admit(event: FormEvent) {
    event.preventDefault(); setPending(true); setError(''); setNotice(''); setScanning(false);
    try {
      const csrf = await fetch('/api/auth/csrf');
      if (!csrf.ok) throw new Error('Unable to authenticate this scan. Please sign in again.');
      const response = await fetch(`/api/organiser/events/${eventId}/check-ins`, { method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': (await csrf.json()).token }, body: JSON.stringify({ code: code.trim() }) });
      const result = await response.json().catch(() => null);
      if (response.ok && result?.outcome === 'admitted') setNotice('Admitted. Welcome in!');
      else if (response.status === 409 && result?.outcome === 'already-used') setError('Already used. This ticket has already been admitted.');
      else if (result?.outcome === 'wrong-event') setError('Wrong event. Select the event on this ticket.');
      else if (response.status === 400) setError('Invalid ticket. Check the admission code.');
      else if ([401, 403].includes(response.status)) setError('Your account does not have check-in access for this event.');
      else throw new Error('Unable to check this ticket. Retry the same code.');
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to check this ticket.'); }
    finally { setPending(false); }
  }

  return <>
    <header className="site-header"><a className="wordmark" href="/">gather<span>✳</span></a><nav aria-label="Main"><a href="/">Discover events ↗</a></nav>
      <AuthControls onSessionChange={() => { setScanning(false); setCode(''); setNotice(''); setReload(value => value + 1); }} /></header>
    <main className="booking-page"><span className="eyebrow">A GOOD WELCOME</span><h1>At the door.</h1>
      {error && <p className="form-error" role="alert">{error}</p>}{notice && <p className="check-in-notice" role="status">{notice}</p>}
      {loading ? <p role="status">Loading assigned events…</p> : events.length ? <form className="account-form check-in-form" onSubmit={admit}>
        <label>Event<select aria-label="Event" required value={eventId} disabled={pending} onChange={e => { setEventId(e.target.value); setCode(''); setError(''); setNotice(''); setScanning(false); }}>
          <option value="">Choose an event</option>{events.map(event => <option key={event.id} value={event.id}>{event.title}</option>)}</select></label>
        <div><button type="button" className="account-button" disabled={!eventId || pending} onClick={() => { setError(''); setNotice(''); setScanning(value => !value); }}>{scanning ? 'Stop camera' : 'Scan QR with camera'}</button></div>
        <video ref={video} hidden={!scanning} className="scanner-preview" muted playsInline aria-label="Ticket camera preview" />
        <label htmlFor="admission-code">Admission code</label>
        <textarea id="admission-code" required maxLength={1024} value={code} disabled={pending} onChange={e => setCode(e.target.value)} placeholder="Scan a QR or paste the ticket’s code" />
        <button className="primary" disabled={!eventId || !code.trim() || pending}>{pending ? 'Checking…' : 'Check ticket'}</button>
      </form> : !error && <p>No events are assigned to your staff account.</p>}
      <button className="text-button" disabled={loading || pending} onClick={() => setReload(value => value + 1)}>Refresh assigned events</button>
    </main>
  </>;
}
