# ComposeSharp

[![CI](https://github.com/GaTTGeng/ComposeSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/GaTTGeng/ComposeSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ComposeSharp.Engine.svg)](https://www.nuget.org/packages/ComposeSharp.Engine)
[![License](https://img.shields.io/github/license/GaTTGeng/ComposeSharp.svg)](LICENSE)

[English](README.md)

ComposeSharp 不是把 `docker compose` 包一层进程调用。它试图解决另一个问题：当你的 .NET 程序需要把一组容器当作“自己管理的项目”时，怎样直接读取 Compose 文件、调用 Docker Engine，并能稳定地找回、观察和清理这组资源。

它会给容器、网络和卷加上 Compose 项目标签。也就是说，`orders-dev` 和 `orders-test` 即使共用一个 Docker daemon，`PsAsync`、`DownAsync`、`ScaleAsync` 看到和处理的仍是各自的资源范围。

## 适合什么场景

- 开发工具在本地拉起一套依赖服务。
- 集成测试根据 Compose 文件准备和回收环境。
- 内部控制面或桌面程序直接管理 Docker 项目。
- 需要读取 Compose YAML，但不希望把 YAML 字典散落在业务代码中。

如果你的首要目标是与 Docker Compose CLI 完全一致，尤其依赖 BuildKit、`watch` 同步、复杂多文件合并或所有 Compose Specification 细节，请暂时直接使用 Docker Compose CLI。ComposeSharp 把目前尚未完成的部分明确写在下文和 [路线图](docs/roadmap.md) 中。

## 从文件到 Docker 资源

```text
compose.yml / .env / env_file
              ↓
      ComposeSharp.Loader
      强类型服务、端口、卷、网络与部署提示
              ↓
      ComposeSharp.Engine
      Docker 容器、网络、卷与项目标签
```

Loader 会寻找 `docker-compose.yml`、`compose.yml`、`compose.yaml` 或 `docker-compose.yaml`，读取 `.env` 和服务的 `env_file`，再展开 YAML 中的变量。解析结果包含镜像、build、命令、环境、端口、卷、网络、健康检查、重启策略、profiles、labels、logging、capabilities、资源限制以及部分 `deploy` 字段。

### 变量插值

展开 YAML 时，ComposeSharp 优先使用进程环境变量，其次使用项目的 `.env`。服务的 `env_file` 只会写入该容器的环境，不作为插值来源。未设置的变量会展开为空；可使用 `-`/`:-` 指定默认值，或使用 `?`/`:?` 强制要求变量存在。`$$` 会生成字面量 `$`；必需变量缺失时，错误会同时指出变量名和 Compose 文件。

## 快速体验

```powershell
dotnet add package ComposeSharp.Engine
```

```csharp
using ComposeSharp.Api;
using ComposeSharp.Engine;

var compose = new ComposeService();
var project = new ComposeProjectContext
{
    ProjectName = "orders-dev",
    WorkingDirectory = @"C:\src\orders"
};

var config = compose.LoadProject(project);
Console.WriteLine(string.Join(", ", config.Services));

await compose.UpAsync(project, new ComposeUpOptions
{
    Pull = "always",
    Scale = new Dictionary<string, int> { ["api"] = 2 }
});

foreach (var container in await compose.PsAsync(project))
    Console.WriteLine($"{container.Service}: {container.State}");
```

在 ASP.NET Core 或其他使用 Microsoft DI 的程序中，可安装 `ComposeSharp.DependencyInjection` 并调用：

```csharp
builder.Services.AddComposeSharp();
```

`ComposeProjectContext` 不会被隐藏到全局配置里。项目名、工作目录、镜像仓库凭据和 Docker socket 都由调用方明确传入，这对同一进程管理多个项目很重要。

## 当前能力与边界

| 能力 | 当前实现 |
| --- | --- |
| 项目生命周期 | 创建项目网络，创建/启动/删除带标签的容器，列举项目容器，按需删除网络和卷，并通过 Docker Engine 构建已选择的服务。 |
| 服务控制 | start、stop、restart、pause、unpause、kill、remove、run、exec、attach、pull、push、scale、wait 和端口查询。 |
| 观察 | 容器、镜像、卷、日志、项目列表，以及 `VizAsync` 输出的 DOT 依赖图。 |
| 事件与文件变化 | `EventsAsync` 每两秒轮询容器状态；`WatchAsync` 仅在 build context 变动时发出 `rebuild` 通知。 |

请特别留意以下事实：

- `BuildAsync` 会将本地 build context 打包为 tar 并发送给 Docker Engine API。它会应用 `.dockerignore` 或所选 Dockerfile 对应的 `.dockerignore`、保留符号链接，并将归档写入临时文件而非进程内存；它支持 Dockerfile、tags、target、build args、labels、`cache_from`、network mode、extra hosts、共享内存与内存限制、单个平台、pull 和 no-cache；`ComposeBuildOptions.LogConsumer` 会接收 Docker 构建状态，构建流中的错误会使操作失败。BuildKit 专用的 `cache_to`、多平台、`privileged`、`builder` 与 progress mode 选择尚未应用。
- `CopyAsync`、`ExportAsync`、`CommitAsync` 当前仍调用 `docker` 可执行文件；核心生命周期则通过 Docker.DotNet。
- `TopAsync` 当前返回空列表。
- `GenerateAsync` 返回读取到的项目摘要，并不会生成新的 Compose 文件。
- `PublishAsync` 只为服务镜像打 tag，不会把镜像推送到 registry。
- `LoadMerged` 按字段逐步合并后置文件：标量由后置值覆盖，映射递归合并，列表追加；服务资源、`command` 和 `entrypoint` 有明确的替换规则。它仍不是 Docker Compose 的完整合并算法；精确规则和不支持的 YAML 标签见[合并语义](docs/merge-semantics.md)。
- `ComposeProjectContext.Profiles` 会在加载项目以及加载 Compose 文件的操作中统一选择服务：未配置 `profiles` 的服务始终会被选择；配置了 profile 的服务会在任一 profile 被激活时被选择。显式指定服务的操作即使未激活该服务的 profile，也可以选择它。
- 加载器可以保留引擎尚未应用的字段。在运行时依赖某个 Compose 属性之前，请查阅 [Compose 字段支持矩阵](docs/compose-field-matrix.md)。
- `depends_on` 已被读取并能体现在依赖图中，但尚未实现完整的启动排序和健康就绪调度。

默认端点在 Windows 是 `npipe://./pipe/docker_engine`，Unix 是 `unix:///var/run/docker.sock`；也可以通过 `SocketPath` 显式指定。

## 包的边界

| 包 | 何时使用 |
| --- | --- |
| [`ComposeSharp.Api`](https://www.nuget.org/packages/ComposeSharp.Api) | 只需要公共契约、选项、结果和回调。 |
| [`ComposeSharp.Loader`](https://www.nuget.org/packages/ComposeSharp.Loader) | 只需要读取 Compose 文件，不访问 Docker。 |
| [`ComposeSharp.Engine`](https://www.nuget.org/packages/ComposeSharp.Engine) | 需要 `IComposeService` 的 Docker Engine 实现。 |
| [`ComposeSharp.DependencyInjection`](https://www.nuget.org/packages/ComposeSharp.DependencyInjection) | 需要 `AddComposeSharp()` 注册扩展。 |

所有包面向 .NET 8、.NET 9 和 .NET 10。Docker Desktop/daemon 是否可用、访问权限、远程 TLS 与 registry 登录状态仍由运行环境负责。

## 路线图

1. **2.1：Compose 模型正确性** — 插值与合并语义、profiles、服务字段映射、校验错误和测试夹具。
2. **2.2：Docker Engine 覆盖度** — 替换进程式 build/copy/export/commit，实现真实 top、Docker 事件流，以及有意义的 generate/publish 行为。
3. **3.0：可靠编排** — 依赖与健康就绪、保守的 reconcile 策略、诊断信息，以及 Windows/Linux Docker 集成测试。

每个阶段的验收标准与明确不做的事项见 [docs/roadmap.md](docs/roadmap.md)；[Compose 字段支持矩阵](docs/compose-field-matrix.md)记录了解析与实际应用的行为；实现任务见 [GitHub Milestones](https://github.com/GaTTGeng/ComposeSharp/milestones)。欢迎用最小 Compose 文件提交 [兼容性问题](https://github.com/GaTTGeng/ComposeSharp/issues/new?template=compose_compatibility_gap.yml)。

## 构建与参与

```powershell
dotnet restore ComposeSharp.sln
dotnet build ComposeSharp.sln --configuration Release --no-restore
dotnet test ComposeSharp.sln --configuration Release --no-build --no-restore
dotnet pack ComposeSharp.sln --configuration Release --no-build --output artifacts/packages
```

- [贡献指南](CONTRIBUTING.md)
- [支持与讨论](SUPPORT.md)
- [安全政策](SECURITY.md)
- [变更日志](CHANGELOG.md)

ComposeSharp 使用 [MIT License](LICENSE)。
