import { useEffect, useRef, useState } from 'react';
import { date, mutate, price } from '../../api';
import type { ManagedEvent, Session } from '../../api';
import { AuthControls } from '../auth/AuthControls';
import { EventEditor } from './EventEditor';

export function EventDashboard() {
  const [organisation, setOrganisation] = useState<Session['memberships'][number]>();
  const [events, setEvents] = useState<ManagedEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [reload, setReload] = useState(0);
  const [editing, setEditing] = useState<ManagedEvent | 'new'>();
  const [pending, setPending] = useState<string>();
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const controller = new AbortController(); setLoading(true); setError('');
    async function load() {
      const me = await fetch('/api/auth/me', { signal: controller.signal });
      if (me.status === 401) { setOrganisation(undefined); return; }
      if (!me.ok) throw new Error('Unable to check your account. Please try again.');
      const session: Session = await me.json();
      const org = session.memberships.find(m => m.role === 'Organiser');
      setOrganisation(org);
      if (!org) return;
      const response = await fetch(`/api/organiser/events?organisationId=${org.organisationId}`, { signal: controller.signal });
      if (!response.ok) throw new Error('Unable to load your events. Please try again.');
      setEvents(await response.json());
    }
    load().catch(e => { if (!controller.signal.aborted) setError(e.message); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [reload]);

  useEffect(() => { if (editing) dialog.current?.showModal(); else dialog.current?.close(); }, [editing]);

  async function toggle(event: ManagedEvent) {
    setPending(event.id); setError(''); setNotice('');
    try {
      const updated = await mutate<ManagedEvent>(`/api/organiser/events/${event.id}/${event.isPublished ? 'unpublish' : 'publish'}`, {});
      setEvents(items => items.map(item => item.id === updated.id ? updated : item));
      setNotice(`${updated.title} is now ${updated.isPublished ? 'published' : 'unpublished'}.`);
    } catch (e) { setError(e instanceof Error ? e.message : 'Please try again.'); }
    finally { setPending(undefined); }
  }

  return <>
    <header className="site-header"><a className="wordmark" href="/">gather<span>✳</span></a><nav aria-label="Main"><a href="/">Discover events ↗</a></nav>
      <AuthControls onSessionChange={() => window.location.reload()} /></header>
    <main className="dashboard-shell">
      <div className="eyebrow">THE ORGANISER’S DESK</div>
      <div className="dashboard-heading"><div><h1>Your events.</h1><p>{organisation?.organisationName || 'A place to bring people together.'}</p></div>
        {organisation && <button className="primary" onClick={() => setEditing('new')}>Create event ↗</button>}</div>
      {loading ? <p className="loading-state">Loading your events…</p> : error ? <div className="empty-state" role="alert"><p>{error}</p><button className="primary" onClick={() => setReload(reload + 1)}>Try again</button></div>
        : !organisation ? <div className="empty-state"><h2>Organiser access required.</h2><p>Sign in with an organiser account to manage events.</p><a className="primary" href="/">Back to discovery</a></div>
        : <>
          <div className="dashboard-summary"><span>{events.length === 200 ? 'Your 200 most recent events' : `${events.length} events`}</span><span>{events.filter(e => e.isPublished).length} published</span><span>{events.filter(e => !e.isPublished).length} drafts</span></div>
          {notice && <p className="dashboard-notice" role="status">{notice}</p>}
          {!events.length ? <div className="empty-state"><h2>Your first gathering starts here.</h2><p>Create a draft, add the details and publish when you’re ready.</p></div>
            : <div className="management-list">{events.map(event => <article className="management-row" key={event.id}>
              <div className="management-date"><strong>{date(event, { day: '2-digit' })}</strong><span>{date(event, { month: 'short' })}</span></div>
              <div className="management-info"><span className={`publication-state ${event.isPublished ? 'published' : ''}`}>{event.isPublished ? 'Published' : 'Draft'}</span>
                <h2>{event.title}</h2><p>{event.venue} · {event.city} · {price(event)} · Capacity {event.capacity}</p></div>
              <div className="management-actions"><button className="account-button" disabled={!!pending} onClick={() => setEditing(event)}>Edit<span className="sr-only"> {event.title}</span></button>
                <button className="account-button" disabled={!!pending} onClick={() => toggle(event)}>{pending === event.id ? 'Updating…' : event.isPublished ? 'Unpublish' : 'Publish'}<span className="sr-only"> {event.title}</span></button></div>
            </article>)}</div>}
        </>}
    </main>
    <dialog className="editor-dialog" ref={dialog} onClose={() => setEditing(undefined)} aria-labelledby="editor-title">
      <button className="close-dialog" aria-label="Close event editor" onClick={() => setEditing(undefined)}>×</button>
      <div className="detail-content"><span className="eyebrow">MAKE A GOOD PLAN</span><h2 id="editor-title">{editing === 'new' ? 'Create an event.' : 'Edit your event.'}</h2>
        {editing && organisation && <EventEditor key={editing === 'new' ? 'new' : editing.id} event={editing === 'new' ? undefined : editing}
          organisationId={organisation.organisationId} onCancel={() => setEditing(undefined)} onSaved={saved => {
            setEditing(undefined); setNotice(`${saved.title} saved.`); setReload(reload + 1);
          }} />}</div>
    </dialog>
  </>;
}
