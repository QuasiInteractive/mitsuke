#!/usr/bin/env bash
# Copies Mitsuke's secrets from your local .env into the Azure Key Vault. Run it yourself:
#
#   scripts/push-secrets.sh
#
# Values go to Key Vault through temp files that are deleted straight away (never on the command line, where other
# processes could read them). Needs: az login, and the Key Vault Secrets Officer role (infra/main.bicep grants it).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
envfile="$root/.env"
get() { grep -E "^$1=" "$envfile" | head -1 | cut -d= -f2- | tr -d '\r'; }

vault="$(az keyvault list -g mitsuke-rg --query '[0].name' -o tsv)"
[ -n "$vault" ] || { echo "No Key Vault in mitsuke-rg yet: run scripts/deploy-infra.sh first."; exit 1; }

# Production database: Supabase's session pooler (IPv4) with Mitsuke's private schema.
pooler="$(get PROD_DB_POOLER_HOST)"; ref="$(get PROD_SUPABASE_URL | sed -E 's#https://([a-z0-9]+)\..*#\1#')"
db="Host=$pooler;Port=5432;Database=postgres;Username=postgres.$ref;Password=$(get PROD_DB_PASSWORD);SSL Mode=Require;Search Path=mitsuke"

tmp="$(mktemp)"; trap 'rm -f "$tmp"' EXIT
# az is a Windows program under Git Bash: give it a Windows path (C:\...), not /tmp/...
aztmp="$tmp"; command -v cygpath >/dev/null && aztmp="$(cygpath -w "$tmp")"
put() {
  [ -n "$2" ] || { echo "  skipped $1 (empty in .env)"; return; }
  printf '%s' "$2" > "$tmp"
  az keyvault secret set --vault-name "$vault" --name "$1" --file "$aztmp" --encoding utf-8 --output none
  : > "$tmp"
  echo "  set $1"
}

echo "Key Vault: $vault"
put mitsuke-db "$db"
put thecarapi-key "$(get THECARAPI_KEY)"
put smtp-password "$(get SMTP_PASSWORD | tr -d ' ')"
put kensaya-partner-key "$(get KENSAYA_PARTNER_KEY)"
echo "Done. Restart the apps (or redeploy) to pick up new values."
