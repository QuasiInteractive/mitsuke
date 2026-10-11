/** The hanko seal: 見 in a slightly rounded crimson square, like a stamped name seal. */
export function HankoMark({ className = "size-9" }: { className?: string }) {
  return (
    <span
      aria-hidden
      className={`grid shrink-0 place-items-center rounded-[0.6rem] bg-accent font-jp text-lg font-bold text-white shadow-[inset_0_1px_0_rgb(255_255_255/0.25),0_8px_24px_-10px_var(--color-accent)] ${className}`}
    >
      見
    </span>
  );
}

/**
 * The red sun over Mt Fuji, drawn faintly behind a hero number (the R32 reference's landed-cost card).
 * Decorative only: it sits behind content and never carries meaning.
 */
export function FujiArt({ className = "" }: { className?: string }) {
  return (
    <svg aria-hidden viewBox="0 0 220 130" className={`pointer-events-none ${className}`} fill="none">
      <circle cx="150" cy="44" r="34" fill="var(--color-accent)" opacity="0.55" />
      <path d="M40 130 L112 46 Q118 40 124 46 L196 130 Z" fill="#2a2a33" opacity="0.9" />
      <path d="M97 64 L112 46 Q118 40 124 46 L139 64 L131 60 L124 66 L118 59 L111 66 L104 60 Z" fill="#d4d4d8" opacity="0.55" />
    </svg>
  );
}

/** A thin ring that fills to the score, crimson: the deal score at a glance. */
export function ScoreRing({ score, className = "size-14" }: { score: number; className?: string }) {
  const r = 22;
  const c = 2 * Math.PI * r;
  return (
    <svg viewBox="0 0 52 52" className={className} aria-hidden>
      <circle cx="26" cy="26" r={r} stroke="rgb(255 255 255 / 0.08)" strokeWidth="5" fill="none" />
      <circle
        cx="26"
        cy="26"
        r={r}
        stroke="var(--color-accent)"
        strokeWidth="5"
        strokeLinecap="round"
        fill="none"
        strokeDasharray={`${(Math.max(0, Math.min(100, score)) / 100) * c} ${c}`}
        transform="rotate(-90 26 26)"
      />
    </svg>
  );
}
