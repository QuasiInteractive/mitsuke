import type { ImportEligibility } from "@/lib/api";

type Step = { title: string; detail: string };

const PARTNER_LIVE = process.env.NEXT_PUBLIC_BID_PARTNER_LIVE === "true";

// The buying part is the same wherever the car is going: Japanese auctions are dealer-only.
const BUY: Step[] = [
  {
    title: PARTNER_LIVE ? "Tap “I want to bid” and set your maximum" : "Request a bid and set your maximum",
    detail: "Japanese auctions are dealer-only, so a licensed exporter bids for you. Mitsuke passes your request on; it never bids or holds money.",
  },
  {
    title: "Pay the exporter's deposit",
    detail: "Exporters usually take a refundable deposit before they bid. Get the fees and refund terms in writing first.",
  },
  {
    title: "Win, then pay the balance",
    detail: "The exporter pays the auction, deregisters the car in Japan and books shipping. If you're outbid, the deposit comes back.",
  },
];

function stepsFor(e: ImportEligibility, builtYear: number | null): { lead: string; steps: Step[]; blocked: boolean } {
  const byName = (s: string) => e.pathways.find((p) => p.name.toLowerCase().includes(s));
  const old = byName("25-year");
  const sevs = byName("sevs");

  if (e.destination === "AU" && old?.verdict === "Yes") {
    return {
      blocked: false,
      lead: "It's old enough for the 25-year rule, the simplest way into Australia.",
      steps: [
        { title: "Apply for import approval (ROVER)", detail: "Apply for a Vehicle Import Approval before the car ships. Assessment can take weeks, so start as soon as you're serious." },
        ...BUY,
        { title: "Shipping and biosecurity", detail: "Cars from Japan need clean-down and, from September to April, stink-bug (BMSB) treatment before or on arrival." },
        { title: "Customs: duty, GST and any luxury car tax", detail: "A customs broker lodges the import and pays duty, 10% GST and, above the threshold, luxury car tax. The landed estimate above includes these." },
        { title: "Roadworthy and registration", detail: "A 25-year car doesn't need RAW compliance, but your state inspects it before it's registered." },
      ],
    };
  }

  if (e.destination === "AU" && sevs) {
    const until = builtYear ? ` It won't qualify under the 25-year rule until about ${builtYear + 25}.` : "";
    return {
      blocked: true,
      lead: `Only possible if this exact model and variant is on the SEVS register.${until} If it isn't listed, it can't be imported yet.`,
      steps: [
        { title: "Check the SEVS register first", detail: "Search the register (link below) for this make, model and variant. Not listed means stop here, before any deposit." },
        { title: "Find a RAW workshop", detail: "SEVS cars are complied by a Registered Automotive Workshop, which usually handles the import approval too. Ask them before you bid." },
        ...BUY,
        { title: "Shipping, biosecurity and customs", detail: "As for any import: BMSB treatment in season, then duty, GST and any luxury car tax through a customs broker." },
        { title: "RAW compliance, then registration", detail: "The workshop brings it up to the Australian Design Rules; then your state registers it. Compliance costs are on top of the landed estimate." },
      ],
    };
  }

  return {
    blocked: e.verdict === "No",
    lead: e.verdict === "Yes" ? `It can be imported to ${e.destination}.` : `Check the rules for ${e.destination} first: ${e.headline.toLowerCase()}.`,
    steps: [
      { title: "Confirm it can be imported", detail: "Use the official links below, or ask an importer, before paying anything." },
      ...BUY,
      { title: "Shipping, customs and registration", detail: "Your importer or customs broker clears the car; then it's inspected and registered where you live." },
    ],
  };
}

/** "How to get this car": the path from this lot to a registered car, worked out from its import verdict. */
export function HowToGetIt({ eligibility, builtYear }: { eligibility: ImportEligibility; builtYear: number | null }) {
  const { lead, steps, blocked } = stepsFor(eligibility, builtYear);
  return (
    <section className={`card p-5 ${blocked ? "border-amber/40" : ""}`}>
      <p className="label">How to get this car</p>
      <p className={`mt-2 text-sm ${blocked ? "text-amber" : "text-muted"}`}>{lead}</p>
      <ol className="mt-4 space-y-4">
        {steps.map((s, i) => (
          <li key={s.title} className="flex gap-3">
            <span className="grid size-7 shrink-0 place-items-center rounded-full border border-hairline bg-black/30 text-xs font-bold tabular">{i + 1}</span>
            <div>
              <p className="text-sm font-semibold">{s.title}</p>
              <p className="mt-0.5 text-sm text-muted">{s.detail}</p>
            </div>
          </li>
        ))}
      </ol>
      <p className="mt-4 text-xs text-faint">A general guide, not legal advice. Rules change: confirm with the official links above and your importer before bidding.</p>
    </section>
  );
}
