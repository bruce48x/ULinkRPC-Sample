# Agar-Style Development Plan

## Workflow Rule

For this repository, any new feature request should follow this order:

1. update or create a design document
2. update or create a development plan document
3. only then start implementation

If the change is small, the documents can be short, but the step should not be skipped.

## This Change

### Phase 0: Workflow Cleanup

- declare `Client/Assets/Scenes/Gameplay.unity` as the scene source of truth
- remove editor-only scene baking for gameplay UI / arena generation
- keep future UI iteration on direct scene edits plus normal runtime scripts

### Phase 1: Shared Simulation

- replace knockback-elimination rules in `Shared/Gameplay/ArenaSimulation.cs`
- add mass / radius / speed-based movement
- replace buff spawning with dense food spawning
- implement player-eats-player resolution
- keep respawn and match timer flow

### Phase 2: Shared Contracts

- extend `Shared/Interfaces/IPlayerService.cs`
- add state fields required by client render:
  - `Mass`
  - `Radius`
  - `MoveSpeed`
- keep message compatibility simple by only extending existing payloads

### Phase 3: Server Runtime

- verify `Server/Server/Services/GameArenaRuntime.cs` still works with the new simulation
- preserve login / logout / world broadcast flow
- keep win persistence on match end

### Phase 4: Client Rendering And HUD

- update `Client/Assets/Scripts/Gameplay/DotArenaWorldSynchronizer.cs`
- update `Client/Assets/Scripts/Gameplay/DotArenaDotView.cs`
- update `Client/Assets/Scripts/Gameplay/DotArenaPresentation.cs`
- update `Client/Assets/Scripts/Gameplay/DotArenaGame.cs`
- replace score/buff language with mass/rank language

### Phase 5: AI Tuning

- adjust bot target selection around food, prey, and threats
- ensure bots remain active in both single-player and multiplayer matches

### Phase 6: Validation

- compile shared and server code
- run available tests if present
- check for Unity-side script errors where possible
- verify no editor workflow still references the removed gameplay scene baker

## Acceptance Criteria

- single-player starts and produces a playable growth loop
- players visibly grow after eating food
- larger players can consume smaller players
- movement speed clearly decreases with growth
- death and respawn still function
- multiplayer state payloads still compile and broadcast
- settlement still completes on timer
- no remaining gameplay workflow depends on a scene bake command
