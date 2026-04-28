# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Workflow

New features must follow: **design doc → development plan → implementation**. See `docs/GAMEPLAY_DESIGN.md` and `docs/DEVELOPMENT_PLAN.md` for the current pattern. Even small changes should include a short design note before coding.

## Build & Run

This is a Unity client + .NET server project. There are no CLI build scripts; development happens in IDEs:

- **Client**: Open `Client/Client.sln` or the `Client/` folder in Unity Editor (2022+). The main scene bootstraps automatically via `DotArenaBootstrap.EnsureGame()`—no scene setup needed.
- **Server Silo** (Orleans host): `dotnet run --project Server/Silo/Silo.csproj`
- **Server App** (RPC + game runtime): `dotnet run --project Server/Server/Server.csproj`
- **Shared**: `dotnet build Shared/Shared.csproj`

Server uses .NET 10.0 and requires both Silo and Server to be running for multiplayer. Start Silo first, then Server.

## Architecture

### Three-layer structure

```
Shared/          — Protocol contracts (MemoryPack-serializable) + gameplay kernel
Server/          — .NET host: ULinkRPC WebSocket transport + Orleans actor persistence
Client/          — Unity project: rendering, input, HUD, meta progression
```

### Shared kernel (`Shared/Gameplay/ArenaSimulation.cs`)

**This is the single source of truth for game rules.** Both single-player (local) and multiplayer (server-authoritative) modes use this exact same class. It owns:

- Player movement with mass-based speed decay
- Food spawning and collection
- Player-vs-player consumption (larger eats smaller by mass ratio)
- AI bot decision-making (food-seeking, prey-chasing, threat-avoidance)
- Respawn, match timer, shrinking arena, winner determination
- `WorldState` snapshot generation for broadcast/rendering

Key tuning knobs live in `ArenaSimulationOptions` (masses, speeds, ratios, bot weights, etc.) and `ArenaConfig` (arena size, collision radii).

### RPC contract (`Shared/Interfaces/IPlayerService.cs`)

Defines the ULinkRPC service interface with MemoryPack-serializable messages:

- **Client → Server**: `LoginAsync`, `SubmitInput(InputMessage)`, `LogoutAsync`
- **Server → Client** (push callbacks): `OnWorldState(WorldState)`, `OnPlayerDead(PlayerDead)`, `OnMatchEnd(MatchEnd)`

`InputMessage` carries `playerId, moveX, moveY, dash, tick`. The `dash` field is preserved in protocol but unused in the agar ruleset.

### Server

| File | Role |
|---|---|
| `Server/Services/PlayerService.cs` | Per-connection RPC handler. Validates login via Orleans `IUserGrain`, delegates to `GameArenaRuntime` |
| `Server/Services/GameArenaRuntime.cs` | Wraps `ArenaSimulation`. Runs a 50ms tick loop. Broadcasts `WorldState`/`PlayerDead`/`MatchEnd` to all connected callbacks. Persists wins to Orleans on match end |
| `Server/Services/GameArenaRuntimeRegistry.cs` | Static singleton holder for the runtime instance |
| `Server/Hosting/GameArenaHostedService.cs` | `BackgroundService` that starts the arena tick loop |
| `Server/Hosting/RpcServerHostedService.cs` | Starts the ULinkRPC WebSocket listener on port 20000 |
| `Server/Orleans/ClusterClientRuntime.cs` | Static Orleans client initialization |

**Server/Silo** is the Orleans host with `IUserGrain` implemented by `UserGrain`. Uses in-memory grain storage (`AddMemoryGrainStorage("users")`). The grain handles password hashing (SHA256), login counting, session tokens, and win persistence.

### Client (`Client/Assets/Scripts/Gameplay/`)

The main MonoBehaviour is `DotArenaGame` — it self-bootstraps via `[RuntimeInitializeLoadOnLoadMethod]` and owns all game state.

| File | Role |
|---|---|
| `DotArenaGame.cs` | Main entry point. Manages mode switching (single/multiplayer), owns the tick loop, delegates to sub-components |
| `DotArenaGame.Types.cs` | Data types: `PlayerRenderState`, enums (`EntryMenuState`, `SessionMode`, `FrontendFlowState`), `MatchSettlementSummary` |
| `DotArenaNetworkSession.cs` | WebSocket RPC connection wrapper. Handles connect, login, disconnect lifecycle |
| `DotArenaCallbackInbox.cs` | Thread-safe inbox for server callbacks (WorldState, PlayerDead, MatchEnd). Drained on main thread |
| `DotArenaWorldSynchronizer.cs` | Applies `WorldState` to view objects. Creates/removes dot views, handles interpolation position tracking, triggers collision jelly feedback |
| `DotArenaPresentation.cs` | Color resolution (stable hash-based palette for players, pickup colors, cosmetic skins) |
| `DotArenaTuning.cs` | All visual/motion constants: colors, sorting orders, camera settings, font sizes, timing |
| `DotArenaSceneUiPresenter.cs` | Procedural UI construction — builds all panels (mode select, matchmaking, lobby, settlement, HUD) via GameObject creation, no prefabs |
| `DotArenaImmediateHudRenderer.cs` | OnGUI-based debug HUD overlay with player name/score labels |
| `DotArenaMetaProgression.cs` | Client-side meta: levels, currency, tasks, match history, shop, leaderboard. Persisted to per-player JSON files |
| `DotArenaInputUtility.cs` | W/A/S/D input polling |
| `DotArenaDotView.cs` | Player dot GameObject with jelly shader feedback |
| `DotArenaPickupView.cs` | Food pickup GameObject rendering |
| `DotArenaSpriteFactory.cs` | Procedural sprite generation (circles, borders) |

### Synchronization model

**Single-player**: `DotArenaGame` runs a local `ArenaSimulation` instance, calling `_localMatch.Tick()` at fixed intervals. No network involved.

**Multiplayer**: Client sends `SubmitInput` at 20Hz (50ms). Server runs `ArenaSimulation.Tick()` at 20Hz and broadcasts `WorldState` to all connected clients via the callback channel. Client interpolates positions over ~100ms to smooth snapshot updates.

### RPC generated code

The `Server/Server/Generated/` and `Client/Assets/Scripts/Rpc/Generated/` directories contain ULinkRPC-generated bindings. These should not be hand-edited.

## Key Constants

- Tick interval: 50ms (20Hz) — both server and single-player
- Max round duration: 120 seconds
- Target players per match: 4 (filled with bots)
- Default arena: 50x50 units (100x100 world units)
- Server port: 20000, path: `/ws`
- Client input is `Vector2` clamped to [-1, 1]
