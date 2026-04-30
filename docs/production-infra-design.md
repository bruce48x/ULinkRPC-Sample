# Production Infra Design

## Context

当前项目已经有：

- Unity 客户端
- `Server/Server` 作为 RPC 网关
- `Server/Silo` 作为 Orleans Silo
- Orleans grain 状态已经建模完成，但仍然使用内存存储

当前项目还没有：

- 外部数据库
- 外部缓存
- 面向多节点部署的集群成员发现与持久化

这会带来两个直接问题：

1. `Silo` 进程重启后，用户、房间、会话、匹配状态全部丢失。
2. `UseLocalhostClustering + AddMemoryGrainStorage` 无法支撑真正的分布式部署。

## Goal

第一阶段先把“必须持久化、并且适合立刻外置”的基础设施落下来：

- 使用 `docker compose` 启动 `PostgreSQL` 与 `Redis`
- 用 `PostgreSQL` 承载 Orleans 的：
  - 集群成员表
  - grain 持久化表
- 把 `Server/Server` 与 `Server/Silo` 的 Orleans 配置改成可外置

## Non-Goal

这一阶段不解决以下问题：

- 网关层 `SessionDirectory` 的跨节点共享
- `RoomRuntime` 的跨节点调度与迁移
- 实时 RPC 连接的路由转发
- 基于 Redis 的分布式锁 / pub-sub / presence

原因很直接：当前 `SessionDirectory` 保存的是进程内回调对象 `IPlayerCallback`，这类对象不能简单序列化进 Redis 后在另一台机器上恢复调用。分布式实时层必须单独设计“连接归属、路由、投递”模型，不能和 Orleans 持久化混成一步。

## Decision

### 1. PostgreSQL 作为第一持久化基座

使用 PostgreSQL 承担：

- Orleans ADO.NET clustering membership
- Orleans grain persistence

选择原因：

- 它同时覆盖“持久化”和“集群发现”两类核心需求
- Orleans 官方直接支持 ADO.NET clustering / persistence
- 对当前项目改造面最小，收益最大

### 2. Redis 先进入 compose，但暂不接入关键业务路径

Redis 会一并启动，原因是它大概率会在后续阶段承担：

- 分布式会话索引
- 在线状态缓存
- 路由辅助信息
- pub/sub 或轻量事件桥接

但本阶段不把它硬塞进现有逻辑，避免制造“看起来用了 Redis，实际上没有解决分布式语义”的伪完成状态。

### 3. Orleans 配置统一外置

把以下配置项改为从配置文件 / 环境变量读取：

- `ClusterId`
- `ServiceId`
- PostgreSQL connection string
- ADO.NET provider invariant
- Silo / Gateway 端口

这样后续切到容器部署、测试环境、生产环境时，不需要再改代码。

## Deployment Boundary After This Change

这一阶段完成后，系统能力会变成：

- `Silo` 可以多实例共享同一套 Orleans membership 与持久化存储
- grain 状态在进程重启后不会丢失
- `Server/Server` 作为 Orleans client，可以通过数据库驱动的集群信息连到 Silo

但仍然保留一个重要约束：

- RPC 网关与房间实时模拟仍然是“单节点本地内存语义”

这意味着：

- 登录连接和实时连接必须命中同一个网关实例，当前系统才是正确的
- 若直接横向扩容 `Server/Server`，还需要引入额外的网关路由/会话归属方案

## Phase-2 Preview

下一阶段如果要继续往真正分布式走，建议优先做：

1. 把匹配队列从 `GatewayMatchmakingService` 本地内存迁到 Orleans grain
2. 把 `PlayerSession` 变成“持久状态 + 网关归属信息”双层模型
3. 设计网关节点标识、玩家归属、跨网关消息投递
4. 再考虑 Redis 在 presence / routing / pub-sub 中的职责
