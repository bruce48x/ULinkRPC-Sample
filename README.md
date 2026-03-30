这个项目是为了对 [ULinkRPC](https://github.com/bruce48x/ulinkrpc)进行测试
# 一、核心玩法（Core Gameplay）
## 游戏目标

玩家在一个平台上互相推挤：

- 被推下平台 → 淘汰
- 最后存活玩家 → 胜利

##  玩家能力

玩家只有 三个动作：

- **移动**	WASD / 摇杆
- **冲刺**	短时间高速撞击
- **推挤**	接触时产生力

## 游戏流程
```txt
Lobby
 ↓
Match Start
 ↓
Battle Phase
 ↓
Player Eliminated
 ↓
Last Player Wins
 ↓
Restart
```

每局时间：30 ~ 90 秒
# 二、地图结构

地图非常简单：
```txt
+--------------------+
|                    |
|        arena       |
|                    |
|                    |
|                    |
+--------------------+
```

核心规则：

- 边缘是 死亡区域
- 掉出边界 → 淘汰

## 地图元素（可选）

可以逐步增加：

- 移动平台	测同步
- 旋转锤	测碰撞
- 弹跳板	欢乐效果
- 随机掉落地板	增加紧张感
- 随机 buff 球	提升对局变数

## 新增 buff 玩法

地图上会随机刷新两种不同的小球，玩家触碰后立刻获得对应增益：

- **加速**：移动速度提升 `50%`，持续 `10 秒`
- **冲击力**：把其他玩家撞飞的距离提升 `50%`，持续 `5 秒`

规则说明：

- 两种球会在场地内随机位置刷新
- 同类球被吃掉后会在短暂冷却后再次随机刷新
- 玩家死亡或重生时，当前 buff 会被清空
- buff 效果可叠加存在，例如同时拥有加速与重击

# 三、核心系统结构

系统可以拆为 6 个模块。
## 1 玩家系统（Player）

玩家实体包含：
```txt
Player
 ├ id
 ├ position
 ├ velocity
 ├ state
 ├ alive
 ├ speedBuffRemaining
 ├ knockbackBuffRemaining
```

状态机：
```txt
Idle
Move
Dash
Stunned
Dead
```
玩家参数
```txt
speed = 6
dashSpeed = 12
dashTime = 0.3s
pushForce = 10
speedBuff = +50% / 10s
knockbackBuff = +50% / 5s
```

## 2 输入系统（Input）

客户端每帧发送：
```txt
PlayerInput
{
    moveX
    moveY
    dash
}
```
频率：20Hz
## 输入流程
```txt
Client Input
    ↓
Send RPC
    ↓
Server Apply Input
    ↓
Physics Simulation
    ↓
Broadcast State
```
## 3 物理系统（Physics）

玩家之间发生：碰撞 → 推挤
简单实现：`Force = direction * pushForce`
### 冲刺机制
Dash：`velocity = direction * dashSpeed`
冲刺时：
- 推力更大
- 碰撞效果更明显

### Buff 球机制
服务器维护两个随机刷新的 pickup：

```txt
SpeedBoostPickup
KnockbackBoostPickup
```

玩家触碰 pickup 后：

- 服务器立即应用 buff
- pickup 消失并进入随机重生冷却
- 新状态跟随 WorldState 一并广播
## 4 淘汰系统（Elimination）
服务器检查：
```txt
if player.position outside arena
    player.dead = true
```
触发：
```txt
PlayerEliminated event
```
### 淘汰广播
服务器广播：
```txt
PlayerDead
{
    playerId
}
```
客户端播放：
- 掉落动画
- UI提示

## 5 胜利判定
服务器维护：`alivePlayers`
每次淘汰：`alivePlayers--`
当：`alivePlayers == 1`
触发：`MatchEnd`
## 6 同步系统（最关键）

这是测试 ulinkrpc 的核心。
### 客户端发送

只发送 输入：
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
### 服务端模拟

服务器：
```txt
ApplyInput
SimulatePhysics
UpdatePosition
```
### 状态广播

服务器广播：
```txt
WorldState
{
    tick
    players[]
    pickups[]
}
```
玩家结构：
```txt
PlayerState
{
    id
    x
    y
    vx
    vy
    speedBuffRemainingSeconds
    knockbackBuffRemainingSeconds
}
```
地图道具结构：
```txt
PickupState
{
    type
    x
    y
}
```
频率：`10~20 Hz`
# 四、客户端渲染
客户端不直接使用服务器坐标。

使用：`interpolation`
## 插值算法
```txt
renderPosition =
lerp(prevState, nextState)
```
优点：
- 平滑
- 抗网络抖动
