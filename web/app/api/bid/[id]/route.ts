import { API_URL } from "@/lib/api";

/**
 * Forwards a bid request from the browser to Mitsuke.Api, which stays private behind the web app.
 * Passes the caller's IP so the API's per-client rate limit still applies.
 */
export async function POST(req: Request, ctx: RouteContext<"/api/bid/[id]">) {
  const { id } = await ctx.params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) return Response.json({ error: "Unknown lot." }, { status: 404 });

  const res = await fetch(`${API_URL}/api/lots/${id}/bid-requests`, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      "x-forwarded-for": req.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ?? "unknown",
    },
    body: await req.text(),
  });
  return new Response(res.body, { status: res.status, headers: { "content-type": res.headers.get("content-type") ?? "application/json" } });
}
