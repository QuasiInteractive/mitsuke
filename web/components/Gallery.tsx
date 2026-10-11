"use client";

import { useState } from "react";
import { ImageOff } from "lucide-react";

/**
 * The hero: the car's real auction photos. Auction shots are plain and often small, so they sit on a dark
 * stage with a soft vignette rather than being stretched into something they're not.
 */
export function Gallery({ photos, title }: { photos: string[]; title: string }) {
  const [index, setIndex] = useState(0);
  const [failed, setFailed] = useState(false);
  if (photos.length === 0 || failed) {
    return (
      <div className="card flex aspect-[4/3] flex-col items-center justify-center gap-2 p-6 text-center">
        <ImageOff className="size-8 text-faint" aria-hidden />
        <p className="text-sm text-muted">{photos.length === 0 ? "No photos published for this lot yet" : "Photos are removed once the auction is over"}</p>
      </div>
    );
  }
  const go = (delta: number) => setIndex((i) => (i + delta + photos.length) % photos.length);

  return (
    <div>
      <div className="relative overflow-hidden rounded-[1.5rem] border border-hairline bg-black">
        {/* eslint-disable-next-line @next/next/no-img-element -- remote auction photos via redirecting, signed URLs */}
        <img
          src={photos[index]}
          alt={`${title}, photo ${index + 1} of ${photos.length}`}
          className="aspect-[4/3] w-full object-contain"
          onError={() => index === 0 && setFailed(true)}
        />
        <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_at_center,transparent_55%,rgb(0_0_0/0.55))]" />
        {photos.length > 1 && (
          <>
            <button aria-label="Previous photo" onClick={() => go(-1)} className="absolute top-1/2 left-3 grid size-10 -translate-y-1/2 place-items-center rounded-full bg-black/55 text-xl backdrop-blur hover:bg-black/75">‹</button>
            <button aria-label="Next photo" onClick={() => go(1)} className="absolute top-1/2 right-3 grid size-10 -translate-y-1/2 place-items-center rounded-full bg-black/55 text-xl backdrop-blur hover:bg-black/75">›</button>
          </>
        )}
        <span className="tabular absolute right-3 bottom-3 rounded-full bg-black/60 px-3 py-1 text-xs backdrop-blur">
          {index + 1} / {photos.length}
        </span>
      </div>
      {photos.length > 1 && (
        <div className="no-scrollbar mt-3 flex gap-2 overflow-x-auto">
          {photos.map((src, i) => (
            <button
              key={src}
              onClick={() => setIndex(i)}
              aria-label={`Show photo ${i + 1}`}
              className={`shrink-0 overflow-hidden rounded-xl border-2 ${i === index ? "border-accent" : "border-transparent opacity-60 hover:opacity-100"}`}
            >
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={src} alt="" className="h-16 w-24 bg-black object-cover" loading="lazy" />
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
