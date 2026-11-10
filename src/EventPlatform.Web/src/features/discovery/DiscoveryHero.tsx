import { useState } from 'react';

const passes = [
  { kind: 'music', title: 'SIDE B.', number: '01', label: 'MUSIC' },
  { kind: 'workshop', title: 'MAKE IT.', number: '02', label: 'WORKSHOPS' },
  { kind: 'community', title: 'COME BY.', number: '03', label: 'COMMUNITY' },
];

export function DiscoveryHero() {
  const [paused, setPaused] = useState(false);

  return (
    <section className="hero" aria-labelledby="hero-title">
      <p className="hero-kicker"><span aria-hidden="true" /> Kuala Lumpur &amp; George Town</p>
      <div className="hero-title-wrap">
        <h1 id="hero-title">
          <span className="hero-line"><span>Good plans.</span></span>{' '}
          <span className="hero-line"><span><em>Better</em> together.</span></span>
        </h1>
        <div className={`pass-stack${paused ? ' is-paused' : ''}`} aria-hidden="true">
          {passes.map(pass => (
            <div className={`hero-pass hero-pass-${pass.kind}`} key={pass.kind}>
              <span className="pass-meta"><span>GATHER</span><span>{pass.number}</span></span>
              <strong>{pass.title}</strong>
              <span className="pass-stub">{pass.label}<span>↗</span></span>
            </div>
          ))}
        </div>
      </div>
      <p className="hero-description">Discover local gigs, hands-on workshops and good company. Pick a plan, grab a ticket and show up.</p>
      <div className="hero-actions">
        <a className="primary hero-cta" href="#events"><span>Explore events</span><span aria-hidden="true">↓</span></a>
      </div>
      <div className="hero-footer">
        <span>THE LOCAL EDITION</span>
        <button className="motion-toggle" type="button" onClick={() => setPaused(value => !value)}>
          <span aria-hidden="true">{paused ? '▷' : 'Ⅱ'}</span> {paused ? 'Play motion' : 'Pause motion'}
        </button>
        <span>GOOD TO GET TOGETHER.</span>
      </div>
    </section>
  );
}
