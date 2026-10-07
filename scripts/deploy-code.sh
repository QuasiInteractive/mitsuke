#!/usr/bin/env bash
# Builds and ships the pipeline (Azure Functions) and the API (App Service), then runs the database migrations.
# GitHub Actions does the same on every push to main (.github/workflows/deploy.yml); this is the manual route.
#
#   scripts/deploy-code.sh
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
rg=mitsuke-rg
out="$(mktemp -d)"; trap 'rm -rf "$out"' EXIT

functions="$(az functionapp list -g "$rg" --query "[?starts_with(name, 'mitsuke-pipeline')].name | [0]" -o tsv)"
api="$(az webapp list -g "$rg" --query "[?starts_with(name, 'mitsuke-api')].name | [0]" -o tsv)"
[ -n "$functions" ] && [ -n "$api" ] || { echo "Run scripts/deploy-infra.sh first."; exit 1; }

echo "== API -> $api"
dotnet publish "$root/src/Mitsuke.Api" -c Release -o "$out/api" --nologo -v quiet
(cd "$out/api" && powershell -NoProfile -Command "Compress-Archive -Path * -DestinationPath '$(cygpath -w "$out/api.zip")' -Force")
az webapp deploy -g "$rg" -n "$api" --src-path "$out/api.zip" --type zip -o none

echo "== Pipeline -> $functions"
(cd "$root/src/Mitsuke.Functions" && func azure functionapp publish "$functions" --dotnet-isolated)

echo "== Health"
curl -fsS "https://$(az webapp show -g "$rg" -n "$api" --query defaultHostName -o tsv)/api/health" && echo
