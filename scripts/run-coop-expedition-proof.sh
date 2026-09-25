#!/usr/bin/env bash
# Built-player co-op expedition proof: one host and N-1 clients of the shipped macOS player play one real expedition
# over UnityTransport on loopback (see Assets/Game/Scripts/App/CoopExpeditionProof.cs).
#
#   ./scripts/run-coop-expedition-proof.sh <out-dir> [size=2] [seed=11] [port=7940]
#
# Writes host.json / clientN.json (step results + per-step peer views) and the peers' logs into <out-dir>.
# Exit code 0 only when every peer reports success. Build the player first (ReleaseBuildTool.BuildMacBatch).
set -uo pipefail

OUT="${1:?usage: $0 <out-dir> [size] [seed] [port]}"
SIZE="${2:-2}"
SEED="${3:-11}"
PORT="${4:-7940}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="$(cd "$SCRIPT_DIR/.." && pwd)"
PLAYER="$PROJECT_PATH/Builds/MacOS/RUINRAIL.app/Contents/MacOS/RUINRAIL"

if [[ ! -x "$PLAYER" ]]; then
  echo "NOT RUN — no built player at $PLAYER" >&2
  exit 127
fi

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
SAVES="$(mktemp -d)"

"$PLAYER" -batchmode -nographics -coop-expedition host -coop-port "$PORT" -coop-size "$SIZE" -seed "$SEED" \
  -coop-out "$OUT/host.json" -savedir "$SAVES/host" -logFile "$OUT/host.log" >/dev/null 2>&1 &
HOST_PID=$!
sleep 3
CLIENT_PIDS=()
for i in $(seq 1 $((SIZE - 1))); do
  "$PLAYER" -batchmode -nographics -coop-expedition client -coop-index "$i" -coop-port "$PORT" -coop-size "$SIZE" -seed "$SEED" \
    -coop-out "$OUT/client$i.json" -savedir "$SAVES/client$i" -logFile "$OUT/client$i.log" >/dev/null 2>&1 &
  CLIENT_PIDS+=($!)
  sleep 1
done

STATUS=0
wait "$HOST_PID" || STATUS=1
for pid in "${CLIENT_PIDS[@]}"; do wait "$pid" || STATUS=1; done
rm -rf "$SAVES"

grep -h "COOP-PROOF .*success=" "$OUT"/*.log
if [[ $STATUS -eq 0 ]]; then echo "PASS: every peer reported success"; else echo "FAIL: see $OUT"; fi
exit $STATUS
