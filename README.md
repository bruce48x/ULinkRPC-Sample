# ULinkRPC Sample 31

这个项目用于验证 [ULinkRPC](https://github.com/bruce48x/ulinkrpc) 在一个轻量多人对战游戏里的接入方式，同时支持：

- 真正离线的本地单机
- 基于 RPC 的联机同步

## 产品规划

- [Agar 风格玩法设计](./docs/GAMEPLAY_DESIGN.md)
- [Agar 风格开发计划](./docs/DEVELOPMENT_PLAN.md)

## 开发流程约定

这个仓库后续新增需求默认遵循：

1. 先补设计文档
2. 再补开发计划文档
3. 最后再进入实现

小改动可以写短文档，但不跳过这一步。

## 1. 玩法

### 目标

玩家在一个方形平台上进行轻量 `agar.io` 风格对局：

- 吃地图上的食物球持续成长
- 更大的球可以吞掉足够小的玩家
- 被吞掉后会在倒计时结束后复活
- 单局最大时长为 `120s`
- 本局结束时，体型最大者获胜
- 本局结束后返回主界面

### 基础动作

- `W/A/S/D` 移动

### 成长规则

- 体型越大，移动速度越慢
- 食物球是稳定刷新的基础成长来源
- 吞噬玩家能获得更高质量收益

### AI

AI 的目标是让对局总人数维持在 `4`：

- 联机模式：真人玩家加入后，服务端自动补足 AI
- 单机模式：本地直接创建 1 个玩家 + 若干 AI，凑满 4 人

## 2. 模式

启动游戏后先显示两个入口：

- `单机`
  不连接服务器。客户端本地运行完整玩法模拟，适合断网或只想和 AI 对战的场景。
- `联机`
  弹出账号密码面板，点击 `匹配` 后才发起 RPC 连接和登录。

## 3. 架构

### Shared

`Shared/Gameplay/ArenaSimulation.cs`

这是整个项目的玩法内核，负责统一实现：

- 玩家移动与体型速度衰减
- 食物刷新与成长结算
- 大球吞小球判定
- 缩圈与最大时长结算
- AI 的觅食 / 追弱 / 避强决策
- 吞噬、复活、胜负判定
- 世界状态快照生成

设计原则：

- 玩法规则只写一份
- 服务端联机和客户端单机共用同一套模拟逻辑
- Shared 只关心规则和状态，不关心网络、UI、存档

### Server

`Server/Server/Services/GameArenaRuntime.cs`

服务端现在是一个很薄的接入层，主要负责：

- 登录后的玩家注册/注销
- 调用 `ArenaSimulation` 推进联机对局
- 广播 `WorldState / PlayerDead / MatchEnd`
- 持久化真人玩家胜场

本地开发时，Orleans silo 默认在 `Server/Silo/appsettings.json` 中把 `Orleans:AdvertisedIPAddress`
固定为 `127.0.0.1`。如果去掉这个配置，Windows/WSL/Docker 多网卡环境可能会把 `172.*`
虚拟网卡地址写入 ADO.NET membership，导致同一台机器上的 client 或新 silo 连接旧地址失败。
遇到这种情况时，停止 `Server/Silo` 和 `Server/Server` 后清理当前开发集群的 membership 记录：

```sql
DELETE FROM OrleansMembershipTable WHERE DeploymentId = 'dev';
DELETE FROM OrleansMembershipVersionTable WHERE DeploymentId = 'dev';
```

### Client

`Client/Assets/Scripts/Gameplay/DotArenaGame.cs`

客户端负责：

- 启动菜单与模式切换
- 单机模式下驱动本地 `ArenaSimulation`
- 联机模式下发送输入、接收世界快照
- 玩家、食物球、HUD 与 UI 表现

## 4. 同步边界

联机模式遵循“客户端发输入，服务端发状态”：

### 客户端发送

```txt
InputMessage
{
    playerId
    moveX
    moveY
    dash
    tick
}
```

其中 `dash` 字段目前保留在协议中，但 `agar` 玩法阶段不再使用。

### 服务端广播

```txt
WorldState
{
    tick
    respawnDelaySeconds
    players[]
    pickups[]
}
```

其中：

- `players[]` 包含位置、速度、生死状态、积分、质量、半径
- `pickups[]` 描述当前地图上还存在的食物球

客户端渲染时对玩家位置做插值，避免快照跳动。

## 5. 视觉实现

### 玩家小球

- 玩家球体使用自定义 shader 做果冻形变
- 当玩家碰撞后进入 `Stunned` 状态，会触发一次短促的弹性反馈

### 食物小球

- 食物球沿用小球 + 标签表现
- 名字显示在球体中心
- 文字颜色会根据球体明暗自动切换黑/白

## 6. 当前代码组织

```txt
Shared
 ├ Gameplay
 │  ├ ArenaConfig.cs
 │  └ ArenaSimulation.cs
 └ Interfaces
    └ IPlayerService.cs

Server
 └ Server
    └ Services
       └ GameArenaRuntime.cs

Client
 └ Assets
    └ Scripts
       └ Gameplay
          └ DotArenaGame.cs
```

## 7. 当前实现状态

已完成：

- 单机与联机双模式入口
- 共享玩法内核抽离到 `Shared`
- 联机/单机共用同一套玩法规则
- `agar` 风格成长、吞噬、AI 补位、复活、胜负判定
- 玩家碰撞果冻效果

待继续验证：

- Unity 编辑器内的完整单机流程回归
- 联机模式下 UI 交互与视觉细节的最终打磨
