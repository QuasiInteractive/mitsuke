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

function watchlistBody(form: FormData) {
  const num = (k: string) => {
    const v = String(form.get(k) ?? "").replace(/[^\d.]/g, "");
    return v === "" ? null : Number(v);
  };
  return {
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
  };
}

async function formErrors(res: Response): Promise<FormState> {
  const body = await res.json().catch(() => null);
  return { errors: body?.errors, message: body?.errors?.[""]?.[0] ?? "Couldn't save that. Try again." };
}

export async function createWatchlist(_prev: FormState, form: FormData): Promise<FormState> {
  const res = await sendAs(await requireToken(), "POST", "/api/me/watchlists", watchlistBody(form));
  if (res.status === 201) redirect("/watchlists");
  return formErrors(res);
}

/** Bound to the watchlist's id by the edit page. */
export async function updateWatchlist(id: string, _prev: FormState, form: FormData): Promise<FormState> {
  if (!uuid.test(id)) return { message: "That watchlist doesn't exist." };
  const res = await sendAs(await requireToken(), "PUT", `/api/me/watchlists/${id}`, watchlistBody(form));
  if (res.status === 204) redirect("/watchlists");
  if (res.status === 404) return { message: "That watchlist doesn't exist any more." };
  return formErrors(res);
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

export type BrowserSubscription = { endpoint: string; keys: { p256dh: string; auth: string } };

/** Saves this device's push subscription for the signed-in person. Returns false if the API refused it. */
export async function savePushSubscription(sub: BrowserSubscription): Promise<boolean> {
  const token = await requireToken();
  const res = await sendAs(token, "PUT", "/api/me/push-subscriptions", {
    endpoint: sub?.endpoint,
    keys: { p256dh: sub?.keys?.p256dh, auth: sub?.keys?.auth },
  });
  return res.ok;
}

export async function removePushSubscription(endpoint: string) {
  if (typeof endpoint !== "string") return;
  await sendAs(await requireToken(), "POST", "/api/me/push-subscriptions/remove", { endpoint });
}

export async function sendTestPush(): Promise<{ delivered: number } | { error: string }> {
  const res = await sendAs(await requireToken(), "POST", "/api/me/push-subscriptions/test");
  if (res.status === 429) return { error: "Slow down: try again in a minute." };
  if (!res.ok) return { error: "Couldn't send a test right now." };
  const body = (await res.json()) as { delivered: number };
  return { delivered: body.delivered };
}
