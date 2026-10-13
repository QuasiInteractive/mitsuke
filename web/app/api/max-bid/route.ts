import { API_URL } from "@/lib/api";

/** The lot page's max-bid calculator, forwarded to Mitsuke.Api (which stays private behind the web app). */
export async function GET(req: Request) {
  const q = new URL(req.url).searchParams;
  const id = q.get("id") ?? "";
  if (!/^[0-9a-f-]{36}$/i.test(id)) return Response.json({ error: "Unknown lot." }, { status: 404 });
  const params = new URLSearchParams({ budget: q.get("budget") ?? "", to: q.get("to") ?? "AU" });
  const res = await fetch(`${API_URL}/api/lots/${id}/max-bid?${params}`, { cache: "no-store" });
  return new Response(res.body, { status: res.status, headers: { "content-type": "application/json", "cache-control": "no-store" } });
}
