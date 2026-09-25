---
name: coop-peer-proof-gotchas
description: Two-process co-op peer proof: both peers need an identical NetworkConfig, and the host must outlive the client's sample
metadata:
  type: feedback
---

Running the built-player co-op peer proof (`RUINRAIL -coop-peer host|client -coop-port N -coop-size N -coop-out f.json`):

- **Both peers must declare the same `NetworkConfig`.** NGO compares the whole config on connect, so if the host sets
  `ConnectionApproval = true` and the client does not, the host logs `Incomplete connection request message given config
  - possible NetworkConfig mismatch` and the client simply never connects. The approval callback is never reached.
- **The host has to stay up while each client takes its measurements**, and each client has to linger while the host
  takes its own. Otherwise a peer samples a session that has already been torn down and reports party=0 even though the
  session was fine. Sample at the moment the party is first complete, then linger.
- `-coop-size` must be passed to clients too, so an early joiner waits for the full party before it starts measuring.

**Why:** each of these produced a false negative that looked like a product bug and cost a full build+run cycle (~4 min each).

**How to apply:** when a peer reports `party=0`/`never connected`, check the config symmetry and the process lifetimes
before suspecting the composition. See [[unity-needs-two-runs-smart-app-control]] and
[[built-player-smoke-seed-flaky-on-mac]] for the other built-player run hazards.
