import Link from "next/link";

export default function LotNotFound() {
  return (
    <div className="card mx-auto mt-10 max-w-md p-8 text-center">
      <h1 className="text-xl font-semibold">That lot isn&apos;t here</h1>
      <p className="mt-2 text-sm text-muted">It may have been removed by the auction house, or the link is wrong.</p>
      <Link href="/" className="mt-6 inline-block rounded-xl bg-accent px-5 py-2.5 font-semibold">Back to your matches</Link>
    </div>
  );
}
