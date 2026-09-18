# RUINRAIL V1 performance report (TASK 143)

Environment: Unity 6000.3.24f1 editor test runner, batchmode, Windows 11 — numbers are relative editor-frame measurements, not a build profile. Build-target device profiling: NOT RUN (no player build profiled in this run).


- Party 1: 10 active enemies, 360 pooled projectile spawns, 180 pooled effects (peak live 64, created 64), 180 damage numbers (created 48), 180 audio events on 24 sources, 40 pickups: avg 0,5 ms, worst 14,5 ms, managed alloc NOT MEASURED — GC.GetAllocatedBytesForCurrentThread reports 0 in this Mono runtime.
- Party 2: 14 active enemies, 360 pooled projectile spawns, 180 pooled effects (peak live 64, created 64), 180 damage numbers (created 48), 180 audio events on 24 sources, 40 pickups: avg 0,5 ms, worst 14,6 ms, managed alloc NOT MEASURED — GC.GetAllocatedBytesForCurrentThread reports 0 in this Mono runtime.
- Party 3: 18 active enemies, 360 pooled projectile spawns, 180 pooled effects (peak live 64, created 64), 180 damage numbers (created 48), 180 audio events on 24 sources, 40 pickups: avg 0,7 ms, worst 18,2 ms, managed alloc NOT MEASURED — GC.GetAllocatedBytesForCurrentThread reports 0 in this Mono runtime.
- Trio cap + Elite + 20 explosive AoE detonations (20 pooled explosion effects, peak live effects 0): avg 0,1 ms, worst 0,6 ms.
- Depth cycle 1: 95 live GameObjects (baseline 35), mono heap 764,78 MB.
- Depth cycle 2: 95 live GameObjects (baseline 35), mono heap 764,77 MB.
- Depth cycle 3: 95 live GameObjects (baseline 35), mono heap 764,77 MB.
- Depth cycle 4: 95 live GameObjects (baseline 35), mono heap 764,77 MB.
- Depth cycle 5: 95 live GameObjects (baseline 35), mono heap 764,76 MB.
- Depth cycle 6: 95 live GameObjects (baseline 35), mono heap 764,76 MB.
- Depth cycle 7: 95 live GameObjects (baseline 35), mono heap 764,76 MB.
- Depth cycle 8: 95 live GameObjects (baseline 35), mono heap 764,76 MB.
- Mono heap growth cycles 2→8: -0,01 MB.


- Biome room pools (63 rooms, validated): 8,1 ms.
- RuinedMetro: host generation avg 2,76 ms (worst 12,10 ms), client rebuild avg 3,28 ms over 30 seed×depth runs; managed allocation NOT MEASURED (GC.GetAllocatedBytesForCurrentThread reports 0 in this Mono runtime).
- Rustworks: host generation avg 2,57 ms (worst 15,19 ms), client rebuild avg 2,23 ms over 30 seed×depth runs; managed allocation NOT MEASURED (GC.GetAllocatedBytesForCurrentThread reports 0 in this Mono runtime).
- OvergrownLabs: host generation avg 2,24 ms (worst 9,70 ms), client rebuild avg 2,47 ms over 30 seed×depth runs; managed allocation NOT MEASURED (GC.GetAllocatedBytesForCurrentThread reports 0 in this Mono runtime).
- Save document with 60 storage items + 9-slot loadout: 13,0 KB; save (atomic write path) avg 0,23 ms, load+validate avg 0,19 ms over 20 iterations.

## Code findings
- Per-frame List allocations removed: EnemyMovesetAttack.Tick (every active Elite/Boss attack cooldown tick), AccessoryPassives target-cooldown Tick, ConsumableEffects.Tick — replaced by reused scratch lists.
- Hot-loop audit (Update/FixedUpdate/LateUpdate/Tick/Step/OnTrigger bodies in gameplay assemblies): no FindObjectsByType/GameObject.Find, no LINQ, no string building found after the fix.
- Pools confirmed reusing instances: ProjectilePool (prewarm 8, returned shots reused — 20 new shots after a volley created 0 instances), EffectPool (cap 64, oldest recycled), DamageNumberPool (cap 48), AudioService one-shots (24 sources, oldest stolen), room prefabs baked.
- Repeated depth transitions (8 cycles, trio cap + loot + shots + effects + audio): live GameObjects constant after the first cycle, mono heap growth 0.00 MB.
