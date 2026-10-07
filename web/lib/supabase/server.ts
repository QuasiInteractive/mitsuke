import "server-only";
import { cookies } from "next/headers";
import { connection } from "next/server";
import { createServerClient } from "@supabase/ssr";

/** Supabase client bound to this request's cookies (server components, actions, route handlers). */
export async function createClient() {
  const store = await cookies();
  return createServerClient(process.env.NEXT_PUBLIC_SUPABASE_URL!, process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY!, {
    cookies: {
      getAll: () => store.getAll(),
      setAll: (toSet) => {
        try {
          toSet.forEach(({ name, value, options }) => store.set(name, value, options));
        } catch {
          // Server components can't set cookies; the proxy refreshes the session on every request instead.
        }
      },
    },
  });
}

export type SignedInUser = { id: string; email: string; accessToken: string };

/**
 * The signed-in person, or null. getClaims() verifies the token's signature against Supabase's published keys,
 * so the email shown is trustworthy; Mitsuke.Api verifies the same token again on every call it receives.
 */
export async function getUser(): Promise<SignedInUser | null> {
  // Per-request by nature (cookies, token expiry checks against the clock): never part of a prerendered shell.
  await connection();
  const supabase = await createClient();
  const { data } = await supabase.auth.getClaims();
  if (!data?.claims?.sub) return null;
  const { data: session } = await supabase.auth.getSession();
  if (!session.session) return null;
  return { id: data.claims.sub, email: String(data.claims.email ?? ""), accessToken: session.session.access_token };
}
