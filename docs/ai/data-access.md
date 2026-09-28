# 数据访问

## 域定位

解决"业务实体定义 → 仓储访问 → ORM 集成 → 数据库方言 → 事务边界"的全链路问题。核心思路是**契约驱动自动化**：实体实现契约接口（或继承基类）即免费获得 Id 生成、审计填充、软删、多租户过滤、乐观并发、领域事件入队、自动事务，业务代码禁止手工做这些事。

四层结构（依赖自上而下）：

```
Cike.Data            契约层：实体契约接口（IEntity/ISoftDelete/IMultiTenant...）、连接串解析、数据过滤器
   ↓
Cike.Data.Domain     抽象层：实体/聚合根基类现成实现 + 三层仓储接口（不依赖任何 ORM）
   ↓
Cike.Data.EFCore     实现层：CikeDbContext 自动化、默认/自定义仓储、EFCoreUnitOfWork、DbContext 工厂
   ↓
Cike.Data.EFCore.MySql / Cike.Data.EFCore.SqlServer    方言层：UseMySQL / UseSqlServer + 顺序 Guid 默认值

Cike.Uow             事务边界抽象：IUnitOfWork / UnitOfWorkOptions（仅接口；实现在 Cike.Data.EFCore）
```

分层含义：Domain 层业务代码只接触 `Cike.Data` 契约 + `Cike.Data.Domain` 基类与仓储接口，完全不感知 EF Core；`*.EntityFrameworkCore` 项目负责 `CikeDbContext` + 方言；换数据库只换最底层的方言包。

事务边界一句话：**实体状态一被跟踪就自动开事务；领域事件在 `SaveChanges` 时入队；`IUnitOfWork.CommitAsync` 在提交事务前先排空领域事件队列（handler 与业务写同事务）；常规请求流由事件管道的事务中间件统一 Commit/Rollback**（见 [事件与 CQRS](./events-cqrs.md)）。

## 覆盖的包

| 包 | 职责 | 直接依赖（模块） |
|---|---|---|
| Cike.Data | 契约层：实体契约接口、`[ConnectionStringName]` + 连接串解析、`IDataFilter` 数据过滤器 | `CikeEventBusModule`（Cike.EventBus） |
| Cike.Data.Domain | 实体基类继承链、聚合根、值对象、三层仓储接口 | `CikeDataModule` |
| Cike.Data.EFCore | `CikeDbContext<T>` 自动化、`EfCoreRepository`、`EFCoreUnitOfWork`、`AddCikeDbContext`、DbContext 工厂 | `CikeAuthModule`、`CikeDomainModule`、`CikeUniversalIdModule`、`CikeUowModule`、`CikeEventBusAdaptiveModule` |
| Cike.Data.EFCore.MySql | MySQL（Pomelo 8.0.2）方言：`UseMySQL` + SplitQuery + `SequentialAsString` 默认 | `CikeDataEFCoreModule` |
| Cike.Data.EFCore.SqlServer | SQL Server（EF 8.0.5）方言：`UseSqlServer` + SplitQuery + `SequentialAtEnd` 默认 | `CikeDataEFCoreModule` |
| Cike.Uow | 工作单元契约：`IUnitOfWork`、`UnitOfWorkOptions`、`UnitOfWorkCommitState` | 无 |

依赖顺序图（被依赖者在前，模块 `ConfigureServicesAsync` 按此序串行执行）：

```
Cike.EventBus ──→ Cike.Data ──→ Cike.Data.Domain ──→ Cike.Data.EFCore ──→ { .MySql, .SqlServer }
Cike.Uow（独立，无依赖）────────────────────────────↗
Cike.Auth / Cike.UniversalId / Cike.EventBus.Adaptive ───────────────────↗
```

命名空间速查（包名与命名空间不一致，AI 高频踩坑点）：

| 包 | 实际命名空间 | 内容 |
|---|---|---|
| Cike.Data | `Cike.Data` | 全部契约接口 |
| | `Cike.Data.Attributes` | `ConnectionStringNameAttribute` |
| | `Cike.Data.DataFilters` | `IDataFilter` / `DataFilter` / `CikeDataFilterOptions` |
| Cike.Data.Domain | `Cike.Domain.Entities` | `Entity` / `AuditedEntity` / `FullAuditedEntity` |
| | `Cike.Data.Domain.Entities` | `FullAuditedAggregateRoot`（仅此类在此命名空间） |
| | `Cike.Data.Domain.AggregateRoots` | `AggregateRoot`（类）、`IAggregateRoot`、`IFullAuditedAggregateRoot`、`DomainEvent` |
| | `Cike.Domain.Repositories` | 三个仓储接口 |
| | `Cike.Domain` | 静态 `EntityHelper`、`CikeDomainModule` |
| | `Core.Plugin.Ddd.Domain` | `ValueObject`（历史遗留命名空间） |
| Cike.Data.EFCore | `Cike.Data.EFCore` | `CikeDbContext` / `CikeDbContextOptions` / `CikeDbContextConfigurationContext` |
| | `Cike.Data.EFCore.Repositories` | `EfCoreRepository` |
| | `Cike.Data.EFCore.Uow` | `EFCoreUnitOfWork` |
| | `Cike.Data.EFCore.Extensions` | `EntityTypeBuilderExtensions`（`ConfigureByConvention`） |
| | `Cike.Data.Extensions` | `AddCikeDbContext`（注意：在本包，命名空间却是 Cike.Data.Extensions） |
| | `Cike.Contracts.Extensions` | `ToPaginationAsync` / `WhereIf` / `GetAsync` 扩展 |
| Cike.Data.EFCore.MySql / .SqlServer | `Cike.Data.EFCore.Extensions` | `UseMySQL` / `UseSqlServer` 扩展方法 |
| Cike.Uow | `Cike.Uow` / `Cike.Uow.Enums` | `IUnitOfWork` / `UnitOfWorkOptions` / `UnitOfWorkCommitState` |

## 章节目录

- [Cike.Data](#cikedata) — 契约层：实体契约接口、连接串解析、数据过滤器
- [Cike.Data.Domain](#cikedatadomain) — 实体基类继承链 + 三层仓储接口
- [Cike.Data.EFCore](#cikedataefcore) — CikeDbContext 自动化、默认/自定义仓储、UoW 实现
- [Cike.Data.EFCore.MySql](#cikedataefcoremysql) — MySQL 方言 Provider
- [Cike.Data.EFCore.SqlServer](#cikedataefcoresqlserver) — SQL Server 方言 Provider
- [Cike.Uow](#cikeuow) — 工作单元与事务边界

## Cike.Data

### 定位

数据访问契约层：实体契约接口（纯接口无实现，业务实体实现即获得框架自动化）、连接串命名与解析、数据过滤器——是 `Cike.Data.Domain` / `Cike.Data.EFCore` 的地基。什么时候不用它：不要拿这里的接口手工拼业务实体（用 `Cike.Data.Domain` 的基类）；除 `IMultiTenant` / `IPagedAndSortedRequest` / `IDataFilter` / `[ConnectionStringName]` 外，业务代码很少直接引用本包类型。

### 能力清单

**实体契约接口**（自动化动作由 Cike.Data.EFCore 的 ChangeTracker 钩子执行，契约本身只是声明）：

| 接口 | 成员 | 触发的自动化 |
|---|---|---|
| `IEntity` | `object[] GetKeys()` | 标记"是实体"：默认仓储注册、模型约定配置、软删/租户过滤器判定的前置条件 |
| `IEntity<TKey> : IEntity` | `TKey Id { get; set; }` | 跟踪为 Added 时生成 Id：`IEntity<long>` 且 `Id == 0` → 雪花 Id；`IEntity<Guid>` 且 `Id == Guid.Empty` → 顺序 Guid（见 [Id 生成](./id-generation.md)） |
| `ICreateAuditedEntity<TUserId>`（`TUserId : struct`） | `DateTime CreatedAt`、`TUserId CreatedBy` | Added 时填充（仅当 `CreatedAt == default`；自动填充只识别 `long` / `Guid` 两种 TUserId） |
| `IAuditedEntity<TUserId> : ICreateAuditedEntity<TUserId>` | `DateTime UpdatedAt`、`TUserId UpdatedBy` | Added / Modified 时填充；Modified 每次无条件覆盖 |
| `IFullAuditedEntity<TUserId> : IAuditedEntity<TUserId>, ISoftDelete` | — | 审计 + 软删的组合标记 |
| `ISoftDelete` | `bool IsDeleted { get; set; }` | 删除转软删（`IsDeleted = true`，状态转为 Modified）；全局过滤器自动排除已删数据 |
| `IMultiTenant` | `long TenantId { get; set; }` | 跟踪为 Added 时从环境租户（`ICurrentTenantAccessor`，AsyncLocal）写入 TenantId（无条件覆盖）；查询按 `ICurrentTenant.Id` 过滤 |
| `IHasConcurrencyStamp` | `string ConcurrencyStamp { get; set; }` | Modified / Deleted 保存前刷新并发戳（新 `Guid.ToString("N")`）；列配置为并发令牌（max 40） |
| `IPagedAndSortedRequest` | `int Page` / `int PageSize` / `string Sorting`（均 get/set） | 分页请求契约，被 `GetPagedListAsync` / `ToPaginationAsync` 消费；本身无自动化 |
| `IDomainEvent : IEvent` | — | 领域事件标记（基类 `DomainEvent` 在 Cike.Data.Domain） |
| `IAggregateRoot` / `IAggregateRoot<TKey>` | `DomainEvents` / `AddDomainEvent` / `ClearDomainEvents` | 聚合根契约（**与 Cike.Data.Domain.AggregateRoots 下同名接口重复**，见该章边界） |

**连接串解析**：

| 类型 | 签名 / 行为 |
|---|---|
| `ConnectionStringNameAttribute` | `[ConnectionStringName("name")]` 标在 DbContext 类上覆盖默认名；`GetConnStringName<T>()`：无特性 → `type.Name.Replace("DbContext", "")`（类名中所有 "DbContext" 子串被移除），有特性 → `Name` |
| `IConnectionStringResolver` | `Task<string> ResolveAsync(string connectionStringName)` |
| `DefaultConnectionStringResolver` | 默认实现（Singleton）：从 `CikeDbConnectionOptions.ConnectionStrings` 字典按 key **精确匹配**取值，未命中返回 `null` |
| `CikeDbConnectionOptions` | `Dictionary<string, string> ConnectionStrings`，绑定配置根（属性名匹配 `ConnectionStrings` 节） |

**数据过滤器**：

| 类型 | 签名 |
|---|---|
| `IDataFilter` | `IDisposable Enable<TFilter>() where TFilter : class`、`IDisposable Disable<TFilter>()`、`bool IsEnabled<TFilter>()` |
| `IDataFilter<TFilter>` | `IDisposable Enable()`、`IDisposable Disable()`、`bool IsEnabled { get; }` |
| `DataFilter` / `DataFilter<TFilter>` | 默认实现；状态存 `AsyncLocal<DataFilterState>`（按异步上下文隔离，非按 Scope） |
| `CikeDataFilterOptions` | `Dictionary<Type, DataFilterState> DefaultStates`——过滤器类型的默认开关；未注册的类型默认**启用** |

### 隐式行为

- 依赖本包（`CikeDataModule`）即：把整份 `IConfiguration` 绑定到 `CikeDbConnectionOptions`（连接串可被解析）；注册 `IDataFilter<TFilter> → DataFilter<TFilter>`（Singleton）；`DataFilter`（非泛型）与 `DefaultConnectionStringResolver` 经标记接口约定注册。
- 过滤器默认状态：`ISoftDelete` / `IMultiTenant` 的全局查询过滤器**默认开启**（未在 `DefaultStates` 注册的类型默认 `IsEnabled = true`）。
- 连带引入 `Cike.EventBus`：`IEvent` / `Event` / `IDomainEvent` / `IQueueEventBus` 基础类型来自这里。
- `Enable/Disable` 返回的 `IDisposable` 在 Dispose 时恢复原状态；对"已处于目标状态"的重复调用返回空操作（`NullDisposable`），嵌套使用安全。

### 示例

```csharp
// 1. 连接串命名：DbContext 类名去掉 "DbContext" 后缀即连接串 key
public class OrderDbContext : CikeDbContext<OrderDbContext> { }
// → key = "Order"，对应 appsettings.json：
// { "ConnectionStrings": { "Order": "Server=...;Database=order;Uid=...;Pwd=..." } }

// 2. 显式指定 key（类名不规则时）
[ConnectionStringName("order-db")]
public class OrderDbContext : CikeDbContext<OrderDbContext> { }

// 3. 运行时临时关闭软删过滤器（回收站场景；using 结束自动恢复）
public class RecycleBinService(IRepository<Folder, long> repository, IDataFilter dataFilter)
{
    public async Task<List<Folder>> GetAllIncludingDeletedAsync()
    {
        using (dataFilter.Disable<ISoftDelete>())
        {
            return await repository.GetListAsync();   // 此时能查到 IsDeleted == true 的数据
        }
    }
}
```

### 配置

- appsettings.json：`ConnectionStrings` 节，key 与 DbContext 连接串名**严格匹配**（区分大小写、无 `Default` 回退）。
- 过滤器默认状态：`services.Configure<CikeDataFilterOptions>(o => o.DefaultStates[typeof(ISoftDelete)] = new DataFilterState(false))`（默认全部启用，一般不改）。
- 其余无 Options、无默认值可调。

### 边界与反模式

- 连接串 key 未命中时 `ResolveAsync` 返回 `null`——不报"key 不存在"的错，而是把 `null` 传给 provider 的 `UseMySql/UseSqlServer`，在**首次解析 DbContext** 时才抛 provider 异常；排障先查 key 拼写。
- `DefaultConnectionStringResolver` 在构造时快照 `IOptionsMonitor.CurrentValue`——连接串**热更新不生效**，改连接串需重启。
- `IMultiTenant.TenantId` 是非可空 `long`：无租户上下文时写 0（租户 0），不要用可空类型自行改造。
- `IPagedAndSortedRequest` 三个属性都是可写属性（非 init）——实现时不能用 `record` 的 init-only 属性。
- 契约层的 `IAggregateRoot` 与领域层同名接口重复（已知技术债）：手工实体若只实现契约层 `IAggregateRoot`，领域事件**不会入队**（CikeDbContext 判定的是领域层接口，详见 Cike.Data.Domain / Cike.Data.EFCore 边界）。

## Cike.Data.Domain

### 定位

领域实体基类与仓储抽象：审计/软删/聚合根/并发戳的现成实现 + 三层仓储接口，业务实体的唯一推荐来源。什么时候不用它：不要自己拼 `IEntity + ISoftDelete + IMultiTenant` 接口组合（丢基类的现成实现且易漏契约）；本包不含任何 EF Core 代码，DbContext/Fluent 配置放 `*.EntityFrameworkCore` 项目。

### 能力清单

**实体基类继承链**（注意：`FullAuditedAggregateRoot` 与 `AggregateRoot` 是**两条平行分支**——前者直接继承 `FullAuditedEntity`，不经过 `AggregateRoot`）：

```
Entity<TKey>
 ├─ AuditedEntity<TKey, TUserId> ── FullAuditedEntity<TKey, TUserId> ── FullAuditedAggregateRoot<TKey, TUserId>
 └─ AggregateRoot<TKey>（无审计，仅领域事件）
```

| 类型 | 命名空间 | 继承 / 实现 | 自有成员 |
|---|---|---|---|
| `Entity<TKey>`（abstract） | `Cike.Domain.Entities` | `IEntity<TKey>` | `Id`、`virtual void SetId(TKey)`、`GetKeys()` |
| `AuditedEntity<TKey, TUserId>` | `Cike.Domain.Entities` | `Entity<TKey>` + `IAuditedEntity<TUserId>` | `CreatedAt` `CreatedBy` `UpdatedAt` `UpdatedBy` |
| `AuditedEntity<TKey>` | `Cike.Domain.Entities` | `AuditedEntity<TKey, long>` | —（TUserId 固定 long 的简写） |
| `FullAuditedEntity<TKey, TUserId>` | `Cike.Domain.Entities` | `AuditedEntity<TKey, TUserId>` + `IFullAuditedEntity<TUserId>` | `bool IsDeleted`（默认 false） |
| `FullAuditedEntity<TKey>` | `Cike.Domain.Entities` | `FullAuditedEntity<TKey, long>` | — |
| `AggregateRoot<TKey>`（abstract） | `Cike.Data.Domain.AggregateRoots` | `Entity<TKey>` + `IAggregateRoot<TKey>` | `IEnumerable<IDomainEvent> DomainEvents`、`AddDomainEvent`、`ClearDomainEvents` |
| `AggregateRoot`（abstract，非泛型） | `Cike.Data.Domain.AggregateRoots` | `IAggregateRoot` | 同上 + `abstract object?[] GetKeys()`（少用） |
| `FullAuditedAggregateRoot<TKey, TUserId>` | `Cike.Data.Domain.Entities` | `FullAuditedEntity<TKey, TUserId>` + `IFullAuditedAggregateRoot<TKey, TUserId>` + `IHasConcurrencyStamp` | `DomainEvents` / `AddDomainEvent` / `ClearDomainEvents` + `virtual string ConcurrencyStamp`（构造时初始化为 `Guid.ToString("N")`） |
| `FullAuditedAggregateRoot<TKey>` | `Cike.Data.Domain.Entities` | `FullAuditedAggregateRoot<TKey, long>` | —（**标准业务实体推荐基类**：审计 + 软删 + 领域事件 + 并发戳） |
| `ValueObject`（abstract） | `Core.Plugin.Ddd.Domain` | `IEquatable<ValueObject>` | `protected abstract IEnumerable<object> GetEqualityComponents()`；重写 Equals/GetHashCode/==/!=（按分量比较） |
| `DomainEvent`（abstract record） | `Cike.Data.Domain.AggregateRoots` | `Event` + `IDomainEvent` | —（业务领域事件的基类：`public record OrderPaidEvent(...) : DomainEvent;`） |
| `EntityHelper`（static） | `Cike.Domain` | — | `Type? FindPrimaryKeyType(Type)` / `FindPrimaryKeyType<TEntity>()`：反射 `IEntity<>` 取主键类型，非实体抛 `ArgumentException` |

聚合根接口（`Cike.Data.Domain.AggregateRoots`）：`IAggregateRoot : IEntity`、`IAggregateRoot<TKey> : IEntity<TKey>, IAggregateRoot`、`IFullAuditedAggregateRoot<TKey, TUserId> : IFullAuditedEntity<TUserId>, IEntity<TKey>, IAggregateRoot<TKey>`、`IFullAuditedAggregateRoot<TKey> : IFullAuditedAggregateRoot<TKey, long>`。

**三层仓储接口**（`Cike.Domain.Repositories`，约束 `where TEntity : class, IEntity<TKey>`——`IEntity` 是 Cike.Data 契约层接口）：

```
IReadOnlyRepository<TEntity, TKey>     查询面（编译期只读，CQRS 查询侧注入）
    ↑
IBasicRepository<TEntity, TKey>        + 增删改（均带 autoSave）
    ↑
IRepository<TEntity, TKey>             合并标记接口（命令侧 / 常规业务注入）
```

`IReadOnlyRepository` 查询面方法全集（与源码逐一对齐）：

| 签名 | 语义 |
|---|---|
| `IDisposable BeginAsNoTracking()` | 临时关闭查询跟踪，Dispose 恢复；跟踪开关在 `GetQueryable()` 调用时固化进查询 |
| `Task<TEntity> GetAsync(TKey id, CancellationToken ct = default)` | 未找到抛 `UserFriendlyException($"Id {id} is NotFound.")` |
| `Task<TEntity?> FindAsync(TKey id, CancellationToken ct = default)` | 未找到返回 `null` |
| `Task<List<TEntity>> GetListAsync(CancellationToken ct = default)` | 全量列表 |
| `Task<List<TEntity>> GetListAsync(Expression<Func<TEntity, bool>> predicate, string sorting = "Id asc", CancellationToken ct = default)` | 条件列表 + 排序（`sorting` 是 System.Linq.Dynamic.Core 字符串，如 `"Name"` / `"Name desc"`；缺省 `"Id asc"`。注意：没有 `(predicate, ct)` 二参重载——传取消令牌需写全 `GetListAsync(pred, "Id asc", ct)`，避免与 `sorting` 参数冲突） |
| `Task<(long Total, List<TEntity> Items)> GetPagedListAsync(IPagedAndSortedRequest request, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)` | 分页 + 排序（`Sorting` 是 System.Linq.Dynamic.Core 字符串，如 `"Name"` / `"Name desc"`；先 Count 后排序分页） |
| `Task<List<TEntity>> ToListAsync(IQueryable<TEntity> query, CancellationToken ct = default)` | 提交自定义 IQueryable 取列表——所有 `GetListAsync` 重载内部统一走它（可重写出口） |
| `Task<(long Total, List<TEntity> Items)> ToPagedListAsync(IQueryable<TEntity> query, IPagedAndSortedRequest request, CancellationToken ct = default)` | 提交自定义 IQueryable 分页（内部 `ToPaginationAsync`）——`GetPagedListAsync` 内部统一走它（可重写出口） |
| `Task<long> GetCountAsync(CancellationToken ct = default)` | 计数 |
| `Task<long> GetCountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)` | 条件计数 |
| `Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken ct = default)` | 存在性 |
| `IQueryable<TEntity> GetQueryable()` | IQueryable 出口（同步方法；软删/多租户全局过滤器生效；默认跟踪） |
| `IQueryable<TEntity> WithDetails(params Expression<Func<TEntity, object?>>[] propertyPaths)` | 指定导航预加载（Include）的 IQueryable——**同步方法，名为 WithDetails 而非 WithDetailsAsync** |

`IBasicRepository` 写方法（均带 `autoSave`，默认 `true`）：

| 签名 | 语义 |
|---|---|
| `Task<TEntity> InsertAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default)` | 返回已填充实体（Id/审计/TenantId 在跟踪时已写入） |
| `Task InsertManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken ct = default)` | 批量插入 |
| `Task<TEntity> UpdateAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default)` | 更新并返回实体 |
| `Task UpdateManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken ct = default)` | 批量更新 |
| `Task DeleteAsync(TEntity entity, bool autoSave = true, CancellationToken ct = default)` | 删除（`ISoftDelete` 实体自动转软删） |
| `Task DeleteAsync(TKey id, bool autoSave = true, CancellationToken ct = default)` | 按主键删除；不存在时静默返回（幂等） |
| `Task DeleteManyAsync(IEnumerable<TEntity> entities, bool autoSave = true, CancellationToken ct = default)` | 批量删除 |

`IRepository` 无新成员，注入它即获得全部读写能力。

实现约定（`EfCoreRepository`）：单数写方法内部**委托批量方法**（`InsertAsync → InsertManyAsync(new[]{entity})`，Update/Delete 同理）——新增/更新/删除逻辑各自内聚在批量路径里，自定义仓储只需覆写批量方法，单数方法自动获得同样行为。

### 隐式行为

- `CikeDomainModule` 无任何服务配置；继承基类即隐式实现全部契约接口，自动获得 Cike.Data 契约表中的所有自动化（Id / 审计 / 软删 / 租户 / 并发戳 / 领域事件入队，由 EF Core 层的 ChangeTracker 钩子执行）。
- 仓储接口不依赖任何 ORM（`IQueryable` 属 System.Linq）——EF Core 实现由 `AddCikeDbContext` 注册（见 [Cike.Data.EFCore](#cikedataefcore)）。

### 示例

```csharp
using Cike.Data;                        // IMultiTenant
using Cike.Data.Domain.AggregateRoots;  // DomainEvent
using Cike.Data.Domain.Entities;        // FullAuditedAggregateRoot（注意命名空间）

public class Order : FullAuditedAggregateRoot<long>, IMultiTenant
{
    public long TenantId { get; set; }               // IMultiTenant 契约：自动填充 + 过滤
    public string OrderNo { get; private set; } = default!;
    public OrderStatus Status { get; private set; }  // 审计四字段、IsDeleted、ConcurrencyStamp、DomainEvents 全部来自基类

    private Order() { }

    public static Order Place(string orderNo)
    {
        var order = new Order { OrderNo = orderNo };
        order.AddDomainEvent(new OrderPlacedEvent(orderNo));  // SaveChanges 时入队，UoW 提交前发布
        return order;
    }

    public void Cancel()
    {
        Status = OrderStatus.Cancelled;
        AddDomainEvent(new OrderCancelledEvent(Id, OrderNo));
    }
}

public record OrderPlacedEvent(string OrderNo) : DomainEvent;
public record OrderCancelledEvent(long OrderId, string OrderNo) : DomainEvent;

// 值对象（EF Core 中以 OwnsOne 映射，命名空间特殊）
using Core.Plugin.Ddd.Domain;

public class Address : ValueObject
{
    public string Province { get; private set; } = default!;
    public string City { get; private set; } = default!;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Province;
        yield return City;
    }
}

// 注入选型：查询侧只读，命令侧全量
public class OrderQueryHandler(IReadOnlyRepository<Order, long> orders) { }  // 编译期即无写能力
public class OrderCommandHandler(IRepository<Order, long> orders) { }
```

### 配置

无（`CikeDomainModule` 无逻辑，本包无可配置项）。

### 边界与反模式

- 【注意】**两份同名聚合根接口**：`Cike.Data.IAggregateRoot`（契约层）与 `Cike.Data.Domain.AggregateRoots.IAggregateRoot`（领域层）。本包的 `AggregateRoot` / `FullAuditedAggregateRoot` 实现的是**领域层**那份；CikeDbContext 的领域事件入队也只判定领域层那份。统一它们是破坏性变更，属已知技术债——业务代码引用基类一律用本包，写 `using` 时留意命名空间。
- 命名空间混乱是刻意记录的现状：`Entity/AuditedEntity/FullAuditedEntity` 在 `Cike.Domain.Entities`，而 `FullAuditedAggregateRoot` 在 `Cike.Data.Domain.Entities`，`ValueObject` 在 `Core.Plugin.Ddd.Domain`——实体文件通常需要同时 using 多个命名空间。
- 审计自动填充只识别 `IAuditedEntity<long>` / `IAuditedEntity<Guid>`：自定义 `TUserId`（如 `int`、`string`）的 `IAuditedEntity<TUserId>` **不会被填充**，模型约定配置也会跳过。
- `FullAuditedAggregateRoot` 的 `ConcurrencyStamp` 在构造时即初始化；并发戳刷新逻辑见 Cike.Data.EFCore（Modified/Deleted 保存前刷新）。
- 不要绕过基类自己实现 `IEntity<TKey>` + 各契约接口——会丢 `SetId`/`GetKeys`/领域事件集合的现成实现，且默认仓储/模型约定对"仅实现契约"的类型行为一致但审计等钩子判定分散，易漏。

## Cike.Data.EFCore

### 定位

EF Core 集成核心：`CikeDbContext<TDbContext>` 基类（全部自动化钩子）、默认/自定义仓储实现、`EFCoreUnitOfWork`、DbContext 工厂与 `AddCikeDbContext` 注册入口；数据库方言由 MySql / SqlServer Provider 包提供。什么时候不用它：不要用 `AddDbContext` / `AddDbContextPool`（见边界）；纯 Dapper/ADO.NET 场景框架无集成，需自行组装；Domain/Application 层项目不要引用本包（EF Core 只属于 `*.EntityFrameworkCore` 层）。

### 能力清单

**`CikeDbContext<TDbContext>`**（`Cike.Data.EFCore`）——业务 DbContext 基类：

```csharp
public abstract class CikeDbContext<TDbContext> : DbContext, IScopedDependency where TDbContext : DbContext
{
    public CikeDbContext(DbContextOptions<TDbContext> options, IServiceProvider serviceProvider);
    protected IServiceProvider CurrentServiceProvider { get; }   // 无 IServiceProvider 构造时访问即抛异常
    protected ICurrentUser CurrentUser { get; }                  // Cike.Auth
    protected ICurrentTenant CurrentTenant { get; }              // Cike.Auth
    public IDataFilter DataFilter { get; }
    public UnitOfWorkOptions UnitOfWorkOptions { get; }
    protected virtual bool IsMultiTenantFilterEnabled { get; }   // => DataFilter.IsEnabled<IMultiTenant>()
    protected virtual bool IsSoftDeleteFilterEnabled { get; }    // => DataFilter.IsEnabled<ISoftDelete>()
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default);
    public override int SaveChanges(bool acceptAllChangesOnSuccess);   // 同步版：不做领域事件入队
    protected override void OnModelCreating(ModelBuilder modelBuilder);
    protected virtual void ConfigureBaseProperties<TEntity>(ModelBuilder, IMutableEntityType) where TEntity : class;
    protected virtual void ConfigureGlobalFilters<TEntity>(ModelBuilder, IMutableEntityType) where TEntity : class;
    protected virtual bool ShouldFilterEntity<TEntity>(IMutableEntityType) where TEntity : class;
    protected virtual Expression<Func<TEntity, bool>>? CreateFilterExpression<TEntity>() where TEntity : class;
    public static EntityTypeBuilder<TEntity> HasCikeQueryFilter<TEntity>(EntityTypeBuilder<TEntity>, Expression<Func<TEntity, bool>>) where TEntity : class;
    protected virtual void ChangeTracker_Tracked(object?, EntityTrackedEventArgs);
    protected virtual void ChangeTracker_StateChanged(object?, EntityStateChangedEventArgs);
}
```

**CikeDbContext 自动化清单**（全部由框架完成，业务代码禁止手工做）：

| 时机 | 自动化 |
|---|---|
| 跟踪为 Added（`Add` / `InsertAsync` 调用时） | Id 生成（`IEntity<long>` 且 Id==0 → 雪花；`IEntity<Guid>` 且 Empty → 顺序 Guid）；`IMultiTenant.TenantId` 从 `ICurrentTenantAccessor`（AsyncLocal）写入（**无条件覆盖**，Add 之后切租户不会改）；`CreatedAt/CreatedBy`（仅 `CreatedAt == default` 时）+ 刷新 Updated；自动开事务（见下） |
| 跟踪为 Modified（`Update` / DetectChanges 发现属性变化） | `UpdatedAt/UpdatedBy` 无条件刷新；自动开事务 |
| 跟踪为 Deleted | 自动开事务 |
| `SaveChanges(Async)` 前（HandlePropertiesBeforeSave） | `IHasConcurrencyStamp`：Modified/Deleted 条目 OriginalValue 固定 + 新 `Guid.ToString("N")`（乐观并发）；`ISoftDelete`：Deleted 状态转 **Modified**（全量更新）、`IsDeleted = true`、刷新更新审计、OwnsOne 值对象条目恢复 Unchanged（避免值对象列被写 NULL） |
| `SaveChangesAsync` 前（EnqueueDomainEventAsync） | 聚合根（领域层 `IAggregateRoot`）的 `DomainEvents` 逐个 `IQueueEventBus.EnqueueAsync` 并 `ClearDomainEvents()`（工作单元提交时发布） |
| OnModelCreating | 每个 `IEntity` 实体 `ConfigureByConvention()`：ConcurrencyStamp 并发令牌（max 40）+ `IsDeleted`/`TenantId`/`CreatedBy`/`UpdatedBy` 列配置与索引；再挂全局查询过滤器 |
| 全局查询过滤器 | `ISoftDelete` → `!IsSoftDeleteFilterEnabled \|\| !IsDeleted`；`IMultiTenant` → `!IsMultiTenantFilterEnabled \|\| TenantId == CurrentTenant.Id`；两者兼备时 AND 合并（`QueryFilterExpressionHelper.CombineExpressions`）；过滤器在**查询执行时**求值（`IDataFilter.Disable` 后立即生效） |
| 自动开事务 | `UnitOfWorkOptions.Enable = true`（默认）时，首个实体状态变化（Added/Modified/Deleted）触发 `IUnitOfWork.BeginTranscationAsync()`；`IUnitOfWork` 未注册（用错 `AddDbContext`）时抛 `InvalidOperationException` |

**注册入口**：

```csharp
// 命名空间 Cike.Data.Extensions（在本包！）
public static IServiceCollection AddCikeDbContext<TDbContext>(this IServiceCollection services, bool addDefaultRepositories = true)
    where TDbContext : CikeDbContext<TDbContext>;
```

一次调用注册三类服务：
1. `DbContextOptions<TDbContext>` 工厂（`DbContextOptionsFactory.Create<T>`，Transient）：连接串名 → `IConnectionStringResolver` 解析 → 构造 `CikeDbContextConfigurationContext<T>`（已带 `UseLoggerFactory` + `UseApplicationServiceProvider`）→ 执行 `CikeDbContextOptions.DefaultConfigureAction`（方言）。
2. `IUnitOfWork → EFCoreUnitOfWork<TDbContext>`（Scoped，`AddScoped` 非 TryAdd）。
3. `addDefaultRepositories = true`（默认）时为每个实现了 `IEntity` 的 DbSet 实体注册默认仓储（见下）。

**默认仓储自动注册规则**：反射 DbContext 的 public 实例 `DbSet<>` 属性，实体需实现 `IEntity` 且能解析 `IEntity<TKey>` 主键类型；对每个实体 `TryAddScoped` 三个接口 `IRepository<,>` / `IBasicRepository<,>` / `IReadOnlyRepository<,>` → `EfCoreRepository<TDbContext, TEntity, TKey>`（Scoped）。约定注册（自定义仓储）在模块加载循环中先于 `AddCikeDbContext` 执行 → 自定义仓储天然以 TryAdd 语义覆盖默认仓储；多个 DbContext 含同一实体时**先注册者保留**。

**`EfCoreRepository<TDbContext, TEntity, TKey>`**（`Cike.Data.EFCore.Repositories`）：

```csharp
public class EfCoreRepository<TDbContext, TEntity, TKey>(TDbContext dbContext) : IRepository<TEntity, TKey>
    where TDbContext : CikeDbContext<TDbContext>
    where TEntity : class, IEntity<TKey>
{
    public TDbContext DbContext { get; }          // 直接暴露 DbContext（自定义仓储可用）
    protected bool asNoTracking;                  // = false
    protected virtual Task SaveChangesIfAsync(bool autoSave, CancellationToken ct = default);
    // + IRepository 全部成员（所有公共方法均为 virtual，可被子类覆写）；
    // GetAsync 未找到抛 UserFriendlyException；DeleteAsync(id) 先 Find 再删
}
```

**IQueryable 扩展**（`Cike.Contracts.Extensions`，在本包）：

| 签名 | 语义 |
|---|---|
| `Task<(long Total, List<TEntity> Items)> ToPaginationAsync<TEntity>(this IQueryable<TEntity>, IPagedAndSortedRequest, CancellationToken ct = default)` | 先 `LongCountAsync` 再排序分页；`Sorting` 非空才 OrderBy（Dynamic.Core）；`PageSize <= 0` 返回全量；**部分路径丢弃 ct** |
| `Task<TEntity> GetAsync<TEntity, TKey>(this IQueryable<TEntity>, TKey id, CancellationToken ct = default) where TKey : struct` | 未找到抛 `UserFriendlyException`（与仓储 `GetAsync` 双轨同形） |
| `IQueryable<TEntity> WhereIf<TEntity>(this IQueryable<TEntity>, bool, Expression<Func<TEntity, bool>>)` | 条件 Where |
| `IQueryable<TEntity> AsNoTracking<TEntity>(this IQueryable<TEntity>, bool asNoTracking)` | 按开关应用跟踪行为（内部显式调用 EF 扩展防自递归） |

**模型约定扩展**（`Cike.Data.EFCore.Extensions`）：`EntityTypeBuilder.ConfigureByConvention()` = `TryConfigureConcurrencyStamp` + `TryConfigureSoftDelete`（列 + 索引）+ `TryConfigureMultiTenant`（列 + 索引）+ `TryConfigureAudited`（审计列 required + 注释 + CreatedBy/UpdatedBy 索引）。

**autoSave 语义**（写方法共用的 `autoSave` 参数）：
- 默认 `true`：立即 `SaveChangesAsync`——并发戳刷新、软删转换、领域事件入队都在这次保存中发生。
- `false`：变更留在 ChangeTracker，等后续 `SaveChanges` / `IUnitOfWork.CommitAsync` 统一保存。
- `autoSave` **只负责 SaveChanges，从不提交事务**——事务提交是工作单元的职责（常规请求流由事件管道中间件完成，见 [事件与 CQRS](./events-cqrs.md)；手动驱动见 [Cike.Uow](#cikeuow)）。

### 隐式行为

- 依赖 `CikeDataEFCoreModule` 即：`CikeSnowflakeOptions.IsEnable = true`（雪花 Id 生成开启，见 [Id 生成](./id-generation.md)）；连带拉入 Auth / Domain / UniversalId / Uow / EventBus.Adaptive。
- `CikeDbContext` 实现了 `IScopedDependency` → 具体 DbContext 类所在程序集被模块树加载时**自动 Scoped 注册**（前提：该项目有自己的模块类）；这就是为什么 DbContext 构造函数必须保留 `(DbContextOptions<T>, IServiceProvider)` 双参且泛型参数必须是自身。
- 所有仓储写操作经 ChangeTracker 钩子获得自动化：`Add`/`InsertAsync` 调用瞬间即生成 Id、写审计、写 TenantId、开事务（不等 SaveChanges）；软删转换与领域事件入队发生在 SaveChanges。
- `GetQueryable()` / `WithDetails()` 返回的查询已带软删/多租户全局过滤器，默认跟踪。
- 所有列表/分页查询统一汇入虚方法 `ToListAsync` / `ToPagedListAsync` 提交——自定义仓储只需重写这两个出口，即可影响全部 `GetListAsync` / `GetPagedListAsync`（含换提交方式、加全局拦截等）。
- `CikeDbContextOptions.DefaultConfigureAction` 为空（既没引方言包也没手动 Configure）时，DbContext 构造会因"无 provider"在首次解析时失败——**本包自身不含任何数据库方言**，方言必须来自 Provider 包或手动 `Configure`（测试里常用 `UseSqlite`）。

### 示例

**DbContext + 模块注册**：

```csharp
// 1. DbContext：泛型参数必须是自身；构造函数保留双参
public class CqrsDbContext(DbContextOptions<CqrsDbContext> options, IServiceProvider serviceProvider)
    : CikeDbContext<CqrsDbContext>(options, serviceProvider)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Buyer> Buyers => Set<Buyer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);   // 框架约定配置 + 全局过滤器在此发生，必须先调
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CqrsDbContext).Assembly);
    }
}

// 2. EFCore 层模块：注册 DbContext（默认仓储随之可用）
public class CQRSEntityFrameworkCoreModule : CikeModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddCikeDbContext<CqrsDbContext>();
        return base.ConfigureServicesAsync(context);
    }
}

// 3. 连接串（appsettings.json）：key = "CqrsDbContext" 去掉 "DbContext" = "Cqrs"
// { "ConnectionStrings": { "Cqrs": "server=...;database=cqrs;..." } }
```

**默认仓储使用**：

```csharp
public class OrderAppService(IRepository<Order, long> orders)
{
    public async Task DemoAsync(long id, IPagedAndSortedRequest page)
    {
        var order = await orders.GetAsync(id);      // 未找到抛 UserFriendlyException
        var maybe = await orders.FindAsync(id);     // 未找到返回 null
        var list = await orders.GetListAsync(o => o.Status == OrderStatus.Pending);
        var result = await orders.GetPagedListAsync(page, o => o.OrderNo.StartsWith("ORD"));
        var created = await orders.InsertAsync(new Order { ... });  // autoSave 默认 true
        await orders.UpdateAsync(created);
        await orders.DeleteAsync(id);                // 软删实体自动转软删；不存在静默返回
    }
}
```

**复杂查询出口**（仓储方法不够用时）：

```csharp
// 任意 LINQ——软删/多租户过滤器依然生效
var queryable = repository.GetQueryable().Where(o => o.TotalAmount > 100);

// 指定导航预加载
var withLines = repository.WithDetails(o => o.Lines);
var order = await withLines.FirstOrDefaultAsync(o => o.Id == id);

// 查询侧关闭跟踪（Dispose 恢复）
using (repository.BeginAsNoTracking())
{
    var dtos = await repository.GetListAsync();
}
```

**自定义仓储**（覆盖默认，零配置；完整写法）：

```csharp
// 业务接口：仓储能力 + 领域语义方法
public interface IOrderRepository : IRepository<Order, long>
{
    Task<long> CountPendingAsync();
}

// 实现：继承 EfCoreRepository 获得全部默认能力；标注 IScopedDependency 走约定注册，
// 约定注册先于 AddCikeDbContext 的 TryAdd 默认注册 → 天然接管同实体的
// IRepository / IBasicRepository / IReadOnlyRepository 三个接口的解析
public class OrderRepository(CqrsDbContext dbContext)
    : EfCoreRepository<CqrsDbContext, Order, long>(dbContext), IOrderRepository, IScopedDependency
{
    public Task<long> CountPendingAsync() => GetCountAsync(o => o.Status == OrderStatus.Pending);
}
```

**autoSave 配合工作单元**：

```csharp
using var scope = serviceProvider.CreateScope();
var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

await repository.InsertAsync(order, autoSave: false);   // 变更挂起，未落库
await repository.InsertAsync(line, autoSave: false);
await unitOfWork.CommitAsync();   // 保存 + 发布领域事件 + 提交事务
```

### 配置

| 配置项 | 位置 | 默认值 | 说明 |
|---|---|---|---|
| 连接串 | appsettings.json `ConnectionStrings:{名}` | — | 名 = DbContext 类名去掉 `DbContext`，或 `[ConnectionStringName]` 覆盖 |
| 方言 | `services.Configure<CikeDbContextOptions>(o => o.Configure(ctx => ...))` | 无（需方言包或手动配置） | `ctx`（`CikeDbContextConfigurationContext`）暴露 `ConnectionString` / `ConnectionStringName` / `DbContextOptionsBuilder` |
| 默认仓储 | `AddCikeDbContext<T>(addDefaultRepositories: false)` | `true` | 关闭后仅自定义仓储可用 |
| 事务 | `UnitOfWorkOptions.Enable` | `true`（`CikeUowModule` 强制置 true） | 见 [Cike.Uow](#cikeuow) |
| 雪花 | `CikeSnowflakeOptions.IsEnable` | 本模块置 `true` | 见 [Id 生成](./id-generation.md) |

### 边界与反模式

- 【陷阱】**不要用 `AddDbContext` / `AddDbContextPool`**——会绕开连接串解析与 UoW 集成；启用 UoW（默认）时首个实体状态变化直接抛 `InvalidOperationException`（提示改用 `AddCikeDbContext`）。
- 【已知技术债】领域层存在**两份同名聚合根接口**（`Cike.Data.IAggregateRoot` 与 `Cike.Data.Domain.AggregateRoots.IAggregateRoot`）：实体基类实现后者；`CikeDbContext.EnqueueDomainEventAsync` 也只判定后者——只实现契约层接口的实体，其领域事件**不会被入队**。统一它们是破坏性变更，待规划。
- 【已知技术债】`ToPaginationAsync`：`Page = 0` / 负数 → 偏移量未定义（`Skip((Page-1)*PageSize)` 可能为负，provider 报错）；`Sorting` 为空时无排序（分页内容不确定）；计数与 Skip/Take 路径丢弃 `CancellationToken`；`IQueryable.GetAsync` 扩展与仓储 `GetAsync` 双轨同形（优先用仓储方法）。
- 【已知技术债】`DeleteAsync(TKey id)` 的"不存在静默返回"存在**并发窗口**：Find 与 Save 之间被并发删除会抛并发异常；框架尚无统一并发异常翻译层。另：被软删/租户过滤挡住的实体同样"静默返回"。
- **IQueryable 生命周期**：查询挂在 Scoped DbContext 上，scope 释放后不可再物化；仓储是 Scoped 服务，不能构造注入进 Singleton（MinimalAPI 端点类是 Singleton——用 `[FromServices]` 方法参数注入，见 [HTTP 接口层](./http-api.md)）；取消令牌在物化处传入。
- 同步 `SaveChanges` 不入队领域事件（只有 `SaveChangesAsync` 入队）；领域事件入队后即 `ClearDomainEvents`（防重复入队/死循环）。
- `IMultiTenant.TenantId` 在跟踪时**无条件覆盖**为环境租户值——无法在 Add 前手工指定 TenantId；跨租户数据构造需先 `ICurrentTenant.Change(tenantId)` 再操作。
- 审计时间戳用 `DateTime.Now`（服务器本地时间，非 UTC）；`ICurrentUser.Id` 非 long 可解析（如 GUID 字符串）时 `CreatedBy/UpdatedBy` 静默写 0。
- `UpdateAsync(entity)` 对游离实体是 **Attach + Modified 全量更新**（所有列都写）；不是差量更新。
- 业务配置里调用 `HasQueryFilter` 会整体替换框架过滤器（EF Core 语义）——自定义过滤器需自行并入软删/租户条件，或避免使用。
- 未实现 `IEntity` 的 DbSet 实体：不参与约定配置（无审计/软删/租户自动化）、不注册默认仓储。
- 软删语义：Deleted 条目实现 `ISoftDelete` 即转软删（含被级联标记为 Deleted 的子实体）；未实现 `ISoftDelete` 的子实体被**硬删**；OwnsOne 值对象随主体保留。多租户过滤器读 `CurrentTenant.Id`（`ICurrentTenantAccessor`，AsyncLocal），与 TenantId 写入同源——不调用 `UseMultiTenant()` 时恒为 0，见 [认证与当前用户](./auth.md)。
- 多 DbContext：`IUnitOfWork` 注册用 `AddScoped`（非 TryAdd），后注册者胜出；`CikeDbContextOptions` 是**全局单委托**——多库场景需在一个 `Configure` 委托内按 `ctx.ConnectionStringName` 分支；默认仓储跨 DbContext 冲突时先注册者保留，需要特定实现写自定义仓储。
- `BeginAsNoTracking` 是仓储实例上的可变标志：跟踪行为在 `GetQueryable()` 调用时固化——在 using 块外创建的 IQueryable 仍保持跟踪查询。
- DbContext 所在程序集必须有模块类被模块树加载，否则 DbContext 不被约定注册、无法解析。

## Cike.Data.EFCore.MySql

### 定位

MySQL（Pomelo `Pomelo.EntityFrameworkCore.MySql` 8.0.2）方言 Provider——**依赖本模块即完成数据库方言配置**，业务侧无需再写任何 provider 配置。什么时候不用它：用 SQL Server 换 `.SqlServer` 包；SQLite 等其他方言没有 Provider 包，需在应用模块手动 `Configure`；不要与 `.SqlServer` 同时引用（见边界）。

### 能力清单

| 类型 | 签名 / 行为 |
|---|---|
| `CikeDataEFCoreMySqlModule` | `[DependsOn(CikeDataEFCoreModule)]`；`ConfigureServicesAsync`：① `CikeSequentialGuidGeneratorOptions.DefaultSequentialGuidType` **仅当为 null 时**设为 `SequentialGuidType.SequentialAsString`（MySQL 字符串主键排序友好）② `Configure<CikeDbContextOptions>(options => options.UseMySQL())` |
| `UseMySQL`（`Cike.Data.EFCore.Extensions`） | `public static CikeDbContextOptions UseMySQL(this CikeDbContextOptions options, Action<MySqlDbContextOptionsBuilder>? mySQLOptionsAction = null)`——内部 `UseMySql(ctx.ConnectionString, ServerVersion.AutoDetect(ctx.ConnectionString), b => { b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery); mySQLOptionsAction?.Invoke(b); })` |

即依赖后的默认行为：`UseMySql` + **SplitQuery**（`QuerySplittingBehavior.SplitQuery`）+ 顺序 Guid `SequentialAsString`。

### 隐式行为

- 依赖本模块 = 完成 MySQL 方言配置：不需要在任何地方写 `UseMySql`、连接串由框架解析后传入。
- `SequentialGuidType` 默认值写入带条件守卫（`== null` 才写）：更早执行的模块设置过则不覆盖；应用模块（后执行）仍可改。
- 模块执行顺序（源码验证）：模块按依赖图**后序遍历**排序（被依赖者在前、启动模块最后），`ConfigureServicesAsync` 按列表串行执行——Provider 模块先执行、应用模块后执行。`CikeDbContextOptions.Configure` 是**单委托、后设置者整体覆盖**。

**【应用模块 Configure 是否会被 Provider 模块覆盖？】不会，方向恰好相反**：应用模块后执行，其 `Configure<CikeDbContextOptions>(...)` 会**整体替换** Provider 的默认委托。因此：
- 应用模块不 Configure → 用 Provider 默认（UseMySQL + SplitQuery）。
- 应用模块 Configure → 必须自带完整 provider 配置；推荐再次调用 `options.UseMySQL(action)`（扩展内部已含 `UseMySql + SplitQuery`），不要裸调 `options.Configure(ctx => ...)` 只写零散选项（会把 `UseMySql` 丢掉）。
- 旧版本包模块顺序相反（应用的 Configure 被 Provider 静默覆盖）——升级时以本行为为准。

### 示例

```csharp
// 1. 常规用法：依赖即完成方言配置，无需任何 provider 代码
[DependsOn(typeof(CikeDataEFCoreMySqlModule))]
public class HostModule : CikeModule { }

// appsettings.json：
// { "ConnectionStrings": { "Cqrs": "server=...;database=cqrs;..." } }   // key = DbContext 类名去掉 DbContext

// 2. 追加自定义 MySQL 选项（整体替换默认配置，用扩展保持完整）
public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
{
    context.Services.Configure<CikeDbContextOptions>(options =>
    {
        options.UseMySQL(b => b.CommandTimeout(30));
    });
    return base.ConfigureServicesAsync(context);
}

// 3. 规避 ServerVersion.AutoDetect 连库探测（性能）：完整替换为静态版本号
public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
{
    context.Services.Configure<CikeDbContextOptions>(options =>
    {
        options.Configure(ctx => ctx.DbContextOptionsBuilder.UseMySql(
            ctx.ConnectionString,
            new MySqlServerVersion(new Version(8, 0, 36)),
            b => b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
    });
    return base.ConfigureServicesAsync(context);
}
```

### 配置

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `UseMySQL` 方言 | 依赖即生效 | 含 SplitQuery |
| `CikeSequentialGuidGeneratorOptions.DefaultSequentialGuidType` | `SequentialAsString`（仅当未设置） | 全局兜底为 `SequentialAtEnd`（见 [Id 生成](./id-generation.md)） |
| 连接串 | — | appsettings.json `ConnectionStrings:{DbContext 类名去掉 DbContext}`，严格匹配 |

### 边界与反模式

- `ServerVersion.AutoDetect(connectionString)` 在**每次构建 DbContextOptions 时都会连库探测版本**（`DbContextOptions<T>` 是 Transient，随每个 Scope 的 DbContext 创建各发生一次）——每个请求多一次数据库往返。生产环境建议用静态 `MySqlServerVersion` 完整替换（见示例 3）。
- 默认启用 SplitQuery（与 SqlServer 包一致）——多表 Include 拆成多条 SQL，注意事务一致性由同一 DbContext 事务保证。
- 应用模块 Configure 会整体覆盖 Provider 默认（见隐式行为）——替换时必须写完整配置。
- 同时引用 MySql 与 SqlServer 两个 Provider：`CikeDbContextOptions` 单委托，后执行的模块胜出（整体覆盖）；`SequentialGuidType` 因条件守卫，先执行者写入、后者跳过——谁先执行取决于应用模块 `[DependsOn]` 的声明顺序，不要依赖此行为，选一个。

## Cike.Data.EFCore.SqlServer

### 定位

SQL Server（`Microsoft.EntityFrameworkCore.SqlServer` 8.0.5）方言 Provider，与 [Cike.Data.EFCore.MySql](#cikedataefcoremysql) 同构——**依赖本模块即完成数据库方言配置**。什么时候不用它：MySQL 用 `.MySql` 包；其他方言无 Provider 包，需手动 `Configure`；不要与 `.MySql` 同时引用。

### 能力清单

| 类型 | 签名 / 行为 |
|---|---|
| `CikeDataEFCoreSqlServerModule` | `[DependsOn(CikeDataEFCoreModule)]`；`ConfigureServicesAsync`：① `CikeSequentialGuidGeneratorOptions.DefaultSequentialGuidType` **仅当为 null 时**设为 `SequentialGuidType.SequentialAtEnd`（SQL Server 主键排序友好）② `Configure<CikeDbContextOptions>(options => options.UseSqlServer())` |
| `UseSqlServer`（`Cike.Data.EFCore.Extensions`） | `public static CikeDbContextOptions UseSqlServer(this CikeDbContextOptions options, Action<SqlServerDbContextOptionsBuilder>? mySQLOptionsAction = null)`（参数名 `mySQLOptionsAction` 为复制粘贴遗留，无碍）——内部 `UseSqlServer(ctx.ConnectionString, b => { b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery); action?.Invoke(b); })` |

即依赖后的默认行为：`UseSqlServer` + **SplitQuery** + 顺序 Guid `SequentialAtEnd`。

### 隐式行为

- 依赖本模块 = 完成 SQL Server 方言配置，无需任何 provider 代码；连接串由框架解析传入。
- `SequentialGuidType` 条件默认：仅当未设置时写 `SequentialAtEnd`（与 `CikeSequentialGuidGeneratorOptions.GetDefaultSequentialGuidType()` 的全局兜底值一致，见 [Id 生成](./id-generation.md)）。
- 模块顺序与覆盖语义与 MySql 包完全一致（源码验证）：Provider 模块先执行、应用模块后执行；应用模块的 `Configure<CikeDbContextOptions>` **不会被 Provider 覆盖**，而是整体替换 Provider 默认——替换时须写完整配置（推荐 `options.UseSqlServer(action)` 而非裸 `Configure`）。旧版本包顺序相反，升级注意。

### 示例

```csharp
// 1. 常规用法：依赖即完成方言配置
[DependsOn(typeof(CikeDataEFCoreSqlServerModule))]
public class HostModule : CikeModule { }

// appsettings.json：
// { "ConnectionStrings": { "Cqrs": "Server=.;Database=cqrs;Trusted_Connection=True;TrustServerCertificate=True;" } }

// 2. 追加自定义选项（整体替换默认配置，用扩展保持完整）
public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
{
    context.Services.Configure<CikeDbContextOptions>(options =>
    {
        options.UseSqlServer(b => b.CommandTimeout(30));
    });
    return base.ConfigureServicesAsync(context);
}
```

### 配置

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `UseSqlServer` 方言 | 依赖即生效 | 含 SplitQuery |
| `CikeSequentialGuidGeneratorOptions.DefaultSequentialGuidType` | `SequentialAtEnd`（仅当未设置） | 与全局兜底一致 |
| 连接串 | — | appsettings.json `ConnectionStrings:{DbContext 类名去掉 DbContext}`，严格匹配 |

### 边界与反模式

- 默认启用 SplitQuery——包含多个 Include 的查询拆成多条 SQL，仍在同一事务内；行数放大敏感的查询注意。
- 应用模块 Configure 会整体覆盖 Provider 默认——替换时必须写完整配置（`UseSqlServer(connectionString, ...)` + SplitQuery 按需自带）。
- 同时引用 MySql 与 SqlServer：`CikeDbContextOptions` 单委托后设者胜出，`SequentialGuidType` 先执行者写入——不要同时引用，选一个。
- 与 MySql 包不同点仅两点：provider 调用本身、`SequentialGuidType` 默认值（`SequentialAtEnd` vs `SequentialAsString`）；SqlServer 无 `ServerVersion.AutoDetect` 式的连库探测开销。

## Cike.Uow

### 定位

工作单元抽象：事务边界契约（`IUnitOfWork` + `UnitOfWorkOptions` + `UnitOfWorkCommitState`）。**接口在本包，实现在 Cike.Data.EFCore 的 `EFCoreUnitOfWork<TDbContext>`**，由 `AddCikeDbContext<T>()` 注册——单独依赖本包得不到任何可运行的事务能力。什么时候不用它：命令/事件 Handler 内**不要手动开事务**——事件管道的事务中间件（`DbTransactionLocalEventMiddleware`，OncePerTree）已在每次根 `PublishAsync` 包裹 Commit/Rollback（见 [事件与 CQRS](./events-cqrs.md)）；只有脱离事件管道的场景（控制台、后台任务、种子数据、测试）才手动驱动。

### 能力清单

```csharp
public interface IUnitOfWork
{
    Guid TransactionId { get; }                 // Begin 前为 Guid.Empty
    IDbTransaction DbTransaction { get; }       // 未开事务时访问抛 NullReferenceException
    bool IsTransactionBegun { get; }
    UnitOfWorkCommitState CommitState { get; }  // 初始 Unknown

    Task BeginTranscationAsync(IsolationLevel? isolationLevel = default, CancellationToken ct = default);  // 方法名拼写即如此（Transaction 少个 a）
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

public class UnitOfWorkOptions
{
    public bool Enable { get; set; } = true;    // true：实体状态变化时 CikeDbContext 自动开事务
}

public enum UnitOfWorkCommitState { Unknown = 0, Uncommitted, Committed, Rollbacked }
```

| 成员 | 语义 |
|---|---|
| `BeginTranscationAsync(IsolationLevel?)` | 幂等（已开则直接返回）；开 `DbContext` 事务，置 `IsTransactionBegun = true`、`CommitState = Uncommitted` |
| `CommitAsync` | **先排空领域事件队列**：`while (IQueueEventBus.AnyQueueAsync()) { PublishQueueAsync(); SaveChangesAsync(); }`（handler 内的新领域事件经 SaveChanges 再入队，循环至空）；然后仅当 `CommitState == Uncommitted` 时 `SaveChangesAsync` + 提交事务，置 `Committed` |
| `RollbackAsync` | 仅当 `CommitState == Uncommitted` 时回滚事务，置 `Rollbacked` |

谁在用它（事务边界全图）：
- `CikeDbContext`——跟踪到实体状态变化且 `UnitOfWorkOptions.Enable = true` 时自动 `BeginTranscationAsync`（首个变化开启，之后复用）。
- `EFCoreUnitOfWork.CommitAsync`——提交前先 drain 领域事件队列（`IQueueEventBus`），handler 与业务写同事务。
- `DbTransactionLocalEventMiddleware`（Cike.EventBus.Local）——每次根 `PublishAsync` 包一个事务：`next()` 成功 → `CommitAsync`；异常 → `RollbackAsync` 后重抛。

### 隐式行为

- `CikeUowModule` 无依赖，`ConfigureServicesAsync` 强制 `UnitOfWorkOptions.Enable = true`——要关闭须在依赖了本模块的**更晚执行**的模块里 `Configure<UnitOfWorkOptions>(o => o.Enable = false)`（Options 的 Configure 动作按注册顺序叠加，后者生效）。
- 事务的实际开启者是 `CikeDbContext`（自动）或事件中间件——`IUnitOfWork` 注册为 Scoped，每个 Scope 一个，`DbContext` 惰性解析。
- `Enable = false` 时：不自动开事务；但 `CommitAsync` 仍会排空事件队列并 `SaveChanges`（无事务提交）。
- 领域事件在**事务提交前**发布：handler 抛异常 → 异常穿出 `CommitAsync` → 中间件 `RollbackAsync` 回滚整包（含业务写）。

### 示例

```csharp
// 常规请求流无需手写——事件管道的事务中间件已覆盖。
// 仅脱离事件管道的场景（控制台/后台任务/种子数据）手动驱动：
using var scope = serviceProvider.CreateScope();
var repository = scope.ServiceProvider.GetRequiredService<IRepository<Order, long>>();
var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

await unitOfWork.BeginTranscationAsync(IsolationLevel.ReadCommitted);  // 注意方法名拼写（源码如此）
try
{
    var order = await repository.InsertAsync(new Order { ... }, autoSave: false);
    order.Pay();                                                        // AddDomainEvent
    await unitOfWork.CommitAsync();   // drain 领域事件队列（handler 同事务执行）→ SaveChanges → 提交
}
catch
{
    await unitOfWork.RollbackAsync();
    throw;
}
```

### 配置

| 配置项 | 默认值 | 说明 |
|---|---|---|
| `UnitOfWorkOptions.Enable` | `true`（`CikeUowModule` 强制置 true） | `false`：实体状态变化不再自动开事务（`CommitAsync` 仍保存 + 发布事件）；无 appsettings 绑定，只能代码 `Configure` |
| 隔离级别 | `BeginTranscationAsync(IsolationLevel?)` 参数，不传用数据库默认 | — |

### 边界与反模式

- 【注意】`CommitState == Unknown`（从没碰过数据库/没开事务）时 `CommitAsync` / `RollbackAsync` 是**空操作**，不报错；`Committed` 后再 `RollbackAsync` 同样无效。
- `DbTransaction` getter 为 `DbContext.Database.CurrentTransaction!.GetDbTransaction()`——未开事务时访问抛 `NullReferenceException`（且会顺带惰性创建 DbContext），先判 `IsTransactionBegun`。
- 领域事件的排空发生在 `CommitAsync` 内部、事务提交**之前**——handler 的写与业务写同事务；但 handler 内异常会把整包回滚，依赖外部副作用的 handler 注意补偿（见 [事件与 CQRS](./events-cqrs.md) 的 Saga 补偿）。
- `IUnitOfWork` 按非泛型接口注册（`AddScoped`）——多 DbContext 场景后注册者胜出，无法按 DbContext 定向解析；需要精确控制某库事务时直接用该 DbContext 的 `Database` API 或拆分模块。
- 事务在实体**状态变化时**（而非 SaveChanges 时）开启——纯插入场景 `Add` 调用即占住连接上的事务；长事务风险由调用方控制（尽快 Commit）。
- 不要在 Handler 内手动 `BeginTranscationAsync` + `CommitAsync`——与中间件的事务包裹重复（`Begin` 幂等所以不炸，但提交时机会被打乱）；事务管理交给管道。
