# Development Plan

## Workflow Rule

For this repository, feature work follows this order:

1. update `docs/GAMEPLAY_DESIGN.md`
2. update this development plan
3. implement the change
4. validate the touched runtime path

Small changes can keep the design and plan notes short, but they should still update the two canonical docs when behavior or architecture changes.

## Current Baseline

The repository has moved beyond the original single-process arena sample.

Completed:

- shared agar-style simulation in `Shared/Gameplay/ArenaSimulation.cs`
- single-player and multiplayer using the shared simulation
- mass/radius/speed growth loop
- food spawning, player consumption, respawn, match timer, and ranking
- Unity scene/script workflow without editor-side gameplay scene baking
- WebSocket control-plane RPC
- KCP realtime-plane RPC
- `AttachRealtimeAsync` for realtime session attachment
- PostgreSQL-backed Orleans clustering and grain persistence
- Orleans-backed matchmaking queue
- durable room/session assignment with runtime gateway endpoint metadata
- realtime-only local session registration for runtime gateway attach
- local compose baseline with PostgreSQL and Redis

Removed from the active plan:

- local memory grain storage as the intended server state model
- localhost-only Orleans clustering as the intended deployment model
- gateway-local matchmaking queue as the intended queue model
- old knockback/dash/buff combat loop as the intended gameplay model
- separate one-off docs for client architecture, production infra, and gateway refactors

## Active Backlog

### Phase 1: Unity Gameplay Regression

- verify single-player start, food growth, player consumption, death, respawn, and settlement in the Unity editor
- verify multiplayer login, matchmaking, realtime attach, input, world snapshots, and settlement with Silo + Server running
- check HUD copy for stale dash/buff/stun language
- check generated Unity `.csproj` and package references pick up KCP cleanly after project refresh

Acceptance criteria:

- local single-player match is playable end-to-end
- multiplayer match can start through matchmaking and receive realtime snapshots
- no visible UI path depends on removed gameplay concepts

### Phase 2: Client Decomposition

- extract match flow responsibilities from `DotArenaGame`
- keep RPC lifecycle in `DotArenaNetworkSession`
- keep callback draining in `DotArenaCallbackInbox`
- reduce `DotArenaSceneUiPresenter` by moving repeated UI construction into smaller helpers or assets
- split `DotArenaMetaProgression` into persistence, reward rules, and summary building when touched

Acceptance criteria:

- mode entry, matchmaking, in-match, and settlement transitions are easier to reason about
- no circular dependency is introduced between `SampleClient.Rpc` and `SampleClient.Gameplay`
- generated RPC code remains untouched

### Phase 3: Gateway Cleanup Semantics

- audit logout, disconnect, matchmaking cancel, room leave, and match-end cleanup
- ensure control and realtime connections can detach independently
- ensure session/room grain state and gateway-local callback state converge after failures
- add focused tests or traceable manual validation for the above flows

Acceptance criteria:

- stale queue tickets are cleared
- stale realtime callbacks are removed
- room runtime removes players on disconnect/leave
- repeated login/logout and match/cancel flows do not leave duplicate local registrations

### Phase 4: Cross-Gateway Realtime Routing Design

- choose the gateway-to-gateway event mechanism: Orleans stream/observer or Redis pub/sub
- define ownership for input forwarding, world-state fan-out, disconnect events, and backpressure
- define ordering, retry, and stale-owner behavior
- update `docs/GAMEPLAY_DESIGN.md` before implementation

Acceptance criteria:

- a player whose control connection is on gateway A can send input to a room runtime on gateway B
- match events can be delivered back to the correct live client connection
- failure behavior is explicit enough to test

### Phase 5: Cross-Gateway Realtime Routing Implementation

- implement the routing layer chosen in Phase 4
- keep room simulation authoritative on one runtime owner at a time
- avoid serializing live callback objects into Redis or Orleans state
- add logging around routing decisions and failed deliveries

Acceptance criteria:

- multi-gateway deployment can handle separate control and realtime/runtime ownership
- world-state broadcast reaches players connected through different gateway nodes
- disconnect/logout/leave cleanup works across gateway ownership boundaries

### Phase 6: Validation And Packaging

- build `Shared/Shared.csproj`
- build `Server/Silo/Silo.csproj`
- build `Server/Server/Server.csproj`
- run available automated tests
- manually smoke-test Unity single-player and multiplayer when Unity is available
- keep README and `CLAUDE.md` aligned when commands, ports, or architecture facts change

Acceptance criteria:

- touched projects compile
- documented run instructions match the current code
- no deleted docs are referenced
