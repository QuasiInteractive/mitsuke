"""Writes src/Mitsuke.Functions/local.settings.json (gitignored) from the example plus values in .env.

    python scripts/local-settings.py [--schedule "0 */1 * * * *"]

Keeps secrets in exactly one local place (.env). The schedule override is handy for watching the
pipeline run locally without waiting ten minutes.
"""
import argparse
import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
functions = root / "src" / "Mitsuke.Functions"

parser = argparse.ArgumentParser()
parser.add_argument("--schedule", help="NCRONTAB override for CollectSchedule")
args = parser.parse_args()

env = {}
env_file = root / ".env"
if env_file.exists():
    for line in env_file.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, value = line.split("=", 1)
            env[key.strip()] = value.strip().strip('"')

settings = json.loads((functions / "local.settings.example.json").read_text(encoding="utf-8"))
for key in settings["Values"]:
    if env.get(key):
        settings["Values"][key] = env[key]
if args.schedule:
    settings["Values"]["CollectSchedule"] = args.schedule

missing = [k for k in ("MITSUKE_DB", "THECARAPI_KEY") if not settings["Values"].get(k) or settings["Values"][k].endswith("Password=")]
(functions / "local.settings.json").write_text(json.dumps(settings, indent=2) + "\n", encoding="utf-8")
print("Wrote src/Mitsuke.Functions/local.settings.json" + (f" (still missing: {', '.join(missing)})" if missing else ""))
