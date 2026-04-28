# Agar-Style Gameplay Design

## Goal

Convert the current `DotArena` from a knockback-elimination arena into a lightweight `agar.io` style game loop:

- every player controls a single cell
- cells grow by consuming pellets
- larger cells can consume sufficiently smaller cells
- larger cells move more slowly
- eliminated players respawn as small cells
- matches end on time limit and rank players by mass first, score second

This scope intentionally targets the core loop first. It does not include split, eject-mass, viruses, or team modes yet.

## Design Principles

- keep one gameplay ruleset in `Shared/Gameplay/ArenaSimulation.cs`
- keep single-player and multiplayer on the same simulation
- minimize RPC surface changes by extending existing state messages instead of replacing them
- preserve the current menu / lobby / meta progression shell unless gameplay data must change

## Scene And UI Workflow

The Unity scene and UI hierarchy are now maintained directly in the checked-in scene and UI scripts.

- `Client/Assets/Scenes/Gameplay.unity` is the source of truth for scene structure
- UI behavior and binding stay in normal runtime scripts such as `DotArenaSceneUiPresenter.cs`
- do not rely on editor-side scene baking to regenerate the gameplay scene

This removes the previous dual-source problem where an editor baker could overwrite manual scene edits.

## Target Gameplay Loop

### 1. Cell Movement

- `W/A/S/D` controls movement direction
- movement is continuous; no dash ability
- move speed scales down as mass grows

Expected feel:

- small cells feel agile
- large cells feel heavy and dominant

### 2. Growth

Growth comes from two sources:

- neutral food pellets scattered across the map
- eating another player when size difference is large enough

Each player tracks:

- `Mass`
- `Radius`
- `Score`

Relationship:

- `Mass` is the authoritative progression value
- `Radius` is derived from `Mass`
- `Score` mirrors match contribution for UI / settlement and can be derived from consumed value

### 3. Player-vs-Player Eating

A player can consume another player when all of the following are true:

- both are alive
- distance is within eat range
- eater radius is sufficiently larger than target radius

On consume:

- target dies and enters respawn timer
- eater gains part or all of target mass
- eater score increases

### 4. Food Spawning

The arena keeps a stable food population.

- food is simple, always-available mass
- when eaten, it immediately respawns elsewhere or is replenished back to target count
- server and local mode share the same deterministic spawning rules already used by `Shared`

### 5. Respawn

When a player is eaten:

- clear temporary movement state
- respawn after a short delay
- respawn at starter mass and starter radius
- no score reset for the overall match

### 6. Match End

The match still uses a round timer.

When time expires:

- rank by `Mass` descending
- use `Score` descending as tie-breaker
- use `PlayerId` as final deterministic tie-breaker

## AI Direction

Bots should stop behaving like push-fighters and instead:

- chase nearby food when small
- chase smaller players when advantage is meaningful
- avoid larger nearby threats
- drift toward open safe areas if trapped

The AI does not need advanced flocking or split prediction in this phase.

## Data Model Changes

### Player State

Add gameplay fields needed by render and ranking:

- `Mass`
- `Radius`
- `MoveSpeed`

Remove or de-emphasize fields tied to old combat loop:

- dash-centric presentation
- stun / knockback-driven feedback

### World State

Keep the existing `WorldState` container and extend `PlayerState`.

Pickups become food pellets for this ruleset. If the existing `PickupState` container is reused, treat `Type` as food flavor / cosmetic category instead of buff semantics.

## Client Presentation

Visual updates:

- player scale follows `Radius`
- overlays show player name + current mass
- pellets are smaller and more numerous
- HUD emphasizes ranking, mass, and survival status

Feedback updates:

- remove messaging centered on dash / buffs
- add messaging for growth, being eaten, and leader changes when useful

## Out Of Scope

Not included in this change:

- split mechanic
- eject mass
- viruses / spikes
- teams
- persistent skill tree changes
- server reconciliation rewrite
