import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { date, get, price } from './api';
import type { EventDetail, EventPage, EventSummary } from './api';
import { AuthControls } from './features/auth/AuthControls';
import { EventDashboard } from './features/organiser/EventDashboard';
import { ReservationPanel } from './features/booking/ReservationPanel';
import { BookingStatusPage } from './features/booking/BookingStatusPage';

const categories = [ ['all', 'Everything'], ['music', 'Music'], ['workshop', 'Workshops'],
  ['arts', 'Arts & culture'], ['food', 'Food & drink'], ['community', 'Community'] ];
const initialFilters = { city: '', maxPrice: '', from: '', to: '' };
const posterText: Record<string, [string, string]> = {
  workshop: ['MADE', 'BY YOU.'], music: ['SIDE', 'B.'], community: ['GOOD', 'COMPANY.'],
  food: ['COME', 'HUNGRY.'], arts: ['OFF', 'THE PRESS.'],
};

function Poster({ event }: { event: EventSummary }) {
  const words = posterText[event.category] ?? ['SEE YOU', 'THERE.'];
  return <div className={`poster poster-${event.category}`} aria-hidden="true">
    {event.imageUrl && <img className="poster-image" src={event.imageUrl} alt="" loading="lazy" referrerPolicy="no-referrer"
      onError={e => { e.currentTarget.style.display = 'none'; }} onLoad={e => { e.currentTarget.style.display = 'block'; }} />}
    <div className="poster-top"><span>GATHER / LOCAL EDITION</span><span>NO. {event.id.slice(-2)}</span></div>
    <div className="poster-words"><span>{words[0]}</span><span>{words[1]}</span></div>
    <div className="poster-shape" />
    <div className="poster-bottom"><span>{event.city.toUpperCase()}</span><span>{date(event, { day: '2-digit', month: 'short' }).toUpperCase()} ↗</span></div>
  </div>;
}

function CataloguePage() {
  const [category, setCategory] = useState('all');
  const [draft, setDraft] = useState(initialFilters);
  const [filters, setFilters] = useState(initialFilters);
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<EventPage>();
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [reload, setReload] = useState(0);
  const [selected, setSelected] = useState<string>();
  const [detail, setDetail] = useState<EventDetail>();
  const [detailError, setDetailError] = useState('');
  const [signInRequest, setSignInRequest] = useState(0);
  const returnToEvent = useRef<string | undefined>(undefined);
  const dialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError('');
    async function load() {
      const params = new URLSearchParams({ page: String(page), pageSize: '6' });
      if (category !== 'all') params.set('category', category);
      if (filters.city) params.set('city', filters.city);
      if (filters.maxPrice) { params.set('maxPriceMinor', filters.maxPrice); params.set('currency', 'MYR'); }
      if (filters.from || filters.to) {
        // Calendar days belong to the event city, not the visitor's computer timezone.
        const windowParams = new URLSearchParams({ timezone: 'Asia/Kuala_Lumpur' });
        if (filters.from) windowParams.set('from', filters.from);
        if (filters.to) {
          const endDay = new Date(`${filters.to}T00:00:00Z`);
          endDay.setUTCDate(endDay.getUTCDate() + 1);
          windowParams.set('to', endDay.toISOString().slice(0, 10));
        }
        const window = await get<{ from: string | null; to: string | null }>(`/api/date-window?${windowParams}`, controller.signal);
        if (window.from) params.set('from', window.from);
        if (window.to) params.set('to', window.to);
      }
      const data = await get<EventPage>(`/api/events?${params}`, controller.signal);
      setResult(data);
    }
    load().catch(e => { if (!controller.signal.aborted) setError(e.message); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [category, filters, page, reload]);

  useEffect(() => {
    if (!selected) { dialog.current?.close(); return; }
    const controller = new AbortController();
    setDetail(undefined); setDetailError(''); dialog.current?.showModal();
    get<EventDetail>(`/api/events/${selected}`, controller.signal).then(setDetail)
      .catch(e => { if (!controller.signal.aborted) setDetailError(e.message); });
    return () => controller.abort();
  }, [selected]);

  function reset() { setCategory('all'); setDraft(initialFilters); setFilters(initialFilters); setPage(1); }
  function apply(event: FormEvent) { event.preventDefault(); setPage(1); setFilters({ ...draft }); }

  return <>
    <a className="skip-link" href="#events">Skip to events</a>
    <header className="site-header"><a className="wordmark" href="/" aria-label="Gather home">gather<span>✳</span></a>
      <nav aria-label="Main"><a href="#events">Discover events <span aria-hidden="true">↗</span></a></nav>
      <span className="demo-label">PORTFOLIO DEMO</span>
      <AuthControls signInRequest={signInRequest} onSessionChange={() => {
        if (returnToEvent.current) { setSelected(returnToEvent.current); returnToEvent.current = undefined; }
      }} />
    </header>
    <main>
      <section className="hero" aria-labelledby="hero-title">
        <div className="eyebrow"><span className="tiny-square" /> LESS SCROLLING. MORE SHOWING UP.</div>
        <div className="hero-row"><h1 id="hero-title">Find your next<br /><em>good plan.</em></h1>
          <div className="hero-aside"><span className="asterisk" aria-hidden="true">✳</span>
            <p>Small gatherings. Shared interests.<br />A reason to step outside.</p><a href="#events">Explore what’s happening <span aria-hidden="true">↓</span></a></div>
        </div>
        <div className="edition"><span>THE LOCAL EDITION</span><span>KUALA LUMPUR & GEORGE TOWN</span><span>NOVEMBER 2026</span></div>
      </section>

      <section id="events" className="catalogue" aria-labelledby="events-title">
        <div className="section-heading"><h2 id="events-title">Out there, waiting for you.</h2><span>01 / DISCOVER</span></div>
        <div className="category-tabs" role="group" aria-label="Event category">{categories.map(([id, label]) =>
          <button key={id} className={category === id ? 'active' : ''} aria-pressed={category === id}
            onClick={() => { setCategory(id); setPage(1); }}>{label}</button>)}</div>
        <form className="filters" onSubmit={apply}>
          <div className="filter-field"><label htmlFor="city-filter">City</label><select id="city-filter" value={draft.city} onChange={e => setDraft({ ...draft, city: e.target.value })}>
            <option value="">All cities</option><option>Kuala Lumpur</option><option>George Town</option></select></div>
          <div className="filter-field"><label htmlFor="price-filter">Price</label><select id="price-filter" value={draft.maxPrice} onChange={e => setDraft({ ...draft, maxPrice: e.target.value })}>
            <option value="">Any price</option><option value="0">Free</option><option value="3000">Up to RM 30</option>
            <option value="5000">Up to RM 50</option><option value="10000">Up to RM 100</option></select></div>
          <label>From<input type="date" value={draft.from} onChange={e => setDraft({ ...draft, from: e.target.value })} /></label>
          <label>Through<input type="date" value={draft.to} min={draft.from || undefined} onChange={e => setDraft({ ...draft, to: e.target.value })} /></label>
          <button className="primary" type="submit">Find events <span aria-hidden="true">↗</span></button>
        </form>
        <div className="results-meta"><span role="status">{loading ? 'Finding your next plan…' : error ? 'Events unavailable' : `${result?.total ?? 0} events to make time for`}</span>
          <button className="text-button" onClick={reset}>Clear filters</button></div>
        {error ? <div className="empty-state" role="alert"><h3>A small interruption.</h3><p>{error}</p><button className="primary" onClick={() => setReload(reload + 1)}>Try again</button></div>
          : loading ? <div className="loading-state" aria-hidden="true">Gathering the good stuff<span>…</span></div>
          : !result?.items.length ? <div className="empty-state"><h3>No plans here. Yet.</h3><p>Try another category, city or date.</p><button className="primary" onClick={reset}>See all events</button></div>
          : <div className="event-grid">{result.items.map(event => <article className="event-card" key={event.id}>
            <button className="event-link" onClick={() => setSelected(event.id)} aria-label={`View ${event.title}`}>
              <Poster event={event} /><div className="card-meta"><span>{event.category}</span><span>{date(event, { weekday: 'short', day: 'numeric', month: 'short' })}</span></div>
              <h3>{event.title}<span aria-hidden="true">↗</span></h3>
              <p className="venue">{event.venue} · {event.city}</p><div className="card-footer"><span>{price(event)}</span><span>View event</span></div>
            </button>
          </article>)}</div>}
        {!loading && !error && result && result.total > result.pageSize && <nav className="pagination" aria-label="Event pages">
          <button disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span>Page {page} of {Math.ceil(result.total / result.pageSize)}</span>
          <button disabled={page * result.pageSize >= result.total} onClick={() => setPage(page + 1)}>Next</button>
        </nav>}
      </section>
      <aside className="closing-note"><span aria-hidden="true">↗</span><p>The best part of a plan?<br /><em>The people you meet there.</em></p><span>SEE YOU OUT THERE.</span></aside>
    </main>
    <footer><a className="wordmark" href="/">gather<span>✳</span></a><p>Fictional events. Demo reservations only.</p><span>MADE FOR SHOWING UP.</span></footer>
    <dialog ref={dialog} onClose={() => setSelected(undefined)} aria-labelledby="detail-title">
      <button className="close-dialog" onClick={() => setSelected(undefined)} aria-label="Close event details">×</button>
      {detail ? <><Poster event={detail} /><div className="detail-content"><span className="eyebrow">{detail.category} / {detail.city}</span>
        <h2 id="detail-title">{detail.title}</h2><p>{detail.description}</p>
        <dl><div><dt>When</dt><dd>{date(detail, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', hour: '2-digit', minute: '2-digit' })}<small>{detail.timezone}</small></dd></div>
          <div><dt>Where</dt><dd>{detail.venue}, {detail.city}</dd></div><div><dt>Entry</dt><dd>{price(detail)}</dd></div></dl>
        <ReservationPanel key={detail.id} event={detail} onSignIn={() => {
          returnToEvent.current = detail.id; setSelected(undefined); setSignInRequest(value => value + 1);
        }} /></div></>
        : <div className="detail-content"><h2 id="detail-title">{detailError ? 'Unable to load event' : 'Loading event…'}</h2>{detailError && <p role="alert">{detailError}</p>}</div>}
    </dialog>
  </>;
}

export default function App() {
  const booking = window.location.pathname.match(/^\/bookings\/([0-9a-f-]{36})$/i);
  if (booking) return <BookingStatusPage bookingId={booking[1]} />;
  return window.location.pathname === '/organiser' ? <EventDashboard /> : <CataloguePage />;
}
