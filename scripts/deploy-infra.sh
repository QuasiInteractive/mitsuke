#!/usr/bin/env bash
# Creates or updates Mitsuke's Azure resources from infra/main.bicep.
#
#   scripts/deploy-infra.sh            # shows what would change (what-if), then asks before applying
#   scripts/deploy-infra.sh --yes      # applies without asking
#
# Reads PROD_SUPABASE_URL, PROD_WEB_URL, BID_REQUESTS_TO and SMTP_USER from .env, so nothing personal is committed.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
get() { grep -E "^$1=" "$root/.env" | head -1 | cut -d= -f2- | tr -d '\r'; }

export PROD_SUPABASE_URL="$(get PROD_SUPABASE_URL)"
export PROD_WEB_URL="$(get PROD_WEB_URL)"
export BID_REQUESTS_TO="$(get BID_REQUESTS_TO)"
export SMTP_USER="$(get SMTP_USER)"
export ADMIN_PRINCIPAL_ID="$(az ad signed-in-user show --query id -o tsv)"
for v in PROD_SUPABASE_URL BID_REQUESTS_TO SMTP_USER; do
  [ -n "${!v}" ] || { echo "Set $v in .env first."; exit 1; }
done

rg=mitsuke-rg
az group create -n "$rg" -l australiaeast --tags app=mitsuke -o none

az deployment group what-if -g "$rg" -f "$root/infra/main.bicep" -p "$root/infra/main.bicepparam" --no-pretty-print \
  --query "changes[].{change:changeType, resource:resourceId}" -o table | sed -E 's#/subscriptions/[^/]+/resourceGroups/##'

if [ "${1:-}" != "--yes" ]; then
  read -r -p "Apply these changes? [y/N] " ok
  [ "$ok" = "y" ] || { echo "Nothing applied."; exit 0; }
fi

az deployment group create -g "$rg" -n "mitsuke-$(date +%Y%m%d%H%M%S)" -f "$root/infra/main.bicep" -p "$root/infra/main.bicepparam" \
  --query "properties.outputs" -o json
