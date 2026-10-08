"use client";

import { useActionState, useEffect, useMemo, useState } from "react";
import type { FormState } from "@/app/actions";
import type { CatalogGeneration, CatalogMake, CatalogModel } from "@/lib/api";

// ---- Curated starting points. Make/model as the Japanese feed names them; the code picks the generation. ----
const CLASSICS = [
  { label: "R32 GT-R", sub: "Nissan · BNR32", make: "Nissan", model: "Skyline", code: "BNR32" },
  { label: "R33 GT-R", sub: "Nissan · BCNR33", make: "Nissan", model: "Skyline", code: "BCNR33" },
  { label: "R34 GT-R", sub: "Nissan · BNR34", make: "Nissan", model: "Skyline", code: "BNR34" },
  { label: "Supra", sub: "Toyota · JZA80", make: "Toyota", model: "Supra", code: "JZA80" },
  { label: "RX-7", sub: "Mazda · FD3S", make: "Mazda", model: "Rx-7", code: "FD3S" },
  { label: "Silvia S15", sub: "Nissan · S15", make: "Nissan", model: "Silvia", code: "S15" },
  { label: "NSX", sub: "Honda · NA1", make: "Honda", model: "Nsx", code: "NA1" },
  { label: "S2000", sub: "Honda · AP1", make: "Honda", model: "S2000", code: "AP1" },
  { label: "Evo VII–IX", sub: "Mitsubishi · CT9A", make: "Mitsubishi", model: "Lancer Evolution", code: "CT9A" },
  { label: "M3 (E46)", sub: "BMW · BL32", make: "BMW", model: "M3", code: "BL32" },
] as const;

const POPULAR_MAKES = ["Toyota", "Nissan", "Honda", "Mazda", "Mitsubishi", "Subaru", "Suzuki", "Lexus", "Daihatsu", "BMW", "Mercedes-Benz", "Porsche"];

type Currency = "AUD" | "NZD" | "USD" | "JPY";
type Destination = "AU" | "NZ" | "US";
const CURRENCIES: { id: Currency; label: string; symbol: string }[] = [
  { id: "AUD", label: "A$", symbol: "A$" },
  { id: "NZD", label: "NZ$", symbol: "NZ$" },
  { id: "USD", label: "US$", symbol: "US$" },
  { id: "JPY", label: "¥", symbol: "¥" },
];
const LANDED_IN: Record<Destination, Currency> = { AU: "AUD", NZ: "NZD", US: "USD" };
const DESTINATION_OF: Partial<Record<Currency, Destination>> = { AUD: "AU", NZD: "NZ", USD: "US" };

const BUDGETS: Record<"landed" | "yen", number[]> = {
  landed: [10_000, 15_000, 20_000, 25_000, 30_000, 35_000, 40_000, 45_000, 50_000, 60_000, 75_000, 100_000, 150_000, 200_000, 300_000],
  yen: [500_000, 1_000_000, 1_500_000, 2_000_000, 3_000_000, 4_000_000, 5_000_000, 7_500_000, 10_000_000, 15_000_000, 20_000_000],
};
const MILEAGES = [10_000, 20_000, 30_000, 50_000, 75_000, 100_000, 120_000, 150_000, 200_000];
const THIS_YEAR = new Date().getFullYear();

/** What the form starts with when editing. */
export type WatchlistInitial = {
  name: string;
  make: string;
  model: string;
  modelCodes: string;
  yearFrom: string;
  yearTo: string;
  budget: string;
  currency: Currency;
  destination: Destination;
  maxMileageKm: string;
  minGrade: string;
  includeRepaired: boolean;
};

const EMPTY: WatchlistInitial = {
  name: "", make: "", model: "", modelCodes: "", yearFrom: "", yearTo: "",
  budget: "", currency: "AUD", destination: "AU", maxMileageKm: "", minGrade: "3.5", includeRepaired: false,
};

const n = (v: number) => v.toLocaleString("en-AU");
const getJson = async <T,>(path: string): Promise<T> => {
  const res = await fetch(path);
  return res.ok ? ((await res.json()) as T) : ([] as T);
};

/** The new/edit watchlist form: catalogue-driven choices instead of free text, one place for the buyer's limits. */
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
  const err = (k: string) => state?.errors?.[k]?.[0];

  const [makes, setMakes] = useState<CatalogMake[]>([]);
  const [models, setModels] = useState<CatalogModel[]>([]);
  const [generations, setGenerations] = useState<CatalogGeneration[]>([]);

  const [make, setMake] = useState(start.make);
  const [model, setModel] = useState(start.model);
  const startCodes = start.modelCodes.split(",").map((c) => c.trim()).filter(Boolean);
  const [code, setCode] = useState(startCodes.length > 1 ? "custom" : startCodes[0] ?? "");
  const [customCodes, setCustomCodes] = useState(startCodes.length > 1 ? start.modelCodes : "");
  const [yearFrom, setYearFrom] = useState(start.yearFrom);
  const [yearTo, setYearTo] = useState(start.yearTo);
  const [name, setName] = useState(start.name);
  const [nameTouched, setNameTouched] = useState(Boolean(initial));
  const [picked, setPicked] = useState<string | null>(null);

  const [currency, setCurrency] = useState<Currency>(start.currency);
  const [destination, setDestination] = useState<Destination>(start.destination);
  const [budget, setBudget] = useState(start.budget);
  const [customBudget, setCustomBudget] = useState(false);
  const [mileage, setMileage] = useState(start.maxMileageKm);
  const [repaired, setRepaired] = useState(start.includeRepaired);

  // Load the makes, and when editing, the chosen make's models and generations, once.
  useEffect(() => {
    let live = true;
    getJson<CatalogMake[]>("/api/catalog/makes").then((m) => live && setMakes(m));
    if (start.make) getJson<CatalogModel[]>(`/api/catalog/models?make=${encodeURIComponent(start.make)}`).then((m) => live && setModels(m));
    if (start.make && start.model)
      getJson<CatalogGeneration[]>(`/api/catalog/generations?make=${encodeURIComponent(start.make)}&model=${encodeURIComponent(start.model)}`).then(
        (g) => live && setGenerations(g),
      );
    return () => {
      live = false;
    };
  }, [start.make, start.model]);

  async function chooseMake(next: string) {
    setMake(next);
    setModel("");
    setModels([]);
    setGenerations([]);
    setCode("");
    if (next) setModels(await getJson<CatalogModel[]>(`/api/catalog/models?make=${encodeURIComponent(next)}`));
  }

  async function chooseModel(next: string, thenCode = "") {
    setModel(next);
    setGenerations([]);
    setCode(thenCode);
    if (!nameTouched) setName(models.find((m) => m.name === next)?.label ?? next);
    if (!next) return;
    const gens = await getJson<CatalogGeneration[]>(`/api/catalog/generations?make=${encodeURIComponent(make || "")}&model=${encodeURIComponent(next)}`);
    setGenerations(gens);
    if (thenCode) applyGeneration(thenCode, gens);
  }

  function applyGeneration(next: string, gens = generations) {
    setCode(next);
    const g = gens.find((x) => x.code === next);
    if (g) {
      setYearFrom(String(g.yearFrom));
      setYearTo(String(g.yearTo));
      if (!nameTouched) setName(g.label);
    }
  }

  async function pickClassic(c: (typeof CLASSICS)[number]) {
    setPicked(c.label);
    if (!nameTouched) setName(c.label);
    setMake(c.make);
    const [mods, gens] = await Promise.all([
      getJson<CatalogModel[]>(`/api/catalog/models?make=${encodeURIComponent(c.make)}`),
      getJson<CatalogGeneration[]>(`/api/catalog/generations?make=${encodeURIComponent(c.make)}&model=${encodeURIComponent(c.model)}`),
    ]);
    setModels(mods);
    setModel(c.model);
    setGenerations(gens);
    applyGeneration(c.code, gens);
    if (!nameTouched) setName(c.label);
  }

  function chooseCurrency(next: Currency) {
    setCurrency(next);
    setBudget("");
    setCustomBudget(false);
    const dest = DESTINATION_OF[next];
    if (dest) setDestination(dest);
  }

  function chooseDestination(next: Destination) {
    setDestination(next);
    if (currency !== "JPY") {
      setCurrency(LANDED_IN[next]);
      setBudget("");
    }
  }

  // The years a model's generation was built, or every plausible year.
  const generation = generations.find((g) => g.code === code);
  const years = useMemo(() => {
    const from = generation?.yearFrom || 1970;
    const to = Math.min(generation?.yearTo || THIS_YEAR, THIS_YEAR);
    const range = Array.from({ length: to - from + 1 }, (_, i) => to - i);
    // Keep a saved year selectable even if it falls outside the generation.
    return [...new Set([...range, ...[yearFrom, yearTo].filter(Boolean).map(Number)])].sort((a, b) => b - a);
  }, [generation, yearFrom, yearTo]);

  const popular = makes.filter((m) => POPULAR_MAKES.includes(m.name));
  const others = makes.filter((m) => !POPULAR_MAKES.includes(m.name));
  // Dropdowns when the catalogue has the answer; a text box when it's empty (not synced yet) or lacks the saved value.
  const makeKnown = makes.length > 0 && (make === "" || makes.some((m) => m.name.toLowerCase() === make.toLowerCase()));
  const modelKnown = models.length > 0 && (model === "" || models.some((m) => m.name.toLowerCase() === model.toLowerCase()));
  const symbol = CURRENCIES.find((c) => c.id === currency)!.symbol;
  const presets = currency === "JPY" ? BUDGETS.yen : BUDGETS.landed;
  const budgetIsPreset = budget === "" || presets.includes(Number(budget));
  const codes = code === "custom" ? customCodes : code;

  return (
    <form action={action} className="mt-10 space-y-8">
      {/* What the server receives. */}
      <input type="hidden" name="make" value={make} />
      <input type="hidden" name="model" value={model} />
      <input type="hidden" name="modelCodes" value={codes} />
      <input type="hidden" name="yearFrom" value={yearFrom} />
      <input type="hidden" name="yearTo" value={yearTo} />
      <input type="hidden" name="maxLandedAmount" value={budget} />
      <input type="hidden" name="budgetCurrency" value={currency} />
      <input type="hidden" name="destination" value={destination} />
      <input type="hidden" name="maxMileageKm" value={mileage} />
      {repaired && <input type="hidden" name="includeRepaired" value="on" />}

      {!initial && (
        <section>
          <p className="label">Start from a classic</p>
          <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-5">
            {CLASSICS.map((c) => (
              <button key={c.label} type="button" onClick={() => pickClassic(c)} aria-pressed={picked === c.label} className="tile">
                <span className="text-sm font-semibold">{c.label}</span>
                <span className="text-xs text-faint">{c.sub}</span>
              </button>
            ))}
          </div>
        </section>
      )}

      <section className="card p-6 sm:p-7">
        <div className="flex items-baseline justify-between">
          <h2 className="text-lg font-semibold">The car</h2>
          {make && model && <p className="text-xs text-faint">{models.find((m) => m.name === model)?.lots ?? 0} at auction now</p>}
        </div>

        <div className="mt-5 grid gap-5 sm:grid-cols-2">
          <label className="block">
            <span className="label">Make</span>
            {makeKnown ? (
              <select className="field" value={make} onChange={(e) => chooseMake(e.target.value)} required>
                <option value="">Choose a make</option>
                {popular.length > 0 && (
                  <optgroup label="Popular">
                    {popular.map((m) => <option key={m.name} value={m.name}>{m.name}</option>)}
                  </optgroup>
                )}
                <optgroup label="All makes">
                  {others.map((m) => <option key={m.name} value={m.name}>{m.name}</option>)}
                </optgroup>
              </select>
            ) : (
              <input className="field" value={make} onChange={(e) => setMake(e.target.value)} required />
            )}
            {err("make") && <span className="mt-1 block text-xs text-accent">{err("make")}</span>}
          </label>

          <label className="block">
            <span className="label">Model</span>
            {modelKnown ? (
              <select className="field" value={model} onChange={(e) => chooseModel(e.target.value)} required disabled={!make}>
                <option value="">Choose a model</option>
                {models.map((m) => (
                  <option key={m.name} value={m.name}>
                    {m.label}{m.lots > 0 ? ` · ${n(m.lots)}` : ""}
                  </option>
                ))}
              </select>
            ) : (
              <input className="field" value={model} onChange={(e) => setModel(e.target.value)} placeholder={make ? "Type the model" : "Choose a make first"} disabled={!make} required />
            )}
            {err("model") && <span className="mt-1 block text-xs text-accent">{err("model")}</span>}
          </label>

          <label className="block sm:col-span-2">
            <span className="label">Generation</span>
            <select className="field" value={code} onChange={(e) => applyGeneration(e.target.value)} disabled={!model}>
              <option value="">Any generation</option>
              {generations.map((g) => (
                <option key={g.code} value={g.code}>
                  {g.label === g.code ? g.code : `${g.label} · ${g.code}`} · {g.yearFrom}–{g.yearTo}
                </option>
              ))}
              {code && code !== "custom" && !generations.some((g) => g.code === code) && <option value={code}>{code}</option>}
              <option value="custom">Enter chassis codes…</option>
            </select>
            {code === "custom" && (
              <input className="field" value={customCodes} onChange={(e) => setCustomCodes(e.target.value)} placeholder="e.g. CT9A, CT9W" autoFocus />
            )}
            {err("modelCodes") && <span className="mt-1 block text-xs text-accent">{err("modelCodes")}</span>}
          </label>

          <label className="block">
            <span className="label">Built from</span>
            <select className="field" value={yearFrom} onChange={(e) => setYearFrom(e.target.value)}>
              <option value="">Any year</option>
              {[...years].reverse().map((y) => <option key={y} value={y}>{y}</option>)}
            </select>
            {err("yearFrom") && <span className="mt-1 block text-xs text-accent">{err("yearFrom")}</span>}
          </label>
          <label className="block">
            <span className="label">Built to</span>
            <select className="field" value={yearTo} onChange={(e) => setYearTo(e.target.value)}>
              <option value="">Any year</option>
              {years.map((y) => <option key={y} value={y}>{y}</option>)}
            </select>
          </label>
        </div>
      </section>

      <section className="card p-6 sm:p-7">
        <h2 className="text-lg font-semibold">Your limits</h2>

        <div className="mt-5">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <span className="label">{currency === "JPY" ? "Highest opening bid" : "Budget on the ground"}</span>
            <div className="segmented" role="group" aria-label="Budget currency">
              {CURRENCIES.map((c) => (
                <button key={c.id} type="button" aria-pressed={currency === c.id} onClick={() => chooseCurrency(c.id)}>
                  {c.label}
                </button>
              ))}
            </div>
          </div>
          {customBudget || !budgetIsPreset ? (
            <div className="relative">
              <span className="pointer-events-none absolute top-1/2 left-4 mt-1 -translate-y-1/2 text-2xl font-semibold text-muted">{symbol}</span>
              <input
                className="field tabular py-4 pl-16 text-3xl font-bold tracking-tight"
                inputMode="numeric"
                value={budget ? n(Number(budget)) : ""}
                onChange={(e) => setBudget(e.target.value.replace(/[^\d]/g, ""))}
                placeholder={currency === "JPY" ? "3,000,000" : "45,000"}
                autoFocus
              />
            </div>
          ) : (
            <select
              className="field tabular py-4 text-3xl font-bold tracking-tight"
              value={budget}
              onChange={(e) => (e.target.value === "custom" ? (setCustomBudget(true), setBudget("")) : setBudget(e.target.value))}
            >
              <option value="">No limit</option>
              {presets.map((v) => <option key={v} value={v}>{symbol}{n(v)}</option>)}
              <option value="custom">Custom amount…</option>
            </select>
          )}
          <p className="mt-2 text-xs text-faint">
            {currency === "JPY"
              ? "The most you'd see as an opening bid at auction, before any fees, shipping or tax."
              : "Everything to get it on the road: the car, auction and export fees, shipping, duty, tax and compliance."}
          </p>
          {err("maxLandedAmount") && <span className="mt-1 block text-xs text-accent">{err("maxLandedAmount")}</span>}
          {err("budgetCurrency") && <span className="mt-1 block text-xs text-accent">{err("budgetCurrency")}</span>}
        </div>

        <div className="mt-6 grid gap-5 sm:grid-cols-3">
          <label className="block">
            <span className="label">Bringing it to</span>
            <select className="field" value={destination} onChange={(e) => chooseDestination(e.target.value as Destination)}>
              <option value="AU">Australia</option>
              <option value="NZ">New Zealand</option>
              <option value="US">United States</option>
            </select>
            {err("destination") && <span className="mt-1 block text-xs text-accent">{err("destination")}</span>}
          </label>
          <label className="block">
            <span className="label">Odometer</span>
            <select className="field" value={mileage} onChange={(e) => setMileage(e.target.value)}>
              <option value="">Any mileage</option>
              {[...new Set([...MILEAGES, ...(mileage ? [Number(mileage)] : [])])].sort((a, b) => a - b).map((km) => (
                <option key={km} value={km}>Under {n(km)} km</option>
              ))}
            </select>
            {err("maxMileageKm") && <span className="mt-1 block text-xs text-accent">{err("maxMileageKm")}</span>}
          </label>
          <label className="block">
            <span className="label">Auction grade</span>
            <select name="minGrade" defaultValue={start.minGrade} className="field">
              <option value="">Any grade</option>
              <option value="3">3 and up</option>
              <option value="3.5">3.5 and up</option>
              <option value="4">4 and up</option>
              <option value="4.5">4.5 and up</option>
            </select>
          </label>
        </div>

        <button
          type="button"
          role="switch"
          aria-checked={repaired}
          onClick={() => setRepaired((r) => !r)}
          className="mt-6 flex w-full items-center justify-between gap-4 rounded-2xl border border-hairline bg-black/20 px-4 py-3 text-left"
        >
          <span>
            <span className="block text-sm font-medium">Include repaired cars</span>
            <span className="block text-xs text-faint">Grade R / RA: cheaper, but they&apos;ve had accident repairs.</span>
          </span>
          <span className={`relative h-6 w-11 shrink-0 rounded-full transition ${repaired ? "bg-accent" : "bg-white/10"}`}>
            <span className={`absolute top-0.5 size-5 rounded-full bg-white shadow transition-all ${repaired ? "left-[1.375rem]" : "left-0.5"}`} />
          </span>
        </button>
      </section>

      <section className="card p-6 sm:p-7">
        <label className="block">
          <span className="label">Name this watchlist</span>
          <input
            name="name"
            required
            maxLength={60}
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              setNameTouched(true);
            }}
            placeholder="My dream R32"
            className="field"
          />
          {err("name") && <span className="mt-1 block text-xs text-accent">{err("name")}</span>}
        </label>
      </section>

      {state?.message && <p className="text-sm text-accent">{state.message}</p>}
      <button disabled={pending} className="btn-primary w-full py-5 text-lg">
        {pending ? "Saving…" : <>{submitLabel} <span aria-hidden>→</span></>}
      </button>
      <p className="-mt-4 text-center text-xs text-faint">Mitsuke checks the auctions every ten minutes and alerts you the moment one fits.</p>
    </form>
  );
}
