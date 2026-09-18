# RUINRAIL — Execute Completion Phase TASK 149 through TASK 184

You are continuing the existing RUINRAIL Unity 6.3 LTS repository after the completed TASK 001–148 engineering run.

Read first, in this order:

1. `CLAUDE.md`
2. `production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md`
3. `production/135_TASK148_GAP_ANALYSIS_ADDENDUM.md`
4. `production/132_COMPLETION_ROADMAP_TASK149_184.md`
5. `production/133_REVIEW_GATED_COMPLETION_RUNNER.md`
6. `production/134_FINAL_PRODUCTION_ASSET_MANIFEST_RULES.md`
7. `art/106_RUINRAIL_FINAL_ART_BIBLE.md`

Then execute TASK 149 onward strictly in numeric order under the review-gated runner.

Critical rules:

- TASK001–148 are the proven engineering baseline; do not rewrite them without fresh regression evidence.
- No new gameplay feature scope. This phase is production completion/release only.
- Preserve exact existing content counts and core rules unless an approved spec or TASK179/TASK182 evidence authorizes a numerical tune.
- Final art direction: original RUINRAIL work; Soul Knight informs sprite construction/readability/geometry, Zero Sievert strongly informs gritty pixel-world presentation, Fallout 4 strongly informs post-apocalyptic atmosphere/material language, ARC Raiders moderately informs industrial sci-fi/tech accents. Never rip/trace/recolor reference-game assets.
- Stop at all human/external gates. Never self-approve visual quality, audio quality, design feel, fun, live-service evidence or target-hardware profiling.
- Real asset tasks may block until final source assets exist. Report exact missing manifest roles.
- Real Sessions/Relay proof must use live Unity services and two built clients; mocks/fake transport cannot satisfy TASK181.
- Never commit credentials/secrets.
- Do not create TASK185.

At each task, append an immutable checkpoint to `production/COMPLETION_RUN_LOG.md` with exact tests/results and blocker state. Continue automatically only when the runner permits it.
