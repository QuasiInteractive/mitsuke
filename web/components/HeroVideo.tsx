"use client";

import { useSyncExternalStore } from "react";

const noopSubscribe = () => () => {};

/** A still frame instead of video for people who asked for less motion or less data. */
function wantsStill(): boolean {
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const saveData = (navigator as Navigator & { connection?: { saveData?: boolean } }).connection?.saveData === true;
  return reduced || saveData;
}

/**
 * Blurred night footage behind the home hero, looping, as on Kensa-ya. Muted and inline so phones autoplay it;
 * scaled up a little so the blurred edges never show; darkened so the headline stays readable.
 * public/video/hero.mp4 is a 960 px, silent, ~1.7 MB web copy of the original clip.
 */
export function HeroVideo() {
  const still = useSyncExternalStore(noopSubscribe, wantsStill, () => false);
  return (
    <div className="pointer-events-none absolute inset-0 -z-20 overflow-hidden" aria-hidden>
      {still ? (
        // eslint-disable-next-line @next/next/no-img-element -- local poster, decorative
        <img src="/video/hero-poster.jpg" alt="" className="hero-media" />
      ) : (
        <video className="hero-media" autoPlay muted loop playsInline preload="metadata" poster="/video/hero-poster.jpg">
          <source src="/video/hero.mp4" type="video/mp4" />
        </video>
      )}
      <div className="absolute inset-0 bg-[linear-gradient(90deg,rgb(7_8_13/0.92)_0%,rgb(7_8_13/0.72)_45%,rgb(7_8_13/0.45)_100%)]" />
      <div className="absolute inset-x-0 bottom-0 h-40 bg-gradient-to-t from-ink to-transparent" />
    </div>
  );
}
