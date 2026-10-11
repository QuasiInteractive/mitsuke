"use client";

import { useState } from "react";
import { createClient } from "@/lib/supabase/client";

export function LoginForm() {
  const [state, setState] = useState<{ kind: "idle" | "sending" | "sent" } | { kind: "error"; message: string }>({ kind: "idle" });

  async function submit(form: FormData) {
    setState({ kind: "sending" });
    const email = String(form.get("email") ?? "").trim();
    const { error } = await createClient().auth.signInWithOtp({
      email,
      options: { emailRedirectTo: `${window.location.origin}/auth/callback?next=/watchlists` },
    });
    setState(error ? { kind: "error", message: error.message } : { kind: "sent" });
  }

  if (state.kind === "sent") {
    return (
      <div className="mt-6 rounded-xl border border-good/40 bg-good/10 p-4 text-sm">
        Check your email for a sign-in link. You can close this tab.
      </div>
    );
  }

  return (
    <form action={submit} className="mt-6 space-y-3">
      <label className="block">
        <span className="label">Email</span>
        <input name="email" type="email" required autoComplete="email" autoFocus placeholder="you@example.com" className="field" />
      </label>
      {state.kind === "error" && <p className="text-sm text-accent">{state.message}</p>}
      <button disabled={state.kind === "sending"} className="btn-primary w-full py-4">
        {state.kind === "sending" ? "Sending…" : "Email me a sign-in link"}
      </button>
    </form>
  );
}
