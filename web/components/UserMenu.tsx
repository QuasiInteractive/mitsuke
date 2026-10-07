import Link from "next/link";
import { getUser } from "@/lib/supabase/server";

/** Reads the session, so it always renders inside a <Suspense> boundary. */
export async function UserMenu() {
  const user = await getUser();
  if (!user) {
    return (
      <Link href="/login" className="rounded-full bg-accent px-4 py-1.5 font-medium text-text hover:bg-accent-strong">
        Sign in
      </Link>
    );
  }
  return (
    <div className="flex items-center gap-1">
      <Link href="/watchlists" className="rounded-full px-3 py-1.5 hover:bg-raised hover:text-text">Watchlists</Link>
      <form action="/auth/signout" method="post">
        <button title={user.email} className="rounded-full px-3 py-1.5 hover:bg-raised hover:text-text">Sign out</button>
      </form>
    </div>
  );
}
