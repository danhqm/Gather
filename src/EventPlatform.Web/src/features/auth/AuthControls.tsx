import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { mutate } from '../../api';
import type { Session } from '../../api';

export function AuthControls({ onSessionChange, signInRequest = 0 }: { onSessionChange?: () => void; signInRequest?: number }) {
  const [session, setSession] = useState<Session | null>(null);
  const [mode, setMode] = useState<'login' | 'register'>();
  const [error, setError] = useState('');
  const [pending, setPending] = useState(false);
  const [ready, setReady] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  const form = useRef<HTMLFormElement>(null);
  useEffect(() => { if (signInRequest > 0) setMode('login'); }, [signInRequest]);

  useEffect(() => {
    const controller = new AbortController();
    fetch('/api/auth/me', { signal: controller.signal }).then(async response => {
      if (response.status === 401) return null;
      if (!response.ok) throw new Error('Unable to check your account. Refresh to try again.');
      return response.json() as Promise<Session>;
    }).then(setSession).catch(e => { if (!controller.signal.aborted) setError(e.message); })
      .finally(() => { if (!controller.signal.aborted) setReady(true); });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    setError('');
    if (mode) dialog.current?.showModal();
    else { dialog.current?.close(); form.current?.reset(); }
  }, [mode]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setPending(true); setError('');
    const fields = new FormData(event.currentTarget);
    try {
      const user = await mutate<Session>(`/api/auth/${mode}`, {
        email: fields.get('email'), password: fields.get('password'), displayName: fields.get('displayName'),
      });
      setSession(user); setMode(undefined); onSessionChange?.();
    } catch (e) { setError(e instanceof Error ? e.message : 'Please try again.'); }
    finally { setPending(false); }
  }

  async function logout() {
    setPending(true); setError('');
    try { await mutate('/api/auth/logout', {}); setSession(null); onSessionChange?.(); }
    catch (e) { setError(e instanceof Error ? e.message : 'Please try again.'); }
    finally { setPending(false); }
  }

  return <div className="auth-controls">
    {session && <a className="organiser-link" href="/tickets">My tickets</a>}
    {session?.memberships.some(m => ['Organiser', 'CheckInStaff'].includes(m.role)) && <a className="organiser-link" href="/check-in">Check in</a>}
    {session?.memberships.some(m => m.role === 'Organiser') && <a className="organiser-link" href="/organiser">Manage events</a>}
    {session ? <><span className="account-name" title={session.displayName}>{session.displayName}</span>
      <button className="account-button" disabled={pending} onClick={logout}>Sign out</button></>
      : <button className="account-button" disabled={!ready} onClick={() => setMode('login')}>Sign in</button>}
    {error && !mode && <span className="account-error" role="alert">{error}</span>}
    <dialog ref={dialog} className="auth-dialog" onClose={() => setMode(undefined)} aria-labelledby="auth-title">
      <button className="close-dialog" aria-label="Close sign in" onClick={() => setMode(undefined)}>×</button>
      <div className="detail-content">
        <span className="eyebrow">YOUR NEXT GOOD PLAN STARTS HERE</span>
        <h2 id="auth-title">{mode === 'register' ? 'Make yourself at home.' : 'Good to see you.'}</h2>
        <p>{mode === 'register' ? 'Create your Gather account.' : 'Sign in to your Gather account.'}</p>
        <form ref={form} className="account-form" onSubmit={submit}>
          {mode === 'register' && <label>Your name<input name="displayName" autoComplete="name" required maxLength={80} /></label>}
          <label>Email address<input name="email" type="email" autoComplete="username" required maxLength={254} /></label>
          <label>Password<input name="password" type="password" autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
            required minLength={mode === 'register' ? 12 : undefined} maxLength={128} aria-describedby={mode === 'register' ? 'password-help' : undefined} /></label>
          {mode === 'register' && <small id="password-help">12–128 characters, with uppercase, lowercase, a number and a symbol.</small>}
          {error && <p className="form-error" role="alert">{error}</p>}
          <button className="primary" disabled={pending} type="submit">{pending ? 'Please wait…' : mode === 'register' ? 'Create account' : 'Sign in'}</button>
        </form>
        <button className="text-button" disabled={pending} onClick={() => setMode(mode === 'register' ? 'login' : 'register')}>
          {mode === 'register' ? 'Already have an account? Sign in' : 'New here? Create an account'}</button>
      </div>
    </dialog>
  </div>;
}
