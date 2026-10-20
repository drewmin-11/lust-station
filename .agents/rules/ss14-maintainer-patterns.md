---
trigger: always_on
---

# Rule: Best Practices and Patterns from Top Maintainers (kanopus952, ThereDrD0, VigersRay)

This rule encapsulates proven architectural patterns and engineering standards extracted from top SS14 / Sunrise / Lust maintainers.

---

## 1. The `kanopus952` Architectural Patterns

### A. Domain-Driven `partial` Decomposition

Large systems must never live in a single monolithic file. Split them into domain-specific partial files:
- `SharedFooSystem.cs` (lifecycle, dependencies, core events)
- `SharedFooSystem.Flow.cs` (state flow, transitions)
- `SharedFooSystem.Tracking.cs` (progress, distance, counters)
- `SharedFooSystem.Visuals.cs` (bubbles, appearance, overlays)

### B. Micro-Systems for Conditions & Triggers

Instead of a single massive event handler trying to handle 20 different conditions:
- Break conditions into dedicated micro-systems (typically 20–40 lines each):
  - `DoorOpenConditionSystem.cs`
  - `HoldInHandsConditionSystem.cs`
  - `SitConditionSystem.cs`
- Each micro-system subscribes only to its specific event and updates the relevant component/progress.

### C. Pure Data-Driven Sequences

- Never encode multi-step game logic, tutorials, or craft sequences as giant `switch-case` statements in C#.
- Define sequences in YAML prototypes via strongly-typed `IPrototype` classes (`StepPrototype`, `SequencePrototype`).

---

## 2. The `ThereDrD0` Engineering Standards

### A. Strict Unhardcoding ("Анхардкод")

- Never hardcode filesystem paths, webhook targets, prototype IDs, or configuration values in code.
- Always use `CVars`, `ProtoId<T>`, or configuration files.
- If two or more places share a value, extract it to a shared constant or configuration model.

### B. Fork Boundaries & Upstream Protection

- Keep modifications strictly inside fork project folders (`_Sunrise`, `_Lust`, etc.).
- Mark vanilla hooks with exact single-line markers (`Sunrise-Edit`) or block markers (`Sunrise edit start` / `Sunrise edit end`).
- Keep diffs to upstream files as minimal as humanly possible (1-3 lines per hook).

### C. Professional Changelogs and Commits

- Changelogs must describe the user-facing outcome clearly in active voice.
- Technical implementation details belong in PR description, never in the player changelog.

---

## 3. The `VigersRay` Safety & ECS Conventions

### A. Defensive ECS (Null & Delete Safety)

- ❌ **NEVER** use `_query.Get(uid)` on entities that might have been deleted or lacked the component:

  ```csharp
  // BAD: Throws exception and crashes if entity was deleted!
  var artifact = _xenoArtifactQuery.Get(node.Attached.Value);
  ```

- ✅ **ALWAYS** use safe pattern with early return or `continue`:

  ```csharp
  if (!_xenoArtifactQuery.TryGetComponent(node.Attached.Value, out var artifact))
      continue;
  ```

### B. Modern `Entity<T>` Idiom

- Pass `(uid, component)` as `Entity<TComponent>` to helper methods rather than passing bare `EntityUid` and re-querying the component:

  ```csharp
  Trigger((node.Attached.Value, artifact), (uid, comp, node));
  ```

### C. State-Driven Network Synchronization

- Do not spray network messages from random click/verb handlers.
- Bind network sync to component state or cache change events:

  ```csharp
  CacheChanged += () =>
  {
      var sync = new MsgPlayerCacheSync { Cache = _cache };
      _netManager.ClientSendMessage(sync);
  };
  ```

### D. Extreme PR Atomicity

- Keep bugfixes and targeted tweaks contained to 1–3 files and < 50 lines of code.
- Never mix a bugfix with a refactor or new content.
