import Link from "next/link";
import { getUser } from "@/lib/supabase/server";

/** Reads the session, so it always renders inside a <Suspense> boundary. */
export async function UserMenu() {
  const user = await getUser();
  if (!user) {
    return (
      <Link href="/login" className="btn-primary rounded-full px-4 py-1.5 text-sm">
        Sign in
      </Link>
    );
  }
  return (
    <div className="flex items-center gap-1">
      <Link href="/watchlists" className="rounded-full px-3 py-1.5 transition hover:bg-white/5 hover:text-text">Watchlists</Link>
      <form action="/auth/signout" method="post">
        <button title={user.email} className="rounded-full px-3 py-1.5 transition hover:bg-white/5 hover:text-text">Sign out</button>
      </form>
    </div>
  );
}
