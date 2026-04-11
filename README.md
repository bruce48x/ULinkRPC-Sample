# ULinkRPC Sample 31

这个项目用于验证 [ULinkRPC](https://github.com/bruce48x/ulinkrpc) 在一个轻量多人对战游戏里的接入方式，同时支持：

- 真正离线的本地单机
- 基于 RPC 的联机同步

## 1. 玩法

### 目标

玩家在一个方形平台上互相碰撞、推挤和冲刺：

- 掉出边界会被淘汰
- 淘汰后会在倒计时结束后复活
- 场上只剩 1 名存活玩家时，本局结束
- 短暂延迟后自动重开下一局

### 基础动作

- `W/A/S/D` 移动
- `Space` 冲刺

### Buff

地图会随机刷新 buff 球。当前规则如下：

- `冲击力`：碰撞击退强度提升到基础值的 `300%`，持续 `5s`
- `加速`：移动速度提升到基础值的 `200%`，持续 `10s`

通用规则：

- buff 被触发后立即生效
- buff 在持续时间结束后自动消失
- 玩家死亡或重生时，当前 buff 会被清空

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

- 玩家移动、冲刺、眩晕
- 玩家碰撞与击退
- buff 刷新与拾取
- AI 决策
- 淘汰、复活、胜负判定
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
- 持久化真人玩家积分

### Client

`Client/Assets/Scripts/Gameplay/DotArenaGame.cs`

客户端负责：

- 启动菜单与模式切换
- 单机模式下驱动本地 `ArenaSimulation`
- 联机模式下发送输入、接收世界快照
- 玩家、buff、小球视觉表现和 UI

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

- `players[]` 包含位置、速度、生死状态、积分、buff 剩余时间
- `pickups[]` 描述当前地图上还存在的 buff 球

客户端渲染时对玩家位置做插值，避免快照跳动。

## 5. 视觉实现

### 玩家小球

- 玩家球体使用自定义 shader 做果冻形变
- 当玩家碰撞后进入 `Stunned` 状态，会触发一次短促的弹性反馈

### Buff 小球

- buff 球带名称标签
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
- `冲击力` / `加速` buff、AI 补位、复活、胜负判定
- 玩家碰撞果冻效果

待继续验证：

- Unity 编辑器内的完整单机流程回归
- 联机模式下 UI 交互与视觉细节的最终打磨
