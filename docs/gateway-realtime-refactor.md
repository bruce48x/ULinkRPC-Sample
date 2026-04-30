# Gateway Realtime Refactor

## Goal

- Lobby login and non-realtime business requests stay on the WebSocket RPC control plane.
- Matchmaking, room assignment, realtime session ownership, and battle orchestration move into the gateway process.
- Orleans remains for account identity and post-match persistence only, removing one extra hop from online battle traffic.

## New boundary

- `Server/Server/Services/GatewayMatchmakingService.cs`
  Owns the in-memory queue, room creation, player-to-room assignment, and match status fan-out.
- `Server/Server/Realtime/RoomRuntime*.cs`
  Remains the authoritative battle runtime, but no longer mirrors room/session lifecycle through Orleans grains.
- `Server/Silo`
  Keeps `IUserGrain` for login, score accumulation, and win persistence. Matchmaking/session/room grains are no longer on the critical realtime path.

## Transport split

- WS control plane:
  login, logout, matchmaking requests, room readiness notifications, and other low-frequency business APIs.
- Realtime plane:
  `MatchmakingStatusUpdate.RealtimeConnection` now carries the target transport, host, port, path, room id, match id, and session token needed for a future dedicated realtime client.

## Current implementation

- Control server transport:
  `ULinkRPC.Transport.WebSocket`
- Realtime server/client transport:
  `ULinkRPC.Transport.Kcp`
- Client flow:
  after WS matchmaking succeeds, the client opens a second RPC connection over KCP and attaches with `AttachRealtimeAsync`.

## Current limitation

- Unity may need one project refresh/reimport so the generated local `.csproj` picks up the newly added KCP package and source file.
