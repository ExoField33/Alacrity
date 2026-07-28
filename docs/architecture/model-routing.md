# Model Routing

## Routing table

| Tier | Agent | Model configuration | Appropriate work |
|---|---|---|---|
| Complex architecture | `architect-luna-high` | GPT-5.6 Luna, HIGH | Architecture, uncertain native work, multi-layer design |
| Complex risk review | `risk-reviewer-luna-high` | GPT-5.6 Luna, HIGH | Hooks, interop, memory, threading, networking, final risk review |
| High | `lead-high` | GPT-5.5, HIGH | Default orchestration, integration, moderately complex reasoning |
| High review | `reviewer-high` | GPT-5.5, HIGH | Nontrivial code review without native-risk escalation |
| Medium | `implementer-medium` | GPT-5.5, MEDIUM | Bounded feature implementation and tests |
| Cheap | `mechanic-cheap` | GPT-5 nano, LOW | Exact mechanical edits only |
| Cheap verification | `verifier-cheap` | GPT-5 nano, LOW | Build, test, format, benchmark, and diff reporting |
| Cheap exploration | built-in `explore` | GPT-5 nano, LOW | File listing, symbol location, and exact evidence gathering |

## Decision sequence

1. Determine whether the task is mechanical, bounded implementation, integration, or architecture/risk.
2. Use the cheapest tier that can complete the work without making an unapproved judgment.
3. Escalate immediately when an assumption is required.
4. Keep XHIGH invocations narrow and supply the relevant files, constraints, and question.
5. Validate every implementation using deterministic tools.
6. The lead reviews the integrated diff.
7. Use XHIGH risk review for native, unsafe, threading, lifetime, rendering-ownership, or network changes.

## Examples

### Cheap

- Change `Padding = 8` to `Padding = 10`.
- Move the player-list panel 4 pixels left.
- Run `dotnet test`.
- Rename a symbol using an exact old-to-new mapping.
- Find every reference to `ChatOverlay`.

### Medium

- Implement sorting and filtering for immutable `PlayerSnapshot` values.
- Add chat timestamp formatting from an approved specification.
- Add unit tests for an existing parser.
- Extract a pure helper while preserving public behavior.

### High

- Integrate a player-list feature across input, snapshots, and UI.
- Diagnose a performance regression spanning several managed projects.
- Decide how to isolate feature failures.
- Review a moderately complex optimization.

### XHIGH

- Select or change a native hook.
- Review calling-convention evidence.
- Change unload or shutdown behavior.
- Design a thread-safe handoff from a hook to the render layer.
- Investigate memory corruption or nondeterministic crashes.
