#!/usr/bin/env bash
# The co-op release smoke: the built-player co-op expedition proof (real host/client processes of the shipped
# non-development player over UnityTransport on loopback) run N consecutive times on one fixed seed, no retries.
#
#   ./scripts/run-coop-release-smoke.sh <out-dir> [size=2] [runs=5] [seed=46] [base-port=7960]
#
# Every run is recorded in <out>/coop_runs.csv (per peer: success, steps passed/total). Exit 0 only if all passed.
set -u
OUT="${1:?out dir}"; SIZE="${2:-2}"; RUNS="${3:-5}"; SEED="${4:-46}"; PORT="${5:-7960}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$OUT"; OUT="$(cd "$OUT" && pwd)"
CSV="$OUT/coop_runs.csv"
echo "size,run,seed,port,peer,success,steps_passed,steps_total,first_failed_step,seconds" > "$CSV"
failed=0
for i in $(seq 1 "$RUNS"); do
  # Stray peers from an aborted run would hold the port or join the wrong session.
  pkill -f "RUINRAIL.app/Contents/MacOS/RUINRAIL -batchmode -nographics -coop-expedition" 2>/dev/null
  dir="$OUT/run_$i"; rm -rf "$dir"; mkdir -p "$dir"
  start=$(date +%s)
  "$ROOT/scripts/run-coop-expedition-proof.sh" "$dir" "$SIZE" "$SEED" $((PORT + i)) >"$dir/runner.out" 2>&1
  secs=$(( $(date +%s) - start ))
  peers="host"; for c in $(seq 1 $((SIZE - 1))); do peers="$peers client$c"; done
  for peer in $peers; do
    line=$(python3 - "$dir/$peer.json" <<'EOF'
import json, sys
try:
    d = json.load(open(sys.argv[1]))
    steps = d.get("Steps", [])
    ok = sum(1 for s in steps if s.get("Pass"))
    first = next((s.get("Name", "?") for s in steps if not s.get("Pass")), "-")
    print("%s,%d,%d,\"%s\"" % (str(d.get("Success")).lower(), ok, len(steps), first.replace('"', "'")[:160]))
except Exception:
    print('false,0,0,"no result json"')
EOF
)
    echo "$SIZE,$i,$SEED,$((PORT + i)),$peer,$line,$secs" >> "$CSV"
    case "$line" in true,*) ;; *) failed=1 ;; esac
    echo "[coop-smoke] size $SIZE run $i $peer $line ${secs}s"
  done
done
exit $failed
