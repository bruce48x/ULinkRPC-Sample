# Distributed Gateway Refactor

## Goal

在已经接入 PostgreSQL 持久化后，继续削弱 `Server/Server` 单机内存对核心业务状态的垄断。

这一轮重点处理三个问题：

1. 匹配队列仍在网关本地内存里，无法多实例。
2. 玩家“应该连到哪个实时网关”没有显式建模。
3. `AttachRealtime` 依赖本地 `SessionDirectory` 先有控制连接，跨网关无法成立。

## What Changes

### 1. Matchmaking moves to Orleans as the source of truth

- 网关不再维护本地 `_queue`
- `GatewayMatchmakingService` 改为调用 `IMatchmakingGrain`
- 保留后台 `MatchmakingHostedService`，但职责变成定时调用 grain `TickAsync`
- 队首等待超时后的补齐匹配逻辑，迁入 `MatchmakingGrain`

### 2. Gateway endpoint becomes durable session metadata

给 `PlayerSession`、`RoomAssignment`、`RoomSnapshot` 增加显式网关端点描述：

- `InstanceId`
- `Transport`
- `Host`
- `Port`
- `Path`

这样匹配完成后，客户端拿到的实时接入目标来自持久化房间/会话信息，而不是当前回调网关临时拼出来的本地地址。

### 3. Realtime attach no longer requires local control-session ownership

`SessionDirectory` 现在允许“只有 realtime 回调，没有 control 回调”的本地注册项。

这使得以下链路成立：

- 控制 RPC 连接留在网关 A
- 房间运行时归属网关 B
- 客户端根据匹配结果附着到网关 B
- 网关 B 为该玩家建立 realtime-only 本地注册

## Current Boundary After This Refactor

已经改善：

- 匹配队列是分布式可共享的
- 房间运行时归属网关被显式建模
- 客户端实时附着目标不再依赖当前控制网关的本地配置

仍未完成：

- 非房间归属网关上的控制连接，不能直接把输入转发到房间归属网关
- 世界状态广播仍然只覆盖本地 runtime 所持有的 callback
- 玩家登出、断线、房间离开还缺少完整的跨网关一致性收尾

## Next Step

下一轮应该优先做输入与事件的跨网关路由层，候选方案有两个：

1. 基于 Orleans stream / observer 做房间输入与广播桥接
2. 基于 Redis pub/sub 做网关间实时事件总线

在这一步完成前，系统已经从“单机内存匹配”前进到“分布式匹配 + 显式房间归属”，但还不是真正完成态的多网关实时架构。
