import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { request } from '@playwright/test';

if (!process.argv.includes('--live')) {
  console.error('Pass --live to run one paid API smoke case, or --live --all for the labelled evaluation.');
  process.exit(1);
}

const cases = JSON.parse(await readFile(new URL('../../evals/event-search.json', import.meta.url), 'utf8'));
const selected = process.argv.includes('--all') ? cases : cases.slice(0, 1);
const client = await request.newContext({ baseURL: 'http://localhost:8080', timeout: 25_000 });

async function post(path, data) {
  const csrf = await client.get('/api/auth/csrf');
  if (!csrf.ok()) throw new Error(`CSRF request returned HTTP ${csrf.status()}`);
  return client.post(path, { data, headers: { 'X-CSRF-TOKEN': (await csrf.json()).token } });
}

function resolveDate(value) {
  if (!['tomorrow', 'saturday', 'sunday'].includes(value)) return value ?? null;
  const today = new Intl.DateTimeFormat('sv-SE', { timeZone: 'Asia/Kuala_Lumpur' }).format(new Date());
  const day = new Date(`${today}T00:00:00Z`);
  const weekday = day.getUTCDay();
  const saturday = weekday === 0 ? -1 : 6 - weekday;
  day.setUTCDate(day.getUTCDate() + (value === 'tomorrow' ? 1 : saturday + (value === 'sunday' ? 1 : 0)));
  return day.toISOString().slice(0, 10);
}

try {
  const id = randomUUID();
  const signup = await post('/api/auth/register', {
    email: `search-eval-${id}@example.test`, displayName: 'Search evaluation', password: `${id}Aa!9`,
  });
  if (!signup.ok()) throw new Error(`Evaluation signup returned HTTP ${signup.status()}`);
  let passed = 0;
  let completed = 0;
  let consecutiveUnavailable = 0;
  for (const { query, expected } of selected) {
    const started = performance.now();
    const response = await post('/api/search/interpret', { query, city: null });
    const elapsed = Math.round(performance.now() - started);
    if (!response.ok()) throw new Error(`Interpret returned HTTP ${response.status()}`);
    const result = await response.json();
    consecutiveUnavailable = result.status === 'unavailable' ? consecutiveUnavailable + 1 : 0;
    if (result.status === 'allowance' || consecutiveUnavailable >= 3 ||
        (completed === 0 && result.status === 'unavailable')) {
      console.error(`Stopped at case ${completed + 1}: ${result.status}`);
      break;
    }
    const fields = ['category', 'city', 'maxPriceMinor', 'keywords'];
    const same = expected === null
      ? result.status === 'unsupported' && !result.filter
      : result.status === 'ok' && fields.every(field =>
        (typeof result.filter[field] === 'string' ? result.filter[field].toLowerCase() : result.filter[field])
        === (typeof expected[field] === 'string' ? expected[field].toLowerCase() : expected[field] ?? null))
        && result.from === resolveDate(expected.from) && result.through === resolveDate(expected.through);
    completed++;
    if (same) passed++;
    console.log(`${same ? 'PASS' : 'FAIL'} ${elapsed}ms | ${query}`);
    if (!same) console.log(JSON.stringify(result));
  }
  console.log(`${passed}/${completed} completed cases passed (${selected.length} requested).`);
  if (passed !== selected.length) process.exitCode = 1;
} finally {
  await client.dispose();
}
