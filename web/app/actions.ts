"use server";

import { redirect } from "next/navigation";
import { refresh } from "next/cache";
import { getUser } from "@/lib/supabase/server";
import { sendAs, type FeedbackKind } from "@/lib/api";

async function requireToken(): Promise<string> {
  const user = await getUser();
  if (!user) redirect("/login");
  return user.accessToken;
}

const uuid = /^[0-9a-f-]{36}$/i;

export type FormState = { errors?: Record<string, string[]>; message?: string } | undefined;

export async function createWatchlist(_prev: FormState, form: FormData): Promise<FormState> {
  const token = await requireToken();
  const num = (k: string) => {
    const v = String(form.get(k) ?? "").replace(/[^\d.]/g, "");
    return v === "" ? null : Number(v);
  };
  const res = await sendAs(token, "POST", "/api/me/watchlists", {
    name: form.get("name"),
    make: form.get("make"),
    model: form.get("model"),
    modelCodes: String(form.get("modelCodes") ?? "").split(",").map((c) => c.trim()).filter(Boolean),
    yearFrom: num("yearFrom"),
    yearTo: num("yearTo"),
    maxMileageKm: num("maxMileageKm"),
    minGrade: num("minGrade"),
    includeRepaired: form.get("includeRepaired") === "on",
    destination: form.get("destination"),
    maxLandedAmount: num("maxLandedAmount"),
  });
  if (res.status === 201) redirect("/watchlists");
  const body = await res.json().catch(() => null);
  return { errors: body?.errors, message: body?.errors?.[""]?.[0] ?? (res.ok ? undefined : "Couldn't save that. Try again.") };
}

export async function setWatchlistActive(id: string, isActive: boolean) {
  if (!uuid.test(id)) return;
  await sendAs(await requireToken(), "PATCH", `/api/me/watchlists/${id}`, { isActive });
  refresh();
}

export async function deleteWatchlist(id: string) {
  if (!uuid.test(id)) return;
  await sendAs(await requireToken(), "DELETE", `/api/me/watchlists/${id}`);
  refresh();
}

export async function setFeedback(lotId: string, kind: FeedbackKind | null, reason?: string) {
  if (!uuid.test(lotId)) return;
  const token = await requireToken();
  if (kind === null) await sendAs(token, "DELETE", `/api/me/lots/${lotId}/feedback`);
  else await sendAs(token, "PUT", `/api/me/lots/${lotId}/feedback`, { kind, reason: reason ?? null });
  refresh();
}
