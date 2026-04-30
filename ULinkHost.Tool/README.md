# ULinkHost.Tool

`ULinkHost.Tool` 是 `ULinkHost` 的项目管理工具，而不是宿主运行时库。

它的职责是：

- 初始化项目管理配置
- 统一执行项目级维护命令
- 作为后续 `ULinkHost` 工具链的命令入口

当前已提供：

- `new`
- `codegen`

## new

命令参数与 `ulinkrpc-starter new` 对齐：

```bash
ulinkhost-tool new --name MyGame --output ./out --client-engine unity --transport kcp --serializer memorypack --nugetforunity-source embedded
```

该命令会先调用 `ulinkrpc-starter new` 生成原始 ULinkRPC 项目骨架，然后在其基础上补充：

- `ULinkHost/`
- `Server/Silo/`
- 基于 `ULinkHost` 的 gateway 启动代码
- Orleans 配置文件
- `ulinkhost.tool.json`

最终会在输出目录下生成：

- `ulinkhost.tool.json`

默认行为：

```bash
ulinkhost-tool new
```

默认输出目录是 `./out`。

前提：

- `ulinkrpc-starter` 需要已安装并可被命令行找到

## codegen

根据 `ulinkhost.tool.json` 所在项目根目录，委托 `ulinkrpc-starter codegen` 重新生成 RPC 代码：

```bash
ulinkhost-tool codegen
```

可选参数：

```bash
ulinkhost-tool codegen --config path/to/ulinkhost.tool.json
ulinkhost-tool codegen --no-restore
```

## Config Example

```json
{
  "project": {
    "name": "MyGame",
    "clientEngine": "unity",
    "transport": "kcp",
    "serializer": "memorypack",
    "nuGetForUnitySource": "embedded"
  },
  "codegen": {
    "contractsPath": "Shared",
    "server": {
      "projectPath": "Server/Server",
      "outputPath": "Generated",
      "namespace": "Server.Generated"
    },
    "unityClient": {
      "projectPath": "Client",
      "outputPath": "Assets/Scripts/Rpc/Generated",
      "namespace": "Rpc.Generated"
    }
  }
}
```

## 定位

`ULinkHost.Tool` 不应承载运行时宿主逻辑。

运行时能力属于：

- `ULinkHost`

项目工具能力属于：

- `ULinkHost.Tool`

## 依赖关系

`ULinkHost.Tool` 对外只依赖：

- `ulinkrpc-starter`

它不会直接调用 `ulinkrpc-codegen`。
