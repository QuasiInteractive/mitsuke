#!/usr/bin/env bash
# Copies Kensa-ya's landed-cost engine, country rules and golden answers into Mitsuke.
#
#   scripts/sync-kensaya-engine.sh [path-to-Kensa-ya]     (default: ../Kensa-ya)
#
# Kensa-ya is the source of truth (its TypeScript website, checked by its C# worker).
# Mitsuke vendors the C# port unchanged so both products quote the same numbers, and
# tests/Mitsuke.Tests/LandedCostParityTests.cs proves it against Kensa-ya's golden answers.
# Only the landed-cost engine and the public rate data are copied; sheet reading stays private.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
kensaya="$(cd "${1:-$root/../Kensa-ya}" && pwd)"
rules_src="$kensaya/worker/src/Kensaya.Worker.Core/Rules"
dest="$root/src/Mitsuke.Pricing/Kensaya"
data="$root/src/Mitsuke.Pricing/data"
golden="$root/tests/Mitsuke.Tests/contract"

mkdir -p "$dest" "$data/countries" "$golden"

for f in CountryRules LandedCost RulesCatalog IRulesSource LiveFx; do
  # The only edit: the data folder lives with Mitsuke.Pricing, not Kensa-ya's SharedData.
  sed 's/SharedData\.DefaultDirectory/Mitsuke.Pricing.PricingData.DefaultDirectory/' "$rules_src/$f.cs" > "$dest/$f.cs"
done

# Eligibility: the only outside type it uses is the sheet's YearBasis enum, so that one enum is restated here
# instead of copying any of Kensa-ya's sheet-reading code.
sed -e '/^using Kensaya\.Worker\.Core\.Sheets;/d' -e 's/Sheets\.YearBasis\./YearBasis./g' "$rules_src/Eligibility.cs" > "$dest/Eligibility.cs"
sheet_types="$kensaya/worker/src/Kensaya.Worker.Core/Sheets/SheetExtraction.cs"
yb="$(grep -n 'public enum YearBasis' "$sheet_types" | cut -d: -f1)"
{
  printf '%s\n' "// Restated from Kensa-ya's Sheets/SheetExtraction.cs by scripts/sync-kensaya-engine.sh: what a vehicle's" \
                "// year means. Eligibility.cs needs it; the rest of Kensa-ya's sheet code stays in Kensa-ya." \
                "using System.Text.Json.Serialization;" "" "namespace Kensaya.Worker.Core.Rules;" ""
  sed -n "$((yb - 1)),$((yb + 6))p" "$sheet_types"
} > "$dest/YearBasis.cs"

cp "$kensaya/src/data/countries.json" "$data/countries.json"
cp "$kensaya"/src/data/countries/*.json "$data/countries/"

# The landed-cost and eligibility parts of the contract (the rest needs sheet types we don't copy).
node -e '
  const g = require(process.argv[1]);
  const out = {
    asOf: g.asOf,
    countries: Object.fromEntries(Object.entries(g.countries).map(([k, v]) => [k, { landedFull: v.landedFull }])),
    landed: g.landed,
  };
  require("fs").writeFileSync(process.argv[2], JSON.stringify(out, null, 1) + "\n");
  require("fs").writeFileSync(process.argv[3], JSON.stringify({ asOf: g.asOf, eligibility: g.eligibility }, null, 1) + "\n");
' "$kensaya/worker/contract/engine-golden.json" "$golden/landed-golden.json" "$golden/eligibility-golden.json"

commit="$(git -C "$kensaya" rev-parse --short HEAD)"
printf '%s\n' "$commit" > "$root/src/Mitsuke.Pricing/KENSAYA_VERSION"
echo "Synced from Kensa-ya $commit."
