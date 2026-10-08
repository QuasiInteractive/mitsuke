"use client";

import { useActionState, useState } from "react";
import type { FormState } from "@/app/actions";

// Make/model spelt exactly as the Japanese auction feed lists them; codes pick the generation.
const PRESETS = [
  { label: "R32 GT-R", make: "Nissan", model: "Skyline", codes: "BNR32", from: 1989, to: 1994 },
  { label: "R33 GT-R", make: "Nissan", model: "Skyline", codes: "BCNR33", from: 1995, to: 1998 },
  { label: "R34 GT-R", make: "Nissan", model: "Skyline", codes: "BNR34", from: 1999, to: 2002 },
  { label: "Supra (A80)", make: "Toyota", model: "Supra", codes: "JZA80", from: 1993, to: 2002 },
  { label: "RX-7 (FD)", make: "Mazda", model: "RX-7", codes: "FD3S", from: 1991, to: 2002 },
  { label: "Silvia S15", make: "Nissan", model: "Silvia", codes: "S15", from: 1999, to: 2002 },
  { label: "NSX", make: "Honda", model: "NSX", codes: "NA1, NA2", from: 1990, to: 2005 },
  { label: "S2000", make: "Honda", model: "S2000", codes: "AP1, AP2", from: 1999, to: 2009 },
  { label: "Evo VIII/IX", make: "Mitsubishi", model: "Lancer Evolution", codes: "CT9A", from: 2003, to: 2007 },
  // Japan lists European cars by their Japanese type code, not the factory one: an E46 M3 is BL32.
  { label: "M3 (E46)", make: "BMW", model: "M3", codes: "BL32", from: 2000, to: 2006 },
];

/** What the form starts with when editing. All strings, as the inputs hold them. */
export type WatchlistInitial = {
  name: string;
  make: string;
  model: string;
  modelCodes: string;
  yearFrom: string;
  yearTo: string;
  maxLandedAmount: string;
  destination: "AU" | "NZ";
  maxMileageKm: string;
  minGrade: string;
  includeRepaired: boolean;
};

type Fields = Pick<WatchlistInitial, "name" | "make" | "model" | "modelCodes" | "yearFrom" | "yearTo">;

const EMPTY: WatchlistInitial = {
  name: "", make: "", model: "", modelCodes: "", yearFrom: "", yearTo: "",
  maxLandedAmount: "", destination: "AU", maxMileageKm: "", minGrade: "3.5", includeRepaired: false,
};

/** One form for creating and editing a watchlist. Presets only show for a new one. */
export function WatchlistForm({
  action: submit,
  initial,
  submitLabel = "Start watching",
}: {
  action: (state: FormState, form: FormData) => Promise<FormState>;
  initial?: WatchlistInitial;
  submitLabel?: string;
}) {
  const start = initial ?? EMPTY;
  const [state, action, pending] = useActionState<FormState, FormData>(submit, undefined);
  const [fields, setFields] = useState<Fields>(start);
  const [picked, setPicked] = useState<string | null>(null);
  const err = (k: string) => state?.errors?.[k]?.[0];
  const set = (k: keyof Fields) => (e: React.ChangeEvent<HTMLInputElement>) => setFields((f) => ({ ...f, [k]: e.target.value }));

  const input = "mt-1 w-full rounded-xl border border-line bg-ink px-3 py-2.5 outline-none focus:border-accent";

  return (
    <form action={action} className="mt-6 space-y-6">
      {!initial && (
        <div className="flex flex-wrap gap-2">
          {PRESETS.map((p) => (
            <button
              type="button"
              key={p.label}
              onClick={() => {
                setPicked(p.label);
                setFields({ name: p.label, make: p.make, model: p.model, modelCodes: p.codes, yearFrom: String(p.from), yearTo: String(p.to) });
              }}
              className={`rounded-full border px-4 py-2 text-sm transition ${picked === p.label ? "border-accent bg-accent/15 text-text" : "border-line bg-raised text-muted hover:text-text"}`}
            >
              {p.label}
            </button>
          ))}
        </div>
      )}

      <div className="card space-y-4 p-5">
        <label className="block text-sm">
          <span className="text-muted">Name</span>
          <input name="name" required maxLength={60} value={fields.name} onChange={set("name")} placeholder="My dream R32" className={input} />
          {err("name") && <span className="text-xs text-accent">{err("name")}</span>}
        </label>
        <div className="grid gap-4 sm:grid-cols-3">
          <label className="block text-sm">
            <span className="text-muted">Make</span>
            <input name="make" required value={fields.make} onChange={set("make")} placeholder="Nissan" className={input} />
            {err("make") && <span className="text-xs text-accent">{err("make")}</span>}
          </label>
          <label className="block text-sm">
            <span className="text-muted">Model</span>
            <input name="model" required value={fields.model} onChange={set("model")} placeholder="Skyline" className={input} />
            {err("model") && <span className="text-xs text-accent">{err("model")}</span>}
          </label>
          <label className="block text-sm">
            <span className="text-muted">Chassis codes</span>
            <input name="modelCodes" value={fields.modelCodes} onChange={set("modelCodes")} placeholder="BNR32" className={input} />
            {err("modelCodes") && <span className="text-xs text-accent">{err("modelCodes")}</span>}
          </label>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <label className="block text-sm">
            <span className="text-muted">Built from</span>
            <input name="yearFrom" inputMode="numeric" value={fields.yearFrom} onChange={set("yearFrom")} placeholder="1989" className={input} />
            {err("yearFrom") && <span className="text-xs text-accent">{err("yearFrom")}</span>}
          </label>
          <label className="block text-sm">
            <span className="text-muted">Built to</span>
            <input name="yearTo" inputMode="numeric" value={fields.yearTo} onChange={set("yearTo")} placeholder="1994" className={input} />
          </label>
        </div>
      </div>

      <div className="card space-y-4 p-5">
        <p className="font-semibold">Your limits</p>
        <div className="grid gap-4 sm:grid-cols-[1fr_auto]">
          <label className="block text-sm">
            <span className="text-muted">Budget on the ground (landed)</span>
            <input name="maxLandedAmount" inputMode="numeric" defaultValue={start.maxLandedAmount} placeholder="45,000" className={`${input} tabular text-lg`} />
            {err("maxLandedAmount") && <span className="text-xs text-accent">{err("maxLandedAmount")}</span>}
          </label>
          <label className="block text-sm">
            <span className="text-muted">Bringing it to</span>
            <select name="destination" defaultValue={start.destination} className={input}>
              <option value="AU">Australia (A$)</option>
              <option value="NZ">New Zealand (NZ$)</option>
            </select>
          </label>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <label className="block text-sm">
            <span className="text-muted">Max mileage (km)</span>
            <input name="maxMileageKm" inputMode="numeric" defaultValue={start.maxMileageKm} placeholder="150,000" className={input} />
            {err("maxMileageKm") && <span className="text-xs text-accent">{err("maxMileageKm")}</span>}
          </label>
          <label className="block text-sm">
            <span className="text-muted">Minimum auction grade</span>
            <select name="minGrade" defaultValue={start.minGrade} className={input}>
              <option value="">Any</option>
              <option value="3">3+</option>
              <option value="3.5">3.5+</option>
              <option value="4">4+</option>
              <option value="4.5">4.5+</option>
            </select>
          </label>
        </div>
        <label className="flex items-center gap-3 text-sm">
          <input type="checkbox" name="includeRepaired" defaultChecked={start.includeRepaired} className="size-4 accent-[var(--color-accent)]" />
          <span>Include repaired cars (grade R / RA). Cheaper, but they&apos;ve had accident repairs.</span>
        </label>
      </div>

      {state?.message && <p className="text-sm text-accent">{state.message}</p>}
      <button disabled={pending} className="w-full rounded-2xl bg-accent py-4 text-lg font-semibold hover:bg-accent-strong disabled:opacity-60">
        {pending ? "Saving…" : submitLabel}
      </button>
    </form>
  );
}
