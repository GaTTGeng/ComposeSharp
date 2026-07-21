# ComposeSharp

[![CI](https://github.com/GaTTGeng/ComposeSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/GaTTGeng/ComposeSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ComposeSharp.Engine.svg)](https://www.nuget.org/packages/ComposeSharp.Engine)
[![License](https://img.shields.io/github/license/GaTTGeng/ComposeSharp.svg)](LICENSE)

[English](README.md)

ComposeSharp 是一个托管式 .NET Docker Compose SDK。它加载 Compose 文件，并主要通过 `Docker.DotNet` 提供项目、服务、容器、镜像、网络、卷、日志、事件和生命周期操作，不依赖调用 `docker compose` 命令行。

## 软件包

| 软件包 | 用途 |
| --- | --- |
| [`ComposeSharp.Api`](https://www.nuget.org/packages/ComposeSharp.Api) | 公共服务契约、选项、结果、回调和项目上下文。 |
| [`ComposeSharp.Loader`](https://www.nuget.org/packages/ComposeSharp.Loader) | Compose YAML 加载、变量插值、文件合并和强类型模型。 |
| [`ComposeSharp.Engine`](https://www.nuget.org/packages/ComposeSharp.Engine) | 基于 Docker.DotNet 的 `IComposeService` 实现。 |
| [`ComposeSharp.DependencyInjection`](https://www.nuget.org/packages/ComposeSharp.DependencyInjection) | ASP.NET Core 和托管应用的 `IServiceCollection` 注册扩展。 |

一般应用请安装 `ComposeSharp.Engine`；使用依赖注入时安装 `ComposeSharp.DependencyInjection`，它会传递引用引擎包。

## 安装与快速开始

```powershell
dotnet add package ComposeSharp.Engine
```

```csharp
using ComposeSharp.Api;
using ComposeSharp.Engine;

var compose = new ComposeService();
var project = new ComposeProjectContext
{
    ProjectName = "sample",
    WorkingDirectory = @"C:\src\sample"
};

var config = compose.LoadProject(project);
var containers = await compose.PsAsync(project);
```

SDK 支持 .NET 8、.NET 9 和 .NET 10，需要能访问 Docker Engine。Docker 引擎权限、镜像凭据以及平台相关配置由 Docker 守护进程负责。

## 当前范围

- Compose 文件加载、环境变量插值与多文件合并。
- 构建、创建、启动、停止、重启、暂停、删除、拉取、推送和终止等生命周期操作。
- 执行命令、附加、复制、日志、事件、状态、进程、镜像、端口、扩缩容、等待、导出、提交和卷操作。

ComposeSharp 是托管 SDK，不是 Docker Compose CLI 的逐字节替代品。请针对 BuildKit、watch 同步、镜像仓库认证和 socket 配置等 Docker 相关行为在目标环境中验证。

目前大多数操作使用 Docker.DotNet；构建和复制的当前实现仍会调用 `docker` 可执行文件，后续会逐步替换为托管 Docker API 实现。

## 构建、测试与打包

```powershell
dotnet restore ComposeSharp.sln
dotnet build ComposeSharp.sln --configuration Release --no-restore
dotnet test ComposeSharp.sln --configuration Release --no-build --no-restore
dotnet pack ComposeSharp.sln --configuration Release --no-build --output artifacts/packages
```

包元数据位于 `src/Directory.Build.props`。发布与 NuGet Trusted Publishing 配置见 [维护者发布指南](docs/maintainer-release.md)。

## 参与贡献、支持与安全

- [贡献指南](CONTRIBUTING.md)
- [支持政策](SUPPORT.md)
- [安全政策](SECURITY.md)

## 许可证

ComposeSharp 使用 [MIT License](LICENSE)。
