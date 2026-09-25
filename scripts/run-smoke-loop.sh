#!/usr/bin/env bash
# Runs the built-player smoke N times in a row on one fixed seed/scenario, without retries and without touching the
# repository between runs, and records every run (exit code, stage, error) in <out>/runs.csv.
#
#   ./scripts/run-smoke-loop.sh <out-dir> [runs=10] [seed=11|clock] [extra player args...]
#
# `clock` omits -seed, i.e. the shipped default (the run seed comes from the clock and every run differs).
#
# Each run gets a fresh -savedir and an absolute -logFile; the player writes smoke_result.json into its save dir.
set -u
OUT="${1:?out dir}"; RUNS="${2:-10}"; SEED="${3:-11}"; shift 3 2>/dev/null || shift $#
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="${RUINRAIL_APP:-$ROOT/Builds/MacOS/RUINRAIL.app/Contents/MacOS/RUINRAIL}"
mkdir -p "$OUT"; OUT="$(cd "$OUT" && pwd)"
[ -x "$APP" ] || { echo "NOT RUN — player executable unavailable: $APP"; exit 2; }
echo "run,exit,success,stage,error,seconds" > "$OUT/runs.csv"
passes=0
for i in $(seq 1 "$RUNS"); do
  dir="$OUT/run_$i"; rm -rf "$dir"; mkdir -p "$dir/save"
  start=$(date +%s)
  seedargs=(-seed "$SEED"); [ "$SEED" = clock ] && seedargs=()
  "$APP" -batchmode -nographics -smoke ${seedargs[@]+"${seedargs[@]}"} -savedir "$dir/save" -proofdir "$dir/proof" -logFile "$dir/player.log" "$@" >/dev/null 2>&1
  code=$?
  secs=$(( $(date +%s) - start ))
  cp "$dir/save/smoke_result.json" "$dir/" 2>/dev/null
  read -r ok stage err < <(python3 - "$dir/smoke_result.json" <<'EOF'
import json, sys
try:
    d = json.load(open(sys.argv[1]))
    print(str(d.get("Success")).lower(), d.get("Stage", "?").replace(" ", "_"), (d.get("Error") or "-").replace(",", ";").replace("\n", " ")[:300])
except Exception as e:
    print("false", "no_result", "no smoke_result.json")
EOF
)
  [ "$code" = 0 ] && [ "$ok" = "true" ] && passes=$((passes + 1))
  echo "$i,$code,$ok,$stage,\"$err\",$secs" >> "$OUT/runs.csv"
  echo "[smoke-loop] run $i/$RUNS exit=$code success=$ok stage=$stage ${secs}s $err"
done
echo "[smoke-loop] $passes/$RUNS passed (seed $SEED)"
[ "$passes" = "$RUNS" ]
