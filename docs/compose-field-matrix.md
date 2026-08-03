# Compose field support matrix

ComposeSharp is an in-process SDK, not a Docker Compose CLI replacement. The loader deliberately retains more Compose-shaped data than the engine currently sends to Docker. This matrix records that distinction for the fields exposed by `ServiceDefinition` and `ComposeProject`.

## Status definitions

| Status | Meaning |
| --- | --- |
| Applied | The loader reads the field and the engine uses it when it creates or operates on resources. |
| Partial | The loader reads the field and the engine uses a documented subset of its value or semantics. |
| Parsed only | The loader exposes the field, but the engine does not currently apply it. |
| Unsupported | The field is exposed for inspection but has no operational behavior. |
| Planned | A related operational change has a tracked issue. It does not imply current support. |

"Applied" describes the SDK's implementation, not full Docker Compose Specification parity. Explicit operation options can override an applied service value where the API allows it.

## Service fields

| Compose field(s) | Model member(s) | Status | Current behavior |
| --- | --- | --- | --- |
| Service key | `Name` | Applied | Identifies the service in resource names and project labels. |
| `image` | `Image` | Applied | Used to create and pull container images. |
| `build` | `Build` | Parsed only / [planned](https://github.com/GaTTGeng/ComposeSharp/issues/14) | Build settings are parsed, but `BuildAsync` currently combines `Arguments` with `ArgumentList`; a non-null build context causes process startup to fail before Docker receives the settings. |
| `container_name` | `ContainerName` | Applied | Used for a single replica; scaled services retain project-generated names. |
| `command`, `entrypoint` | `Command`, `Entrypoint` | Partial | List syntax is passed to Docker's create-container request. Scalar values are passed as one argument rather than split into a command and arguments. |
| `environment`, `env_file` | `Environment`, `EnvFile` | Partial | Inline environment and values read from scalar or list-of-string `env_file` entries are passed to the container, and their original paths are retained for inspection. Long `env_file` syntax and advanced Compose environment semantics are not implemented. |
| `ports` | `Ports` | Partial | Short `HOST:CONTAINER` string syntax creates a port binding when each container port has one mapping. Container-only entries expose the port but do not create a host binding. Multiple mappings for one container port, long syntax, host IP, ranges, and other per-port options are not modeled. |
| `volumes` | `Volumes` | Partial | Short POSIX bind and named-volume strings are resolved and passed as binds. Long syntax, Windows drive-letter binds, and top-level volume driver/options are not applied. |
| `restart` | `Restart`, `RestartMaxRetries` | Partial | Docker restart policy name is applied. An `on-failure` retry count is parsed but not sent. |
| `healthcheck` | `Healthcheck` | Partial | Disable flag, list-form tests, supported interval/timeout/start-period values, and retries are sent in the create-container request. `start_interval` is not modeled. Durations accept .NET `TimeSpan` syntax or a single whole-number `ms`, `s`, `m`, or `h` unit; fractional and multi-component Compose durations are not accepted. A scalar `test` command is not converted to `CMD-SHELL`. |
| `depends_on` | `DependsOn` | Partial / [planned](https://github.com/GaTTGeng/ComposeSharp/issues/19) | The engine makes a best-effort ordering pass. It does not detect cycles or wait for dependency conditions or health readiness. A targeted lifecycle operation does not add unselected dependencies. |
| `networks` | `Networks` | Partial | The first declared service network becomes the container network mode. Per-network configuration and multi-network attachment are not applied. When a service declares no network and the project has custom networks, it attaches to the first custom network rather than an implicit default network. |
| `extra_hosts` | `ExtraHosts` | Partial | Short list entries are passed to Docker as host entries. Mapping syntax loses the host address during loading and is not supported. |
| `privileged` | `Privileged` | Applied | Passed in the host configuration. |
| `network_mode`, `ipc`, `shm_size` | `NetworkMode`, `Ipc`, `ShmSize` | Partial | Direct Docker modes and integer/K/M/G `shm_size` values are passed in the host configuration. Decimal byte values are not accepted. `network_mode: service:<name>` and `ipc: service:<name>` are not resolved to a service container. `network_mode` takes precedence over the generated project network. |
| `profiles` | `Profiles` | Applied | Service selection uses `ComposeProjectContext.Profiles`; unprofiled services remain selected, and explicitly requested services are selectable. |
| `deploy` | `Deploy` | Partial | Only `deploy.replicas` affects `UpAsync`; resource memory and literal `nano_cpus` values, placement constraints, restart policy, update/rollback settings, labels, and mode are parsed only. Duration fields use the same restricted duration syntax as health checks. Other placement settings, standard `deploy.resources.*.cpus` values, and scalar `endpoint_mode` are not retained. |
| `secrets`, `configs` | `Secrets`, `Configs` | Unsupported | Short string entries are parsed and surfaced, but no secret or config is provisioned or mounted. Long syntax is not retained correctly. |
| `labels` | `Labels` | Applied | Merged into the labels applied to service containers. |
| `logging` | `Logging` | Parsed only | Driver and options are exposed but not sent to Docker. |
| `hostname`, `domainname`, `user`, `working_dir` | `Hostname`, `Domainname`, `User`, `WorkingDir` | Applied | Passed to Docker's create-container request. |
| `tty`, `stdin_open` | `Tty`, `StdinOpen` | Applied | Passed to Docker's create-container request. |
| `stop_signal`, `stop_grace_period` | `StopSignal`, `StopGracePeriod` | Parsed only | Exposed by the loader when the duration uses the supported .NET `TimeSpan` or single whole-number unit syntax; the container create and stop paths do not apply them. |
| `read_only`, `tmpfs` | `ReadOnly`, `Tmpfs` | Applied | Passed in the host configuration. |
| `cap_add`, `cap_drop`, `security_opt` | `CapAdd`, `CapDrop`, `SecurityOpt` | Applied | Passed in the host configuration. |
| `devices` | `Devices` | Partial | Two-segment short mappings are passed in the host configuration. Permission-bearing mappings such as `/dev/sda:/dev/xvdc:rwm` are parsed incorrectly and are not supported. |
| `sysctls` | `Sysctls` | Parsed only | Exposed by the loader but not passed to Docker. |
| `init` | `Init` | Applied | Enables Docker init when the value is `true`. |
| `platform`, `pull_policy` | `Platform`, `PullPolicy` | Parsed only | Exposed by the loader; platform is not passed to create or build, and pull behavior is controlled by operation options. |
| `dns`, `dns_search` | `Dns`, `DnsSearch` | Parsed only | Exposed by the loader but not passed to Docker. |
| `pid`, `mac_address`, `cgroup_parent` | `Pid`, `MacAddress`, `CgroupParent` | Partial | Direct Docker PID modes, MAC address, and cgroup parent are passed to Docker. `pid: service:<name>` is not resolved to a service container. |
| `extends` | `ExtendsService`, `ExtendsFile` | Unsupported | The loader records the reference but does not resolve or merge it. A service that declares only `extends`, without its own `image` or `build`, is rejected during loading. |
| `develop` | `Develop` | Unsupported | Mapping-shaped values are not retained as watch configuration. `WatchAsync` observes build contexts only and does not interpret this field. |
| `links` | `Links` | Parsed only | Exposed by the loader but not passed to Docker. |
| `cpu_shares`, `cpuset` | `CpuShares`, `Cpuset` | Applied | Converted to Docker CPU shares and CPU set host settings. |
| `cpu_quota` | `CpuQuota` | Parsed only | Exposed by the loader but not passed to Docker. |
| `mem_limit`, `memswap_limit`, `mem_reservation` | `Memory`, `MemorySwap`, `MemoryReservation` | Partial | Integer byte values, K/M/G units, and `memswap_limit: -1` are converted and passed in the host configuration. Decimal and other Compose byte formats are not accepted. |
| `oom_kill_disable`, `oom_score_adj` | `OomKillDisable`, `OomScoreAdj` | Parsed only | Values are not passed to Docker. `oom_score_adj` is retained, while `oom_kill_disable` is retained only when `true`; an explicit `false` is indistinguishable from omission. |
| `group_add` | `GroupAdd` | Applied | Passed as supplemental groups in the host configuration. |
| `annotations` | `Annotations` | Parsed only | Exposed by the loader but not sent to Docker. |

## Top-level project fields

| Compose field(s) | Model member(s) | Status | Current behavior |
| --- | --- | --- | --- |
| Compose file directory | `WorkingDirectory` | Applied | Resolves Compose-relative paths and bind mounts. |
| `services` | `Services` | Applied | Provides service definitions used by project operations. |
| `volumes` | `Volumes` | Partial | Project-scoped volumes are created with a generated name and project label. Driver, labels, options, and external volumes are not represented; external volume names are rewritten to project-scoped names. |
| `networks` | `Networks` | Partial | Project-scoped bridge networks are created with generated names and project labels. Driver, IPAM, labels, external networks, and options are not represented. |
| `secrets`, `configs` | `Secrets`, `Configs` | Unsupported | Names are loaded and reported by `LoadProject`, but are not provisioned or mounted. |
| `x-*` extensions | `Extensions` | Parsed only | String-valued extensions are retained for inspection and have no engine behavior. |

## Related work

- [Issue #14](https://github.com/GaTTGeng/ComposeSharp/issues/14) will replace the process-backed build path with Docker Engine APIs.
- [Issue #19](https://github.com/GaTTGeng/ComposeSharp/issues/19) will make dependency ordering and readiness deliberate and testable.
- [Issue #23](https://github.com/GaTTGeng/ComposeSharp/issues/23) tracks a broader matrix of tested Compose constructs and environments.

For the loader's multi-file merge rules, see [merge semantics](merge-semantics.md). For planned work and non-goals, see the [roadmap](roadmap.md).
