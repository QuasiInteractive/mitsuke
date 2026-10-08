import { API_URL } from "@/lib/api";

const KINDS = new Set(["makes", "models", "generations"]);

/**
 * The new-watchlist form's dropdown data (makes, models, generations), forwarded from Mitsuke.Api, which stays
 * private behind the web app. The same for everyone, so browsers may cache it briefly.
 */
export async function GET(req: Request, ctx: RouteContext<"/api/catalog/[kind]">) {
  const { kind } = await ctx.params;
  if (!KINDS.has(kind)) return Response.json({ error: "Unknown catalogue." }, { status: 404 });

  const res = await fetch(`${API_URL}/api/catalog/${kind}${new URL(req.url).search}`, { cache: "no-store" });
  return new Response(res.body, {
    status: res.status,
    headers: { "content-type": "application/json", "cache-control": res.ok ? "public, max-age=600" : "no-store" },
  });
}
