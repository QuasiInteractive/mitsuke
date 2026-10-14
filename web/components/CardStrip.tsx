"use client";

import { useEffect, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, LayoutGrid, Rows3 } from "lucide-react";

/**
 * A row of cards that scrolls sideways (swipe on a phone, arrows on a desktop, where a mouse wheel can't scroll
 * sideways), with "Show all" to lay every card out in a grid instead. Arrows only show when there's more to see.
 */
export function CardStrip({ children, count, label }: { children: React.ReactNode; count: number; label: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const [all, setAll] = useState(false);
  const [edges, setEdges] = useState({ start: true, end: true });

  useEffect(() => {
    const el = ref.current;
    if (!el || all) return;
    const update = () => setEdges({ start: el.scrollLeft <= 4, end: el.scrollLeft + el.clientWidth >= el.scrollWidth - 4 });
    update();
    el.addEventListener("scroll", update, { passive: true });
    window.addEventListener("resize", update);
    return () => {
      el.removeEventListener("scroll", update);
      window.removeEventListener("resize", update);
    };
  }, [all, count]);

  const page = (dir: 1 | -1) => ref.current?.scrollBy({ left: dir * ref.current.clientWidth * 0.85, behavior: "smooth" });
  const overflowing = !(edges.start && edges.end);

  return (
    <div>
      <div className="mb-3 flex items-center justify-end gap-2">
        {!all && overflowing && (
          <>
            <button type="button" onClick={() => page(-1)} disabled={edges.start} aria-label={`Scroll ${label} left`}
              className="grid size-9 place-items-center rounded-full border border-hairline bg-black/40 text-text transition hover:border-white/25 disabled:opacity-30">
              <ChevronLeft className="size-4" aria-hidden />
            </button>
            <button type="button" onClick={() => page(1)} disabled={edges.end} aria-label={`Scroll ${label} right`}
              className="grid size-9 place-items-center rounded-full border border-hairline bg-black/40 text-text transition hover:border-white/25 disabled:opacity-30">
              <ChevronRight className="size-4" aria-hidden />
            </button>
          </>
        )}
        {(overflowing || all) && count > 1 && (
          <button type="button" onClick={() => setAll((a) => !a)} className="btn-ghost px-3 py-1.5 text-xs" aria-pressed={all}>
            {all ? <Rows3 className="size-3.5" aria-hidden /> : <LayoutGrid className="size-3.5" aria-hidden />}
            {all ? "Show as a row" : `Show all ${count}`}
          </button>
        )}
      </div>
      <div
        ref={ref}
        className={
          all
            ? "grid gap-4 sm:grid-cols-2 lg:grid-cols-3 [&>*]:!w-auto"
            : "strip-scroll -mx-1 flex snap-x snap-mandatory gap-4 overflow-x-auto overscroll-x-contain px-1 pb-3 [&>*]:snap-start"
        }
      >
        {children}
      </div>
    </div>
  );
}
