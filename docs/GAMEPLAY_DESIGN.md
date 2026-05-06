# Gameplay And Architecture Design

## Purpose

This document is the single design entry point for the sample game. Keep gameplay rules, client/server boundaries, and production-infrastructure decisions here instead of creating one-off design notes.

The project validates ULinkRPC in a lightweight multiplayer `agar.io` style game that also supports a fully offline single-player mode.

## Gameplay Goal

Players control one cell in a square arena:

- collect food pellets to gain mass
- consume sufficiently smaller players
- move more slowly as mass increases
- respawn after being consumed
- finish the match on a timer and rank by mass first, score second

The current scope intentionally excludes split, eject-mass, viruses, spikes, teams, and a persistent skill tree.

## Design Principles

- `Shared/Gameplay/ArenaSimulation.cs` is the single source of truth for gameplay rules.
- Single-player and multiplayer use the same simulation.
- The client sends input; the authoritative multiplayer runtime sends world snapshots.
- Extend existing RPC payloads when possible instead of replacing the contract.
- Keep scene structure in checked-in Unity scene/scripts. Do not reintroduce editor-side gameplay scene baking.
- Keep long-lived design state in this file and implementation sequencing in `docs/DEVELOPMENT_PLAN.md`.

## Gameplay Rules

### Movement

- `W/A/S/D` controls movement direction.
- Movement is continuous.
- The legacy `dash` input field remains in the protocol for compatibility, but the agar ruleset does not use dash.
- Speed decreases as mass grows.

Expected feel:

- small cells are agile
- large cells are slower but threatening

### Growth

Growth comes from:

- neutral food pellets scattered around the arena
- consuming smaller players

Each player tracks:

- `Mass`
- `Radius`
- `MoveSpeed`
- `Score`

`Mass` is the authoritative progression value. `Radius` and `MoveSpeed` are derived gameplay/presentation values. `Score` tracks match contribution for UI and settlement.

### Player Consumption

A player can consume another player when:

- both players are alive
- distance is within consume range
- the eater is larger by the configured ratio

On consume:

- the target dies and starts a respawn timer
- the eater gains mass
- the eater gains score

### Food

The arena maintains a target food population.

- Food is simple always-available mass.
- Consumed food is replenished back toward the target count.
- Server and local modes use the shared deterministic spawning rules.

### Respawn

When a player is consumed:

- clear temporary movement state
- respawn after a short delay
- restore starter mass/radius
- keep accumulated match score

### Match End

The match ends on the round timer.

Ranking order:

1. `Mass` descending
2. `Score` descending
3. `PlayerId` ascending as deterministic tie-breaker

## AI

Bots should:

- chase nearby food when small
- chase smaller players when the advantage is meaningful
- avoid larger nearby threats
- drift toward safer open areas when pressured

The AI target is practical match activity, not advanced flocking or split prediction.

## Runtime Architecture

### Shared

`Shared` contains:

- MemoryPack-serializable RPC contracts in `Shared/Interfaces/IPlayerService.cs`
- gameplay simulation in `Shared/Gameplay/ArenaSimulation.cs`
- shared arena configuration in `Shared/Gameplay/ArenaConfig.cs`

`Shared` must not depend on Unity, server hosting, transport setup, persistence, or UI.

### Client

The Unity client owns mode selection, input, rendering, HUD, local progression, and local single-player simulation.

Important boundaries:

- `Client/Assets/Scenes/Gameplay.unity` is the scene source of truth.
- `Client/Assets/Scripts/Rpc` owns transport creation and generated RPC binding access.
- `Client/Assets/Scripts/Gameplay` owns runtime orchestration, world synchronization, view objects, HUD, and local meta progression.

Assembly direction:

```txt
Shared -> ULinkRPC.Generated -> SampleClient.Rpc -> SampleClient.Gameplay
```

`Gameplay` may depend on RPC helpers. RPC helpers must not depend back on gameplay code.

Known client refactor targets:

- `DotArenaGame` is still the main application coordinator.
- `DotArenaSceneUiPresenter` is large and still owns runtime widget construction.
- `DotArenaMetaProgression` mixes persistence, reward rules, and summary building.

### Server Gateway

`Server/Server` is the RPC gateway and room runtime host.

Current responsibilities:

- WebSocket control-plane RPC for login, logout, matchmaking, and low-frequency business APIs
- KCP realtime-plane RPC for match input/session attachment
- gateway-local `SessionDirectory` for live callback objects
- room runtime ownership through `RoomRuntimeHost` / `RoomRuntime`
- pushing `WorldState`, death, settlement, and matchmaking status callbacks

### Orleans Silo

`Server/Silo` owns durable Orleans grains:

- user identity and win persistence
- player sessions
- matchmaking queue state
- room assignment/snapshot state

PostgreSQL is the configured Orleans ADO.NET clustering and grain persistence backend. Local memory grain storage and localhost-only clustering are no longer the current design.

### Infrastructure

The local production-like baseline is:

- `docker-compose.yml` starts PostgreSQL and Redis
- PostgreSQL stores Orleans membership and grain state
- `Server/Silo/appsettings.json` and `Server/Server/appsettings.json` externalize cluster, service, database, gateway, and realtime endpoint settings
- Redis is available for later routing/presence/pub-sub work, but it is not yet a required runtime path for realtime gameplay

## Multiplayer Flow

Control connection:

1. Client connects to the WebSocket RPC server.
2. Client logs in.
3. Client enqueues for matchmaking.
4. Gateway calls `IMatchmakingGrain`.
5. Matchmaking/room grains assign a room and runtime gateway.
6. Gateway pushes `MatchmakingStatusUpdate` with `RealtimeConnection`.

Realtime connection:

1. Client opens KCP RPC to `RealtimeConnection`.
2. Client calls `AttachRealtimeAsync` with player/session/room/match tokens.
3. Runtime gateway registers a realtime callback.
4. Client sends input through realtime RPC.
5. Room runtime broadcasts world snapshots through realtime callbacks.

`SessionDirectory` may contain a realtime-only local registration, which allows the control connection and realtime connection to land on different gateway nodes when the room runtime belongs to the realtime gateway.

## Current Distributed Boundary

Already distributed or durable:

- Orleans membership and grain persistence use PostgreSQL.
- Matchmaking queue state lives in Orleans.
- Room assignment includes explicit runtime gateway endpoint metadata.
- Clients receive an explicit realtime target instead of assuming the control gateway owns the room.
- Realtime attach no longer requires a local control callback registration.

Still local to a gateway process:

- live RPC callback objects
- active room runtime simulation
- world-state broadcast fan-out
- some disconnect/logout/leave cleanup semantics

The next distributed architecture step is a gateway-to-gateway input and event routing layer. Candidate mechanisms are Orleans streams/observers or Redis pub/sub. Redis should only enter that path with a clear ownership, ordering, and failure model.

## Client Presentation

- Player scale follows `Radius`.
- Overlays emphasize name, mass, rank, and survival state.
- Pellets are small and numerous.
- HUD language should use mass/rank/growth wording, not dash/buff wording.
- Player collision/growth feedback may use jelly presentation, but stun/knockback should not be treated as core gameplay.

## Out Of Scope

- split mechanic
- eject mass
- viruses/spikes
- teams
- persistent skill tree changes
- server reconciliation rewrite
- automatic room runtime migration
- Redis-backed realtime routing without a separate design update
