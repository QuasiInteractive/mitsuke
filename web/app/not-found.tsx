import Link from "next/link";
import { ArrowRight } from "lucide-react";

/** Every 404 in the family look, not the framework's white page. */
export default function NotFound() {
  return (
    <section className="relative isolate left-1/2 w-screen -translate-x-1/2 overflow-hidden py-24">
      <div className="grid-bg absolute inset-0 -z-10 opacity-70" />
      <div className="mx-auto max-w-6xl px-4 sm:px-6">
        <span className="hanko inline-block px-2 py-1 text-2xl">見つからない</span>
        <h1 className="display-xl mt-6">
          Nothing <span className="glow-red">here</span>
        </h1>
        <p className="mt-4 max-w-lg text-muted">
          That page doesn&apos;t exist, or the lot has gone. Japanese auction lots come and go every few days.
        </p>
        <div className="mt-8 flex flex-wrap gap-3">
          <Link href="/" className="btn-primary px-6 py-3.5">Back to Mitsuke <ArrowRight className="size-4" aria-hidden /></Link>
          <Link href="/market" className="btn-ghost px-5 py-3.5 text-text">Browse the market</Link>
        </div>
      </div>
    </section>
  );
}
