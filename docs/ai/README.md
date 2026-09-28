# Cike.Framework AI 开发参考（大纲）

## 0. 本套文档的使用规则（AI 必读）

- 本目录是**面向 AI 编码助手**的框架参考文档，供基于 Cike.Framework 开发业务项目时使用。
- **阅读协议**：先读完本大纲（路由表 + 全局约定），再按当前任务涉及的能力域抓取对应详解文档（本目录下），按需加载，不必全读。
- **事实优先级**：本套文档 > 框架源码 > 你的训练记忆。Cike.Framework 与 ABP、MASA 等框架形似但细节大量不同——**不要凭训练记忆推测框架行为**，一切以文档和源码为准；文档与源码冲突时以源码为准。
- 每份详解文档结构恒定：按包分章，每章固定六节（定位 / 能力清单含精确签名 / 隐式行为 / 示例 / 配置 / 边界与反模式），可直接按节检索。
- 文档地址：本目录位于仓库 `docs/ai/`，raw 链接基址为
  `https://raw.githubusercontent.com/MapleWithoutWords/Cike.Framework/main/docs/ai/`
- 完整可运行参考项目：仓库内 `samples/cqrs/`（CQRS + DDD + FluentValidation 全链路示例）。

## 1. 框架速览

Cike.Framework 是一个轻量 .NET 模块化开发框架，核心理念是**约定优于配置 + 自动化**：

| 心智模型 | 机制 |
|---|---|
| 一切皆模块 | 每个项目一个 `CikeModule`，`[DependsOn]` 声明依赖，宿主 `AddApplicationAsync<T>()` 一行引导 |
| 零手写 DI | 类实现 `ISingletonDependency` / `IScopedDependency` / `ITransientDependency` 即自动注册 |
| CQRS 无 Dispatcher | Command/Query 就是事件，派发复用本地事件总线 `ILocalEventBus.PublishAsync`，事务自动包裹 |
| 零 MapXxx | 端点类继承 `MinimalApiServiceBase`，public 方法按命名约定自动生成 `api/v1/...` 路由 |
| 数据访问自动化 | 实体实现契约接口（`IEntity`/`ISoftDelete`/`IMultiTenant`...）即获得 Id 生成、审计、软删、租户过滤 |

典型请求链路（一次下单）：

```
POST /api/v1/Orders
  └─ OrderService.CreateAsync                    [AutoValidation] 先校验 Dto
       └─ ILocalEventBus.PublishAsync(CreateOrderCommand)
            └─ 事务中间件（自动 Commit/Rollback）
                 └─ CommandHandler 执行 → 聚合根 AddDomainEvent → repository.InsertAsync
                 └─ IUnitOfWork.CommitAsync      提交前 drain 领域事件队列 → 领域事件 handler 同事务执行
```

## 2. 能力域地图与路由表

| 能力域 | 文档 | 覆盖的包 | 解决什么问题 |
|---|---|---|---|
| 框架内核 | [framework-core.md](./framework-core.md) | Cike.Core、Cike.Contracts | 模块系统、自动 DI、异常体系、DTO/分页契约、常用工具与表达式组合扩展 |
| HTTP 接口层 | [http-api.md](./http-api.md) | Cike.AspNetCore.MinimalAPIs、Cike.AspNetCore.Swagger、Cike.FluentValidation | 自动路由、全局 HTTP 行为、Swagger、参数校验 |
| 数据访问 | [data-access.md](./data-access.md) | Cike.Data、Cike.Data.Domain、Cike.Data.EFCore、.MySql、.SqlServer、Cike.Uow | 实体基类、仓储、DbContext 自动化、事务边界、方言 |
| 事件与 CQRS | [events-cqrs.md](./events-cqrs.md) | Cike.EventBus、.Local、.Adaptive、Cike.Cqrs | Command/Query 用例、本地事件、领域事件、Saga 补偿、后台事件 |
| 认证与当前用户 | [auth.md](./auth.md) | Cike.Auth | ICurrentUser/ICurrentTenant 上下文、租户解析中间件 |
| 缓存 | [caching.md](./caching.md) | Cike.Caching | Redis 分布式缓存、内存+Redis 两级缓存、跨实例失效 |
| 锁 | [locks.md](./locks.md) | Cike.Locks、Cike.Locks.DistributedRedis | 进程内锁、Redis 分布式锁 |
| Id 生成 | [id-generation.md](./id-generation.md) | Cike.UniversalId | 雪花 Id、顺序 Guid（业务层通常无需手动调用） |
| 本地化 | [localization.md](./localization.md) | Cike.Localization | IStringLocalizer 与资源贡献者模型（现状使用率低） |

**开发者意图 → 文档**的典型映射示例：

- "我要新增一个接口/端点" → [HTTP 接口层](./http-api.md)
- "我要实现一个业务用例（写/查）" → [事件与 CQRS](./events-cqrs.md)
- "我要建实体/表、写仓储查询、开事务" → [数据访问](./data-access.md)
- "聚合根状态变化后要联动别的聚合" → [事件与 CQRS](./events-cqrs.md)（领域事件）
- "怎么拿到当前登录用户/租户" → [认证与当前用户](./auth.md)
- "加缓存/加分布式锁" → [缓存](./caching.md) / [锁](./locks.md)
- "新建项目怎么组装模块、怎么注册服务" → [框架内核](./framework-core.md) + 本文档第 3 节

## 3. 全局分层规范

以 `samples/cqrs` 为基准的标准业务解决方案结构（`*` 为项目名前缀）：

| 项目 | 职责 | 放什么 | 不许放什么 |
|---|---|---|---|
| `*.Domain.Shared` | 最稳定的领域概念 | 枚举、常量 | 实体类、业务逻辑 |
| `*.Domain` | 领域层 | 聚合根/实体、子实体、值对象、领域事件 | EF Core 代码、Dto、HTTP 相关 |
| `*.Application.Contracts` | 应用层契约 | Dto、Command、Query | 业务逻辑、Handler |
| `*.Application` | 应用层 | `[LocalEventHandler]` 的命令/查询/领域事件 Handler、FluentValidation Validator | 实体类、DbContext、HTTP 相关 |
| `*.EntityFrameworkCore` | EF Core 实现 | DbContext、模块（`AddCikeDbContext`）、迁移 | 业务规则 |
| `*.Service.Open` | HTTP 宿主 | Program.cs、启动模块、MinimalAPI 端点类、appsettings.json | 业务逻辑、实体 |
| `*.Tests`（可选） | 测试 | xUnit 集成测试 | — |

**项目引用关系**（实测自 samples/cqrs）：

```
Service.Open ──→ Application ──→ Application.Contracts ──→ Domain.Shared
    │                │    └──→ Domain ──→ Domain.Shared
    │                └──→ Cike.Cqrs / Cike.EventBus.Local / Cike.FluentValidation / Cike.Data.EFCore
    └──→ EntityFrameworkCore ──→ Domain + Cike.Data.EFCore + 方言包(MySql/SqlServer)
Application.Contracts ──→ Cike.Contracts + Cike.Cqrs      // Command/Query 定义在契约层
Domain ──→ Cike.Data.Domain                                // 实体基类与仓储接口
```

**命名规范**：
- 每个项目一个模块类：`项目名去掉点 + Module`（如 `CQRSApplicationModule`），依赖用 `[DependsOn]` 声明
- 每个项目一个 `_Imports.cs`，用 `global using` 集中管理公共引用
- 业务按聚合根组织子目录：`Application/Orders/` 下有 `Commands/`、`Queries/`、`Validators/`、Handler 类
- 历史代码中存在 `CommandHanlder` 拼写错误（少个 d），新代码一律写 `Handler`

## 4. 全局隐式行为汇总

框架"引用即生效"的自动化清单（详细条件与例外见对应能力域文档）：

| # | 行为 | 触发条件 | 详见 |
|---|---|---|---|
| 1 | 程序集自动 DI 注册 | 类实现 `ISingleton/IScoped/ITransientDependency`；接口/基类以转发工厂注册，生命周期跟随类自身标记 | 框架内核 |
| 2 | Handler 自动注册 | 方法标注 `[LocalEventHandler]`，所在类自动 AddScoped | 事件与 CQRS |
| 3 | MinimalAPI 自动路由 | 类继承 `MinimalApiServiceBase`，public 方法按命名约定生成 `api/v1/...` | HTTP 接口层 |
| 4 | 雪花/顺序 Guid Id 生成 | `IEntity<long>.Id == 0` 的实体被跟踪（Add）；`IEntity<Guid>` 自动顺序 Guid | 数据访问 / Id 生成 |
| 5 | 审计字段填充 | 实体实现 `IAuditedEntity`：Added 填 CreatedAt/CreatedBy，Modified 填 UpdatedAt/UpdatedBy | 数据访问 |
| 6 | 软删 | 实体实现 `ISoftDelete`：删除变 `IsDeleted=true`，查询自动过滤已删 | 数据访问 |
| 7 | 多租户过滤 | 实体实现 `IMultiTenant`：插入自动填 TenantId，查询自动按 `ICurrentTenant.Id` 过滤（`CurrentTenant` 来源是 `TenantMiddleware`，未启用 `UseMultiTenant()` 时恒为 0） | 数据访问 / 认证 |
| 8 | 事务包裹 | 每次**根** `PublishAsync`（嵌套发布共享同一事务）：事件树成功 Commit / 失败 Rollback | 事件与 CQRS |
| 9 | 领域事件入队/发布 | 聚合根 `AddDomainEvent(...)`：SaveChanges 入队，UoW Commit 提交前依次发布 | 事件与 CQRS |
| 10 | 业务异常转 400 | 抛 `UserFriendlyException`/`BusinessException` → 中间件返回 BadRequest + 消息文本 | HTTP 接口层 |
| 11 | long → string JSON | 所有 HTTP 响应中 `long`/`long?` 序列化为字符串（防前端精度丢失） | HTTP 接口层 |
| 12 | 乐观并发戳刷新 | 实体实现 `IHasConcurrencyStamp`，修改时自动刷新 ConcurrencyStamp | 数据访问 |
| 13 | 后台事件 | 事件开启后台模式即生效：继承 `BackgroundEvent` 或调用 `EnableBackgroundThread()`（`Event` 基类已实现 `IBackgroundEvent`，实际开关是 `IsBackgroundThread()`，默认同步）→ Channel + 独立 Scope 并发消费，异常仅记日志、无事务、不通知发布方 | 事件与 CQRS |
| 14 | Saga 补偿 | `[LocalEventHandler(IsCancel = true)]`：handler 失败（重试耗尽、非 Ignore）时按 Order **升序**执行 `Order ≤ 失败步骤`（Throw 为 `≤ Order-1`）的取消 handler，然后抛出原异常 | 事件与 CQRS |
| 15 | Validator 自动注册 | 依赖 `CikeFluentValidationModule`：全部已加载模块程序集的 `AbstractValidator<T>` 自动注册 | HTTP 接口层 |
| 16 | 缓存 key 格式化 | 默认 `TypeName` 模式：实际 key = `{类型简单名}.{key}`（如 `User.42`，不含命名空间）；L2 Redis 端为 GZip 压缩的 Hash，redis-cli 不可直读 | 缓存 |
| 17 | 端点默认要求认证 | 全局 `GlobalMinimalApiRouteOptions.EnabledAuthorization`（默认 true）是**总开关**：关 → 不挂认证中间件且所有端点不加 `RequireAuthorization`；开 → 端点级 `RouteOptions.EnabledAuthorization`（默认 true）逐服务决定是否加；未注册认证方案时是 500 而非 401 | HTTP 接口层 |

## 5. 跨模块最佳实践与反模式

**最佳实践**（跨模块；模块内的实践见各详解文档）：

- 服务注册一律走标记接口 / `[Dependency]`，让框架机制发现你的类
- 命令侧 Handler 注入 `IRepository`，查询侧注入 `IReadOnlyRepository`（编译期隔离读写）
- 查询只读场景用 `repository.BeginAsNoTracking()` 包裹，复杂查询走 `GetQueryable()` / `WithDetailsAsync()`
- 动态组合 `Expression<Func<T, bool>>` 查询条件用 `ExpressionExtensions.And/Or/Not`（`using Cike.Core.Extensions.Expressions;`，参数自动重绑定），不要手写 ExpressionVisitor
- 仓储 `GetListAsync` / `GetPagedListAsync` 内部统一汇入虚方法 `ToListAsync` / `ToPagedListAsync` 提交——自定义仓储重写这两个出口即可影响全部列表/分页查询
- `autoSave` 默认 true 立即保存；配合工作单元统一提交时传 false
- 业务校验失败抛 `UserFriendlyException`，HTTP 层自动转 400 + 消息文本
- 端点类是 Singleton：Scoped 依赖（EventBus/仓储/DbContext）用 `[FromServices]` 方法参数注入，不要构造函数注入

**反模式（不要做）**：

1. 不要手写 `services.AddXxx` 注册业务类——用标记接口
2. 不要手写 `app.MapGet/MapPost`——继承 `MinimalApiServiceBase` 走命名约定
3. 不要手写 `AddValidatorsFromAssembly`——框架已自动扫描，重复注册造成解析混乱
4. 不要用 `AddDbContext/AddDbContextPool`——会绕开连接串解析与 UoW 集成，一律 `AddCikeDbContext<T>()`
5. 不要在 Handler 里手动管理事务——`PublishAsync` 的事务中间件已覆盖
6. 不要给新增实体手工赋 Id、手工填审计字段/TenantId——框架自动完成
7. 不要在查询里手写 `TenantId == ...` 或 `IsDeleted == false`——全局过滤器已带上；确需临时关闭用 `IDataFilter`
8. 不要在非宿主项目（Domain/Application 等）引用 `Cike.AspNetCore.*`——HTTP 只属于 Service.Open 层
9. 不要在 Application 层直接依赖 `XxxDbContext`——通过仓储接口访问
10. 新建 Command/Query 必须继承 `Command` / `Query<TResult>`——不继承就不是 `IEvent`，发布时被**静默忽略**
11. 不要用后台事件实现需要"全成功或全回滚"的流程——后台事件无事务保障，异常不会通知发布方
12. 不要在 DTO 之外的地方把 HTTP 概念（`Results`、`[FromServices]` 等）引入 Application/Domain 层
13. 多实例部署不要直接依赖默认雪花生成器注册——机器号硬编码为 1，会产出重复 Id（见 Id 生成文档）

## 6. 废弃与现状说明

- **Cike.MultiTenant 已废弃，勿用**：不是合法模块（未继承 `CikeModule`）、`Guid?` 租户 Id 与框架现行 `long` 冲突、命名空间与 `Cike.Auth` 的同名类型冲突。现行多租户能力全部在 [Cike.Auth](./auth.md)。
- **Cike.Localization 现状使用率低，且实现不完整**：`CikeStringLocalizer`/`Factory` 为存根（抛 `NotImplementedException`），框架内无任何代码消费其资源贡献模型；现有业务项目多为中文硬编码消息。新项目除非明确需要多语言并自行补全实现，建议沿用团队惯例。
- 文档随 `main` 分支演进，远程链接永远读到最新版。
