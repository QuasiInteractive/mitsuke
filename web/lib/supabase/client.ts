import { createBrowserClient } from "@supabase/ssr";

/** Supabase client for the browser (the sign-in form). */
export function createClient() {
  return createBrowserClient(process.env.NEXT_PUBLIC_SUPABASE_URL!, process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY!);
}
