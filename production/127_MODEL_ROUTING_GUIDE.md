# Model Routing Guide

> **Status:** Development workflow guidance, not game design.

Model choice must never change the specification. The documentation/task structure is intended to let routine work use a lower-cost capable coding model while reserving the strongest reasoning model for tasks with substantial architectural uncertainty.

## Routine Narrow Tasks

For small, deterministic tasks with explicit acceptance criteria (for example early player/input/item primitives), a capable coding model at high reasoning effort is normally sufficient.

Examples:
- folder/bootstrap work,
- Input Actions,
- straightforward player movement/aim,
- data definitions,
- simple inventory rules,
- reference weapons after the framework exists,
- editor validators with explicit rules.

## Escalate to Strongest Reasoning Model

Prefer the strongest available reasoning/coding model for:
- seeded dungeon graph generation, socket matching, assembly and reachability validation,
- multiplayer architecture, host authority and state synchronization,
- disconnect/reconnect, downed/revive/spectator state across the network,
- persistence transactions where duplication/loss exploits are possible,
- cross-system refactors,
- difficult race conditions or nondeterministic test failures.

## Claude Code Aliases

If the installed Claude Code version supports model aliases such as `sonnet` and `opus`, a practical workflow is to use the faster capable model for narrow tasks and switch to the strongest model for the categories above. Do not hardcode a marketing generation/version into project design docs; model availability changes faster than the game specification.
