import { useState, type FormEvent } from 'react';
import { ApiError, mutate } from '../../api';

type SearchResult = {
  status: string;
  filter: { category: string | null; city: string | null; maxPriceMinor: number | null; keywords: string | null } | null;
  from: string | null;
  through: string | null;
};

export function AiSearchBox({ city, onApply }: { city: string; onApply: (result: SearchResult) => void }) {
  const [query, setQuery] = useState('');
  const [pending, setPending] = useState(false);
  const [notice, setNotice] = useState('');

  async function interpret(event: FormEvent) {
    event.preventDefault();
    if (pending || !query.trim()) return;
    setPending(true);
    setNotice('');
    try {
      const result = await mutate<SearchResult>('/api/search/interpret', { query: query.trim(), city: city || null });
      if (result.status === 'ok' && result.filter) {
        onApply(result);
        setNotice('Search filters applied. You can adjust them below.');
      } else {
        setNotice(result.status === 'allowance'
          ? 'The AI search allowance has been reached. Use the filters below.'
          : result.status === 'unsupported'
            ? 'Try a search about events in Kuala Lumpur or George Town, or use the filters below.'
            : 'AI search is unavailable right now. Use the filters below.');
      }
    } catch (error) {
      setNotice(error instanceof ApiError && error.status === 401
        ? 'Sign in to use AI search. You can browse with the filters below.'
        : 'AI search is unavailable right now. Use the filters below.');
    } finally {
      setPending(false);
    }
  }

  return <form className="ai-search" onSubmit={interpret} aria-busy={pending}>
    <label htmlFor="event-ai-query">Describe what you’re looking for</label>
    <div>
      <input id="event-ai-query" value={query} maxLength={512} required disabled={pending}
        onChange={event => setQuery(event.target.value)} placeholder="Free workshops this weekend" />
      <button className="primary" disabled={pending || !query.trim()} type="submit">
        {pending ? 'Interpreting…' : 'Search with AI'}
      </button>
    </div>
    <p>AI suggests filters for this catalogue. Search text is sent to OpenAI.</p>
    <p role="status">{notice}</p>
  </form>;
}
