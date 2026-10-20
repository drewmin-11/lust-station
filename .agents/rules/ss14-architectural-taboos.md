---
trigger: always_on
---

# Rule: SS14 Architectural Taboos & Anti-Patterns

This rule defines strict architectural prohibitions and core development principles for Space Station 14 based on official documentation (docs.spacestation14.com and wiki.team.ss14.org).

---

## 1. Taboo: Stateful Entity Systems & Memory Leaks

Entity systems (`EntitySystem`) in Robust Toolbox / SS14 MUST BE STATELESS.

* ❌ **PROHIBITED**: Storing mutable entity states, timers, cooldowns, or lifecycle collections of entities in system fields. System dependencies (`[Dependency]`) and safe `EntityQuery<T>` caches stored in system fields and populated via dependency injection or `Initialize()` are permitted:

  ```csharp
  // TERRIBLE: Memory leak when entities are deleted or moved!
  private readonly Dictionary<EntityUid, TimeSpan> _lastPuffTime = new();
  private readonly HashSet<EntityUid> _activeEntities = new();
  ```

* ✅ **MANDATORY**: All entity state must reside strictly within components (`Component`):

  ```csharp
  [RegisterComponent, AutoGenerateComponentPause]
  public sealed partial class SmokableComponent : Component
  {
      [AutoPausedField]
      public TimeSpan LastPuffTime;
  }
  ```

---

## 2. Taboo: Core Atmosphere Expansion for Vanity / Content Effects

* ❌ **STRICTLY PROHIBITED**: Modifying `Content.Shared/Atmos/Atmospherics.cs`:
  - Never increment `TotalNumberOfGases`.
  - Never add custom gases to `enum Gas` for cosmetic items (tobacco smoke, vape clouds, fragrances, etc.).
* **Why**: Atmosphere is an ultra-hot path. Every gas added allocates an extra index on every tile, pipe network, and `GasMixture` across the entire server grid.
* ✅ **CORRECT WAY**:
  - For cosmetic vapors/smoke, use particle emitters, spawners, visual entities, or existing gases (`WaterVapor`) without touching core enums.

---

## 3. Taboo: Hardcoding Prototypes, Reagents, and Sprites in C# (Anti-Data-Driven)

SS14 is a data-driven engine. C# code must not hardcode specific prototype IDs or sprite states.

* ❌ **PROHIBITED**: Hardcoding reagent IDs, prototype names, or sprite state strings in C# logic:

  ```csharp
  // TERRIBLE: Any new juice or sprite requires modifying C# and recompilation!
  if (solution.GetTotalPrototypeQuantity("JuiceLime") > 0)
      state = "vape-mint";
  else if (solution.GetTotalPrototypeQuantity("JuiceBerry") > 0)
      state = "vape-berry";
  ```

* ✅ **MANDATORY**:
  - Use `GenericVisualizer` in YAML for sprite state switching based on appearance data.
  - Or derive visual appearance from data (e.g., `solution.GetColor()` for tinting).
  - If a C# system needs prototype references, use `[DataField] ProtoId<ReagentPrototype>` or `[DataField] Dictionary<ProtoId<ReagentPrototype>, string>`.
  - Never compare magic strings. Use `ProtoId<T>` constants if a system-level default is truly required.

---

## 4. Don't Reinvent the Wheel: Check Existing Prototype Components

Reviewer rule: *"What is the point of most systems if they do what already exists in prototypes?"*

Before writing a new `EntitySystem` and `Component`:

* ❌ **PROHIBITED**: Writing custom systems for container slots, eating/drinking, exams, or reagent dispensing when engine systems already exist.
* ✅ **MANDATORY**:
  - For insertable cartridges / magazines: use `ItemSlotsComponent` and `ContainerSystem`.
  - For consumables (chewing gums, snacks): use `FoodComponent` / `EdibleComponent` with reagent solution.
  - For transferring reagents: use `SolutionContainerSystem` / `RefillableSolutionComponent`.
  - For delays: use standard `DoAfter` system.
  - For clicks / triggers: use `UseInHandEvent`, `ActivateInWorldEvent`.

---

## 5. System Decomposition via `partial` Classes

Do not write 700-line monolithic systems.

* ❌ **PROHIBITED**: A single huge system file doing 10 different unrelated things (vaping, zippo tricks, patches, addiction, rings).
* ✅ **MANDATORY**:
  - If extending a base system (like `SmokingSystem`), split into partial files by domain:
    - `SmokingSystem.cs` (lifecycle, shared helpers)
    - `SmokingSystem.Puff.cs` (puff mechanics)
    - `SmokingSystem.Vape.cs` (vape logic)
  - Or create separate, cleanly scoped systems in their own files.

---

## 6. Time, Pausing, and Field Deltas

* Always use `TimeSpan` (via `_timing.CurTime`), never float frametime accumulators.
* Any component with runtime-modified `TimeSpan` fields must have `[AutoGenerateComponentPause]` on the class and `[AutoPausedField]` on the fields.
* For components with multiple fields changing independently, use `[AutoGenerateComponentState(fieldDeltas: true)]` and `DirtyField(...)`.

---

## 7. PR Hygiene: Atomic PRs Over Kitchen Sink PRs

* ❌ **PROHIBITED**: "Kitchen Sink" PRs combining 10 distinct features (e.g. vapes + zippos + nicotine addiction + core atmos gas + smoke rings in 67 files).
* ✅ **MANDATORY**:
  - 1 PR = 1 cohesive feature or bugfix (typically < 300–400 lines of code).
  - Easy to review, easy to test, fast to merge.

---

## 8. Taboo: Legacy `[DataField("name")]` and DataFields on Runtime State

* ❌ **PROHIBITED**: Using explicit string names in `[DataField("name")]` in modern Robust Toolbox code:

  ```csharp
  // BAD: Legacy positional string in DataField causes static analyzer errors (ss14-datafield-must-not-use-legacy-name-string)
  [DataField("cooldown")]
  public TimeSpan Cooldown;
  ```

* ❌ **PROHIBITED**: Putting `[DataField]` on runtime-generated fields, cooldown timestamps, or transient entities:

  ```csharp
  // BAD: Serializing runtime state into YAML prototypes causes unexpected data corruption and breaks map saving!
  [DataField, AutoPausedField]
  public TimeSpan LastPuffTime;
  ```

* ✅ **MANDATORY**:
  - Use bare `[DataField]` without string parameters; YAML property names are automatically derived in camelCase from the C# member name.
  - Runtime state (like `LastPuffTime`, `Shooter`, `SpawnTime`, `Dissipated`, `CurrentFlavor`, `ActionEntity`) must NOT have `[DataField]`. For pausing, use only `[AutoPausedField]`.

---

## 9. Taboo: Event Subscriptions Outside `Initialize()`

* ❌ **PROHIBITED**: Calling `SubscribeLocalEvent` inside arbitrary helper methods or secondary initializers (`InitializeVapes()`, `InitPuff()`, etc.):

  ```csharp
  // BAD: ast-grep and engine rule violation (ss14-subscribelocalevent-only-in-initialize)!
  private void InitializeVapes()
  {
      SubscribeLocalEvent<VapeComponent, UseInHandEvent>(OnVapeUseInHand);
  }
  ```

* ✅ **MANDATORY**: All subscriptions must reside strictly inside `public override void Initialize()`.

---

## 10. Taboo: Bloating Vanilla Upstream Files (Upstream Maintenance)

* ❌ **PROHIBITED**: Dumping hundreds of lines of fork-specific mechanics directly into vanilla upstream systems (e.g. adding 400 lines into `SmokingSystem.Vape.cs` or adding fields to vanilla `VapeComponent.cs`).
* ✅ **MANDATORY**:
  - Create standalone systems in the fork folder (`Content.Shared/_Sunrise/...`, `Content.Server/_Sunrise/...`).
  - Keep vanilla upstream files clean with 0 lines or minimal 1-2 line stubs/hooks enclosed in `// Sunrise edit start - reason` / `// Sunrise edit end`.

---

## 11. Mandatory Pattern: Client-Side Prediction for Interactive Actions

* ❌ **PROHIBITED**: Handling user interactions (`UseInHandEvent`, `AfterInteractEvent`, click triggers) and DoAfter starts solely on the server (`Content.Server`).
  - Server-only handling introduces severe input latency, delayed DoAfter bars, and audio sync issues.
* ✅ **MANDATORY**:
  - User interactions and initial validation (`CanDo`), predicted audio (`_audio.PlayPredicted`), predicted popups (`_popup.PopupPredicted`), and `_doAfter.TryStartDoAfter(...)` belong in **`Content.Shared`** systems.
  - The server system inherits the shared system and only subscribes to the completed DoAfter event to execute irreversible state changes (chemical transfer, atmosphere emission, entity spawning).
