# ULinkHost

`ULinkHost` 是一个面向在线游戏服务端的薄宿主框架。

它的目标不是提供一整套厚重的游戏业务框架，也不是替具体项目定义玩法模型，而是提供一层稳定、克制、可复用的服务运行底座。

## 定位

`ULinkHost` 解决的是：

- 服务如何启动
- 模块如何装配
- RPC 请求如何进入系统
- Orleans 集群如何接入
- 连接和运行时如何承载业务模块
- RPC 服务如何接入宿主 DI
- 配置、宿主、传输这些通用能力如何复用

`ULinkHost` 不解决的是：

- 具体游戏玩法
- 房间制对战模型
- MMORPG 世界模型
- 通用匹配系统
- 通用战斗系统
- 通用排行榜业务规则

一句话概括：

`ULinkHost` 是“在线游戏服务宿主”，不是“游戏业务大全框架”。

## 设计哲学

### 1. 保持薄

通信由 `ULinkRPC` 负责，分布式 actor 与集群状态由 `Microsoft Orleans` 负责。

因此 `ULinkHost` 不应重复发明：

- 自定义 RPC 框架
- 自定义 actor 框架
- 自定义分布式运行时
- 自定义全家桶业务范式

`ULinkHost` 只应补足它们之间的服务端宿主接缝。

### 2. Core 只放机制，不放重业务语义

框架核心只能沉淀“多数联网游戏都稳定存在”的能力。

允许进入 Core 的能力类型：

- 宿主与启动
- 模块注册
- 请求上下文
- Orleans client/silo 装配
- 传输接入约定
- RPC 服务 DI 接入
- 配置读取
- 基础运维接缝

不允许直接进入 Core 的能力类型：

- `Room`
- `Matchmaking`
- `BattleInstance`
- `Guild`
- `Mail`
- `AOI`
- `World`
- `Leaderboard`

这些都属于可选 feature 或具体 game，不属于宿主内核。

### 3. 先判断“是不是所有联网游戏都需要”，再决定是否进内核

任何新能力进入 `ULinkHost` Core 前，必须先通过这两个判断：

1. 没有这个能力，大多数联网游戏服务还能不能成立？
2. 这个能力在 MMORPG、SLG、MOBA、射击、房间制对战之间，抽象形状是否仍然稳定？

只有两个问题都能回答“是”，才有资格考虑进入 Core。

如果答案不稳定，就应放到：

- `Features.*`
- `Samples.*`
- 具体业务项目

而不是放进 `ULinkHost`。

### 4. 框架定义运行与集成方式，不定义业务组织方式

`ULinkHost` 不强制规定：

- 游戏必须怎样建模
- 业务必须怎样分层
- 战斗逻辑必须怎样运行
- 世界服必须怎样组织

`ULinkHost` 只定义：

- 服务如何被托管
- 模块如何接入
- RPC 服务如何通过宿主 DI 获取依赖
- Orleans 如何统一接线
- 控制面与实时面如何挂接

## 当前 Core 边界

当前 `ULinkHost` 中，属于 Core 的内容包括：

- `Hosting/`
  - 宿主启动扩展
  - Orleans client/silo 装配
  - 控制面 / 实时面 RPC hosted service
  - RPC server 配置上下文
- `Transport/`
  - 控制面配置
  - 实时面配置

这些能力都是宿主层机制，不带具体游戏业务语义。

## 非目标

`ULinkHost` 明确不追求以下目标：

- 不追求成为类似 ET 的一体化厚框架
- 不追求统一客户端与服务端所有开发范式
- 不追求在 Core 中预置所有常见游戏业务模块
- 不追求抽象一套“所有游戏通用”的玩法系统

如果未来需要房间、匹配、世界服、公会、排行榜等能力，应采用：

- 独立 `Features.*` 模块
- 示例项目 `Samples.*`
- 或者具体业务项目内实现

## 后续开发准绳

后续对 `ULinkHost` 的任何新增代码，都应遵守以下规则：

1. 优先扩展宿主能力，而不是扩展业务语义。
2. 优先做可插拔机制，而不是做默认业务模型。
3. 发现某段代码带有明确业务名词时，先假设它不属于 Core。
4. 如果一项能力更适合作为 feature，就不要因为“常见”而塞进宿主框架。
5. 宁可让 Core 少一点，也不要让边界变脏。

## 推荐演进方向

未来 `ULinkHost` 可以继续向这些方向演进：

- 更明确的模块注册接口
- 更稳定的请求上下文抽象
- 更清晰的 observability 接缝
- 更规范的 session / identity Core 抽象
- 更清晰的 feature 装配边界
- 对任意 `ULinkRPC.Transport` 与任意 `ULinkRPC.Serializer` 的更自然装配体验

但这些演进仍必须遵守上面的边界约束。

## 关于 Transport 与 Serializer

`ULinkHost` Core 必须能够灵活支持任意 `ULinkRPC.Transport` 与任意 `ULinkRPC.Serializer`。

因此：

- `ULinkHost` 不应内置或硬编码特定 serializer
- `ULinkHost` 不应内置或硬编码特定 transport
- `ULinkHost` 只负责托管 RPC server 生命周期
- 具体项目负责注册控制面与实时面的 RPC builder 配置

当前做法是通过以下接口把具体实现留给上层项目：

- `IControlPlaneRpcServerConfigurator`
- `IRealtimeRpcServerConfigurator`

配置接口接收 `ULinkHostRpcServerContext`，业务项目可以通过其中的 `Builder`
配置 serializer / transport / service binder，也可以通过 `Services` 接入宿主
DI 容器创建 RPC service。

这样 `ULinkHost` 保持宿主层中立，而业务项目可以自由选择：

- `MemoryPack`
- 未来其它 serializer
- `WebSocket`
- `Kcp`
- 未来其它 transport
