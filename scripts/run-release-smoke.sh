#!/usr/bin/env bash
# The official release smoke: independent deterministic built-player scenarios, each run N consecutive times on a
# fixed seed with a fresh save directory, no retries and no repository change between runs. Every run is recorded.
#
#   ./scripts/run-release-smoke.sh <out-dir> [runs=10] [scenarios="fresh returning death cache"]
#
# Scenarios (see release_smoke_manifest.csv):
#   fresh      full solo smoke on a brand-new profile, seed 11 (D1 → boss → descend → D2 → return → save/reload,
#              death run, leave-to-menu run)
#   returning  relaunch on the profile that fresh run i left behind (copied, so the fresh evidence stays intact)
#   death      independent solo death on a fresh profile, seed 11
#   cache      the full solo smoke on seed 31 (a D1 with a Weapon Cache and the ordered stages that share it)
#   longrun    12 consecutive descends in one solo run, sampled per depth (objects, heap, rebuild time), UI churn,
#              save/load timing — not in the default set; run it explicitly
#
# Exit code 0 only when every run of every requested scenario passed.
set -u
OUT="${1:?out dir}"; RUNS="${2:-10}"; SCENARIOS="${3:-fresh returning death cache}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="${RUINRAIL_APP:-$ROOT/Builds/MacOS/RUINRAIL.app/Contents/MacOS/RUINRAIL}"
mkdir -p "$OUT"; OUT="$(cd "$OUT" && pwd)"
[ -x "$APP" ] || { echo "NOT RUN — player executable unavailable: $APP"; exit 2; }
SUMMARY="$OUT/release_smoke_runs.csv"
echo "scenario,run,seed,exit,success,stage,error,seconds" > "$SUMMARY"
failed=0

record() { # scenario run seed dir code secs
  local scenario=$1 run=$2 seed=$3 dir=$4 code=$5 secs=$6
  cp "$dir/save/smoke_result.json" "$dir/" 2>/dev/null
  local line
  line=$(python3 - "$dir/smoke_result.json" <<'EOF'
import json, sys
try:
    d = json.load(open(sys.argv[1]))
    err = (d.get("Error") or "-").replace('"', "'").replace("\n", " ")[:300]
    print("%s,%s,\"%s\"" % (str(d.get("Success")).lower(), d.get("Stage", "?"), err))
except Exception:
    print('false,no_result,"no smoke_result.json"')
EOF
)
  echo "$scenario,$run,$seed,$code,$line,$secs" >> "$SUMMARY"
  case "$line" in true,*) [ "$code" = 0 ] || failed=1 ;; *) failed=1 ;; esac
  echo "[release-smoke] $scenario $run/$RUNS exit=$code $line ${secs}s"
}

run_player() { # dir seed extra...
  local dir=$1 seed=$2; shift 2
  local seedargs=(); [ -n "$seed" ] && seedargs=(-seed "$seed")
  "$APP" -batchmode -nographics -smoke ${seedargs[@]+"${seedargs[@]}"} -savedir "$dir/save" -proofdir "$dir/proof" -logFile "$dir/player.log" "$@" >/dev/null 2>&1
}

for i in $(seq 1 "$RUNS"); do
  for scenario in $SCENARIOS; do
    dir="$OUT/$scenario/run_$i"; rm -rf "$dir"; mkdir -p "$dir/save"
    start=$(date +%s)
    case "$scenario" in
      fresh) seed=11; run_player "$dir" 11; code=$? ;;
      cache) seed=31; run_player "$dir" 31; code=$? ;;
      death) seed=11; run_player "$dir" 11 -smoke-scenario death; code=$? ;;
      longrun) seed=11; run_player "$dir" 11 -smoke-scenario longrun; code=$? ;;
      returning)
        seed=profile
        src="$OUT/fresh/run_$i/save"
        if [ -d "$src" ]; then
          cp -R "$src/." "$dir/save/"; rm -f "$dir/save/smoke_result.json"
          run_player "$dir" "" -smoke-scenario returning; code=$?
        else
          code=3
        fi ;;
      *) echo "unknown scenario $scenario"; exit 2 ;;
    esac
    record "$scenario" "$i" "$seed" "$dir" "$code" $(( $(date +%s) - start ))
  done
done

# The last passing full smoke is also the machine's smoke_result.json (read by FinalMvpAudit).
last=$(ls -d "$OUT"/fresh/run_* 2>/dev/null | sort -t_ -k2 -n | tail -1)
[ -n "$last" ] && [ -f "$last/smoke_result.json" ] && cp "$last/smoke_result.json" "$ROOT/TestResults/smoke_result.json"

python3 - "$SUMMARY" <<'EOF'
import csv, sys, collections
rows = list(csv.DictReader(open(sys.argv[1])))
by = collections.OrderedDict()
for r in rows: by.setdefault(r["scenario"], []).append(r)
for s, rs in by.items():
    ok = sum(1 for r in rs if r["success"] == "true" and r["exit"] == "0")
    print(f"[release-smoke] {s}: {ok}/{len(rs)} passed (seed {rs[0]['seed']})")
EOF
exit $failed
