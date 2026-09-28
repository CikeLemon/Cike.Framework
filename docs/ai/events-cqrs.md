# 事件与 CQRS

## 域定位

本域用一套机制解决四件事：**用例派发**（Command/Query）、**领域事件**（聚合根状态变化 → 同事务联动其他聚合/服务）、**Saga 补偿**（多步骤流程失败后执行取消 handler）、**后台任务**（不阻塞发布方、无事务保障的异步处理）。

核心设计：**CQRS 无独立 Dispatcher**。`Command` / `Query<TResult>` 本身就是事件（继承 `Event`），派发复用本地事件总线 `ILocalEventBus.PublishAsync`——事务包裹、重试、补偿、中间件管线全部来自事件机制，Cqrs 包只提供基类。后果：一个 Command 可以被 0 个或多个 `[LocalEventHandler]` 处理（无 single-handler 强制约束），执行顺序与失败语义由 `[LocalEventHandler]` 参数控制。

写路径完整链路（一次 Command 发布）：

```
HTTP 端点方法（Service 层）
 └─ ILocalEventBus.PublishAsync(command)                   根发布：装配中间件栈
     └─ DbTransactionLocalEventMiddleware（最外层）         事务边界
         └─ ExceptionLocalEventMiddleware                  状态跟踪
             └─ PreventRecursiveMiddleware                 树结束重置发布计数
                 └─ CommandHandler（[LocalEventHandler]）
                     ├─ 聚合根业务方法内 AddDomainEvent(new XxxEvent(...))
                     └─ repository.InsertAsync（autoSave:true）→ CikeDbContext.SaveChangesAsync
                         └─ 聚合根 DomainEvents 逐个 EnqueueAsync 到 IQueueEventBus，随后清空防重复
     └─（handler 全部成功）DbTransactionMiddleware → IUnitOfWork.CommitAsync
         └─ while (AnyQueueAsync())                        提交前 drain 领域事件队列
             └─ PublishQueueAsync → PublishAsync(领域事件)
                 └─ 领域事件 [LocalEventHandler] handler 同一事务内执行
         └─ SaveChanges + CommitTransactionAsync           COMMIT（任一环节失败 → 整体回滚，异常抛回调用方）
```

要点：

- 事务在实体首次被跟踪/修改时由 `CikeDbContext` 自动开启（见[数据访问](./data-access.md)），**提交只能由 `IUnitOfWork.CommitAsync` 完成**——即根发布的事务中间件。绕过 `PublishAsync` 直接写仓储且不手动 `CommitAsync`，事务悬置并在 DbContext 释放时回滚（数据不落库）。
- 领域事件链路：`聚合根 AddDomainEvent → SaveChangesAsync 入队 IQueueEventBus → CommitAsync 提交前逐个发布`，详见 Cike.EventBus.Adaptive 章节。
- 嵌套发布（handler 内再 `PublishAsync`）不重新装配事务中间件，共享树根事务。

## 覆盖的包

| 包 | 职责 | [DependsOn] 直接依赖 |
|---|---|---|
| Cike.EventBus | 事件契约与总线抽象（`IEvent` / `Event` / `IBackgroundEvent` / `IEventBus` / `IQueueEventBus`），无实现 | 无 |
| Cike.EventBus.Local | 本地事件总线（`ILocalEventBus`）、`[LocalEventHandler]` 扫描注册、中间件管线、Saga 策略、后台 Channel | Cike.EventBus |
| Cike.EventBus.Adaptive | `IEventBus` 转发实现（`EventBusAdaptive`）+ 领域事件队列（`QueueEventBus : IQueueEventBus`） | Cike.EventBus |
| Cike.Cqrs | Command / Query 用例基类 | Cike.EventBus |

模块加载顺序（被依赖者在前）：`Cike.EventBus → Cike.EventBus.Local → Cike.EventBus.Adaptive / Cike.Cqrs`。

【陷阱】Adaptive 的模块只声明依赖 `CikeEventBusModule`，但其实现（`QueueEventBus` 构造注入 `ILocalEventBus`）**需要 `CikeEventBusLocalModule` 一起加载**——自动 DI 只扫描已加载模块的程序集，Local 不在依赖树里则 `ILocalEventBus` 无注册，解析 `IQueueEventBus` 时抛异常。业务应用在 Application 层模块显式 `[DependsOn(typeof(CikeEventBusLocalModule))]`。另：`Cike.Data.EFCore` 的模块依赖 `CikeEventBusAdaptiveModule`，引入 EF Core 即自动带上 Adaptive，Local 仍需业务侧显式引入。

## 章节目录

- [Cike.EventBus](#cikeeventbus)
- [Cike.EventBus.Local](#cikeeventbuslocal)
- [Cike.EventBus.Adaptive](#cikeeventbusadaptive)
- [Cike.Cqrs](#cikecqrs)

## Cike.EventBus

### 定位

事件体系的契约层：事件类型、标记接口与总线抽象，不含任何实现。所有事件（业务事件、Command、Query、领域事件）都从这里出发。

### 能力清单

| 类型 / 成员 | 签名与说明 |
|---|---|
| `IEvent` | 事件契约：`string GetEventId()` / `SetEventId(string)` / `DateTime GetCreationTime()` / `SetCreationTime(DateTime)` |
| `Event` | 抽象 record 基类：构造时自动生成事件 Id（Guid 字符串）与创建时间（`DateTime.Now`，本地时间非 UTC）；`virtual bool IsBackgroundThread()` / `virtual void EnableBackgroundThread()`；实现 `IBackgroundEvent` |
| `Event<TResult>` | 带结果的事件：`TResult? Result { get; set; }` 由 handler 赋值、调用方读取 |
| `IEvent<TResult>` | `Event<TResult>` 的契约（含 `Result`） |
| `IBackgroundEvent` | 后台事件标记接口：`bool IsBackgroundThread()` / `void EnableBackgroundThread()`。**进入后台通道的判定条件是实现此接口**——`Event` 基类已实现它，所有 `Event` 派生事件均满足；实际开关是 `IsBackgroundThread()`（见 Local 章节陷阱） |
| `IEventBus` | `Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent` |
| `IQueueEventBus : IEventBus` | 领域事件队列：`Task EnqueueAsync<TEvent>(TEvent @event)` / `Task PublishQueueAsync()` / `Task<bool> AnyQueueAsync()`（实现在 Adaptive 包） |
| `CikeEventBusModule` | 模块类，无逻辑、无依赖 |

### 隐式行为

- 该包只定义契约，无任何运行时行为——`PublishAsync` 的实际语义由 Local/Adaptive 包赋予。
- 继承 `Event` 即自动获得事件 Id 与创建时间。

### 示例

```csharp
using Cike.EventBus;

// 无结果事件（业务事件 / Saga 事件 / 领域事件的基元形态）
public record InventoryChangedEvent(long ProductId, int Delta) : Event;

// 带结果事件：handler 给 Result 赋值
public record StockQueryEvent(long ProductId) : Event<int>;

var evt = new StockQueryEvent(42);
await eventBus.PublishAsync(evt);   // 同步语义下返回时 handler 已完成
var stock = evt.Result;             // 直接读取（可能为 null，见边界）
```

### 配置

无。

### 边界与反模式

- **`IBackgroundEvent` 与 `Event` 基类**：`Event` 基类已实现 `IBackgroundEvent`，所有 `Event` 派生事件都通过 `is IBackgroundEvent` 检查——是否进后台通道只取决于 `IsBackgroundThread()` 返回值（默认 `false` = 同步执行；调用 `EnableBackgroundThread()` 后 = 后台 Channel）。旧版框架基类不实现该接口、要求事件类型显式声明，升级时注意：过去调用了 `EnableBackgroundThread()` 却因未声明接口而静默同步执行的事件，现在会真正进后台通道，详见 Local 章节的关键陷阱。
- 事件创建时间是 `DateTime.Now`（服务器本地时间），不是 UTC——对时间敏感的逻辑不要直接依赖 `GetCreationTime()`。
- 不要在本包找 `PublishAsync` 的行为定义（静默忽略、事务等）——那是 Local 包的事。

## Cike.EventBus.Local

### 定位

本地事件总线：进程内**同步**事件派发（默认模式返回时 handler 已执行完毕），同时是框架 CQRS 的执行引擎。基于 Channel 提供可选的后台线程模式。

### 能力清单

| 类型 / 成员 | 签名与说明 |
|---|---|
| `ILocalEventBus : IEventBus` | `Task CancelAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : IEvent`——**ct 无默认值**；`PublishAsync` 继承自 `IEventBus` |
| `LocalEventBus` | 实现，`IScopedDependency`；路由规则：`is IBackgroundEvent && IsBackgroundThread()` → 写 Channel，否则同步执行 |
| `[LocalEventHandler]` | 标注在任意非抽象类的 public 实例方法上即注册为 handler。参数见下表 |
| `LocalEvent` | 空标记 record（`: Event`），无附加成员、无路由含义——业务事件可继承它或直接 `Event` |
| `BackgroundEvent` | 抽象 record（`: LocalEvent`），构造时调用 `EnableBackgroundThread()`；经由 `Event` 基类实现 `IBackgroundEvent`——继承它即默认路由到后台 Channel |
| `ILocalEventMiddleware<TEvent>` | 中间件契约：`Task HandleAsync(TEvent @event, EventHandlerDelegate next)`（`delegate Task EventHandlerDelegate()`）+ `MiddlewareExecutionPolicy ExecutionPolicy` |
| 内置中间件 ×3 | `DbTransactionLocalEventMiddleware`（事务）/ `ExceptionLocalEventMiddleware`（状态跟踪）/ `PreventRecursiveMiddleware`（重置发布计数），均 `OncePerTree`、Transient |
| `ILocalEventContext` | Scoped 状态上下文：`Counter` / `Status`（`ExecutorStatusEnum`）/ `Exception` / `Reset()` |
| `SagaStrategyExecutor` | `IStrategyExecutor` 实现（Singleton）：失败立即重试（无退避），共 `1 + RetryCount` 次尝试 |
| Channel 体系 | `IChannelPublisher`（Singleton）/ `IChannelWarpper<T>`（每事件类型一个 Channel）/ `ChannelOptionsManager` / `LocalEventBusOptions` |
| `CikeEventBusLocalModule` | 模块类：构建 handler 关系容器（Singleton，启动期）、注册中间件与 Channel 服务 |

`[LocalEventHandler]` 参数表（源码 `LocalEventHandlerAttribute`）：

| 参数 | 类型 | 默认值 | 语义 |
|---|---|---|---|
| `order`（构造参数）/ `Order` | int | 100 | 同一事件多个 handler 按 Order **升序**执行；取消 handler 同样升序 |
| `IsCancel` | bool | false | `true` = 注册为取消/补偿 handler，不参与正常执行 |
| `RetryCount` | int | 0 | 失败立即重试次数（无退避间隔），共 `1 + RetryCount` 次尝试 |
| `FailureLevel` | FailureLevelEnum | `Throw` | 见下表 |

`FailureLevelEnum` 语义（重试耗尽后）：

| 值 | 行为 |
|---|---|
| `Throw`（默认） | 执行补偿（范围：`Order ≤ 失败 handler 的 Order − 1`，即只补偿先于失败步骤的取消 handler，不含失败步骤自身），然后抛出原异常，后续 handler 不再执行 |
| `ThrowAndCancel` | 同 Throw，但补偿范围含失败步骤自身（`Order ≤ 失败 handler 的 Order`）——用于"失败步骤可能已产生部分副作用、需要自身清理"的场景 |
| `Ignore` | 仅 LogError，不抛异常、不补偿，**继续执行后续 handler** |

### 隐式行为

**Handler 发现与注册**
- 模块配置期扫描**所有已加载模块的程序集**（框架 + 业务模块；无模块类的纯类库不被扫描），构建 `LocalEventHandlerRelationContainer`（Singleton）。
- 含 `[LocalEventHandler]` 方法的类自动 `AddScoped`（注册具体类型本身，无需标记接口）。
- 违反约束**启动即失败**（快失败）：方法参数中 `IEvent` 派生类型参数有且仅有一个（0 个或 ≥2 个 → `ArgumentException`）；返回值必须是 `Task` 或 `void`（`Task<T>` / `ValueTask` → `NotSupportedException`）。

**Handler 方法参数解析**
- 事件参数：按**精确类型匹配**（handler 参数类型 = 发布的事件类型；声明为基类型不命中）。
- `CancellationToken` 参数自动传入。
- 其余参数从 DI `GetRequiredService` 解析（未注册直接抛错）。

**执行语义**
- 正常 handler 与取消 handler 都按 `Order` 升序执行；同 Order 时保持注册顺序。
- handler 失败（重试耗尽、`FailureLevel != Ignore`）：置状态上下文异常 → 执行补偿 → 异常重新抛出，**中断后续 handler**。
- 无 handler 注册的事件类型：`PublishAsync` 直接 return——不抛错、无警告日志（仅 Debug 级"Publishing event"），同步与后台路径行为一致。

**中间件管线**
- 注册顺序：DbTransaction → Exception → PreventRecursive（`TryAddEnumerable`，Transient）；执行时 `Reverse + Aggregate` 装配 → **先注册者在最外层**。
- `OncePerTree` 策略：仅"树根"发布（中间件提供者的发布计数为 0）装配 `OncePerTree` 中间件；嵌套发布（handler 内再 Publish）过滤掉它们——handler 直接执行、共享树根事务。树结束时 `PreventRecursiveMiddleware` 重置计数。
- 自定义中间件：实现 `ILocalEventMiddleware<TEvent>` 并在模块 `ConfigureServicesAsync` 中 `TryAddEnumerable` 注册；`ExecutionPolicy.Always` 的中间件每次发布都执行（含嵌套发布），位置取决于注册顺序。

**事务**
- 实体被跟踪/状态变化时 `CikeDbContext` 自动 `BeginTransactionAsync`（`UnitOfWorkOptions.Enable` 默认 true）——事务开启不依赖事件总线。
- 根发布的 `DbTransactionLocalEventMiddleware` 在事件树成功后调用 `IUnitOfWork.CommitAsync`（提交前 drain 领域事件队列，见 Adaptive 章节）；异常时 `RollbackAsync` 后重抛。

**后台事件（`IBackgroundEvent` 路由生效时）**
- `PublishAsync` 把事件写入对应事件类型的 Channel（Singleton）即返回，不等 handler。
- `ChannelWarpper<T>` 用 `Parallel.ForEachAsync` 消费（默认并行度 = CPU 核数），每条消息在**独立 DI Scope** 中执行——独立 DbContext / UoW / 中间件栈，即后台事件自己开自己的事务。
- 异常仅 LogError，不通知发布方、不参与发布方事务。
- 通道默认 `UnboundedChannelOptions`（内存无上限），可用 `LocalEventBusOptions` 配置默认或按事件类型覆盖。

### 示例

**Saga 补偿**（多步骤流程，外部副作用用补偿、数据库副作用靠事务回滚）：

```csharp
using Cike.EventBus.Local;
using Cike.Data.Domain.Repositories;

// 事件定义（Application.Contracts 层）
public record SubmitOrderEvent(long OrderId, long BuyerId, decimal Amount) : LocalEvent;

public class OrderSagaHandlers(IRepository<Order, long> orderRepository)
{
    // ── 正常步骤（Order 升序执行）──

    [LocalEventHandler(order: 10)]
    public Task LockStockAsync(SubmitOrderEvent @event, IInventoryClient inventory, CancellationToken ct = default)
        => inventory.LockAsync(@event.OrderId, ct);        // IInventoryClient 从 DI 解析（额外参数示例）

    [LocalEventHandler(order: 20, RetryCount = 2)]
    public Task FreezeAmountAsync(SubmitOrderEvent @event, CancellationToken ct = default)
        => accountClient.FreezeAsync(@event.BuyerId, @event.Amount, ct);

    [LocalEventHandler(order: 30)]
    public async Task CreateOrderAsync(SubmitOrderEvent @event, CancellationToken ct = default)
        => await orderRepository.InsertAsync(Order.Recreate(@event.OrderId), cancellationToken: ct);

    // ── 补偿（IsCancel = true；失败时按 Order 升序执行 Order ≤ 起点的取消 handler）──

    [LocalEventHandler(order: 10, IsCancel = true)]
    public Task UnlockStockAsync(SubmitOrderEvent @event, CancellationToken ct = default)
        => inventoryClient.UnlockAsync(@event.OrderId, ct);   // 释放外部系统的库存锁（数据库部分靠回滚）

    [LocalEventHandler(order: 20, IsCancel = true)]
    public Task UnfreezeAmountAsync(SubmitOrderEvent @event, CancellationToken ct = default)
        => accountClient.UnfreezeAsync(@event.BuyerId, @event.Amount, ct);
    // 若 FreezeAmount(20) 失败（默认 Throw）：只补偿 Order ≤ 19 → UnlockStock(10)。
    // 若失败步骤可能已产生部分副作用需自身清理：给该 handler 设 FailureLevel = ThrowAndCancel，
    // 补偿范围变为 Order ≤ 20，含 UnfreezeAmount(20) 自身。
}
```

**后台事件**（关键：必须显式实现 `IBackgroundEvent`，仅继承 `BackgroundEvent` 不生效）：

```csharp
// ✔ 正确：显式声明接口（Event 基类的虚方法恰好满足接口签名）
public record SendWelcomeSmsEvent(long BuyerId, string Phone) : BackgroundEvent, IBackgroundEvent;

// ✗ 错误：仅继承 BackgroundEvent —— is IBackgroundEvent 判定为 false，静默走同步（见边界）
public record SendWelcomeSmsEvent(long BuyerId, string Phone) : BackgroundEvent;

public class NotificationHandlers(ISmsClient smsClient)
{
    [LocalEventHandler]
    public Task SendAsync(SendWelcomeSmsEvent @event, CancellationToken ct = default)
        => smsClient.SendAsync(@event.Phone, $"Welcome, buyer {@event.BuyerId}!", ct);
}

// 发布方：PublishAsync 写入 Channel 即返回
await eventBus.PublishAsync(new SendWelcomeSmsEvent(dto.BuyerId, dto.Phone), ct);
```

**自定义事件中间件**：

```csharp
using Cike.EventBus;
using Cike.EventBus.Local.LocalEventMiddlewares;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public class AuditLocalEventMiddleware<TEvent> : ILocalEventMiddleware<TEvent> where TEvent : IEvent
{
    public MiddlewareExecutionPolicy ExecutionPolicy => MiddlewareExecutionPolicy.Always; // 嵌套发布也执行
    public async Task HandleAsync(TEvent @event, EventHandlerDelegate next)
    {
        // 前置逻辑（记录事件 Id / 耗时等）
        await next();
        // 后置逻辑
    }
}

// 模块 ConfigureServicesAsync 中注册（TryAddEnumerable 防重复）
context.Services.TryAddEnumerable(ServiceDescriptor.Describe(
    typeof(ILocalEventMiddleware<>), typeof(AuditLocalEventMiddleware<>), ServiceLifetime.Transient));
```

### 配置

无 appsettings 节（EventBus 系包不读配置文件），唯一入口是代码配置：

```csharp
// 业务模块 ConfigureServicesAsync
context.Services.Configure<LocalEventBusOptions>(options =>
{
    // 全部事件类型的后台通道默认配置（默认：UnboundedChannelOptions，容量不限）
    options.DefaultChannelOptions = new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.Wait,   // 写满时发布方等待（背压）
    };
    // 按事件类型覆盖
    options.ChannelOptionsManager.RegisterChannelOptions<SendWelcomeSmsEvent>(
        new BoundedChannelOptions(1_000) { FullMode = BoundedChannelFullMode.DropOldest });
});
```

| 项 | 默认值 |
|---|---|
| `DefaultChannelOptions` | `UnboundedChannelOptions`（无界） |
| 后台消费并行度 | `Parallel.ForEachAsync` 默认（CPU 核数） |
| `[LocalEventHandler] Order / IsCancel / RetryCount / FailureLevel` | 100 / false / 0 / `Throw` |

### 边界与反模式

1. **【关键】后台路由规则**：`PublishAsync` 的路由条件是 `is IBackgroundEvent && IsBackgroundThread()`。`Event` / `LocalEvent` / `BackgroundEvent` 基类都已实现 `IBackgroundEvent`（继承自 `Event`）→ 是否进后台只看 `IsBackgroundThread()`：继承 `BackgroundEvent`（构造即启用）或调用 `EnableBackgroundThread()` 都会进后台通道，两者都不做则同步执行。升级注意：旧版要求事件类型显式声明 `: IBackgroundEvent` 才生效，新版只调用 `EnableBackgroundThread()` 也会进后台——依赖旧行为（声明接口前静默同步）的业务需自查。
2. **静默忽略**：无 handler 的事件（忘标 `[LocalEventHandler]`、handler 参数写成基类型、事件类型改名）发布后无声无息。"发布没反应"时按这三点排查。
3. **不要依赖"提交失败自动补偿"**：`ExceptionLocalEventMiddleware` 的"已 Succeed 后异常 → 全量取消"分支依赖 `Status = Succeed`，但当前源码没有任何地方设置它（死代码）；且该中间件位于 DbTransaction 中间件**内层**，`CommitAsync` 的异常根本不会流经它——双重不可达。需要补偿语义时用 `FailureLevel = ThrowAndCancel` + `IsCancel` handler 在 handler 执行期完成。
4. **旧版包陷阱**：`FailureLevel` 默认值修正前的旧版 `Cike.EventBus.Local`，未显式设置 `FailureLevel` 且事件带取消 handler 时，失败补偿路径抛 `NotImplementedException`（`ComputeCancelList` 的 switch 落入 `_ => throw`）。引用旧版包时必须显式设置 `FailureLevel`。
5. **泛型静态类型**：`PublishAsync<TEvent>` 的 `TEvent` 取调用点静态类型——用基类型变量发布派生事件，handler（精确类型匹配）不命中，静默忽略。
6. **重试无退避**：`RetryCount` 重试是立即重试（无间隔）；抛 `UserFriendlyException` 的 handler 同样会被重试——业务校验失败别设 RetryCount。
7. **状态残留**：`ILocalEventContext.Reset()` 只清 Counter/Exception 不清 Status——同 Scope 内上一次发布的状态会残留到下一次。
8. **手动 `CancelAsync`**：事件类型无任何 handler 注册时直接 `KeyNotFoundException`（字典直接索引）；补偿 handler 按 Order **升序**执行（非倒序——与常见 Saga 直觉相反）。
9. **后台事件纪律**：消费是并发的（多线程同时执行同一事件的 handler），handler 必须幂等；`Unbounded` 默认下堆积无上限，量大时配 `BoundedChannelOptions`。
10. **绕过 PublishAsync 写库不落库**：直接调仓储写方法而不经过根发布（或手动 `CommitAsync`），事务悬置、DbContext 释放时回滚。
11. **不要注入裸 `IEventBus`**：Local/Adaptive 同时加载时有多个 `IEventBus` 注册（LocalEventBus/EventBusAdaptive/QueueEventBus），`GetRequiredService<IEventBus>` 取最后注册者——业务代码注入 `ILocalEventBus` 或 `IQueueEventBus`。

## Cike.EventBus.Adaptive

### 定位

自适应事件总线与**领域事件队列**。`EventBusAdaptive` 把 `IEventBus.PublishAsync` 转发本地总线（不做任何类型筛选）；`QueueEventBus` 提供进程内事件队列——聚合根上的领域事件先入队、在数据库事务提交前依次发布。

### 能力清单

| 类型 | 签名与说明 |
|---|---|
| `EventBusAdaptive : IEventBus` | `IScopedDependency`；`PublishAsync` 直接转发 `ILocalEventBus.PublishAsync` |
| `QueueEventBus : EventBusAdaptive, IQueueEventBus` | `IScopedDependency`；实例字段 `Queue<IEvent>`（**Scoped——每个 DI Scope 一份队列**） |
| `EnqueueAsync<TEvent>(TEvent @event)` | 入队（`IEvent` 约束） |
| `AnyQueueAsync() : Task<bool>` | 队列是否非空 |
| `PublishQueueAsync()` | 循环 `Dequeue` → `PublishAsync((dynamic)@event)`——`dynamic` 保证按**运行时具体类型**派发，命中具体类型的 handler |
| `CikeEventBusAdaptiveModule` | 模块类，无逻辑（运行时需要 Local，见"覆盖的包"陷阱） |

### 隐式行为

**领域事件完整链路**（与[数据访问](./data-access.md)协作）：

1. 聚合根（`AggregateRoot` / `FullAuditedAggregateRoot`）业务方法中 `AddDomainEvent(new XxxEvent(...))`；领域事件继承 `DomainEvent : Event`。
2. 仓储写方法（`autoSave: true`）→ `CikeDbContext.SaveChangesAsync`：遍历 ChangeTracker 中的聚合根，逐个 `EnqueueAsync` 到 `IQueueEventBus`，随后 `ClearDomainEvents()` 防重复入队。
3. 根发布的事务中间件 → `IUnitOfWork.CommitAsync`：
   ```
   while (await queueEventBus.AnyQueueAsync())
   {
       await queueEventBus.PublishQueueAsync();   // 逐个发布领域事件 → handler 同事务执行
       await DbContext.SaveChangesAsync();        // handler 的写操作（可能再产生领域事件 → 循环继续）
   }
   ```
4. 队列空后 `SaveChanges` + `CommitTransactionAsync`。实际 COMMIT 由**最内层**（领域事件树）的 `CommitAsync` 完成——根树的 `PreventRecursiveMiddleware` 在 drain 前已重置发布计数，领域事件发布构成新树根、重新装配事务中间件；外层 `CommitAsync` 见 `CommitState == Committed` 则跳过。等效语义：**全部 handler（含领域事件 handler）成功才提交，任一失败整体回滚**。
5. `IQueueEventBus` 在 `CikeDbContext` / UoW 中按可空解析（`GetService`）——Adaptive 未加载时静默跳过入队/发布（领域事件直接丢失，无警告；正常引入 `Cike.Data.EFCore` 即不会发生，它的模块依赖 Adaptive）。

### 示例

```csharp
using Cike.Data.Domain.AggregateRoots;   // DomainEvent / 聚合根基类
using Cike.Data.Domain.Entities;

// 领域事件（Domain 层）
public record OrderPlacedEvent(long OrderId, long BuyerId, decimal Amount) : DomainEvent;

// 聚合根：业务方法中登记事件（不要在聚合外部发布）
public class Order : FullAuditedAggregateRoot<long>
{
    public static Order Place(long buyerId, Address address, IEnumerable<OrderLine> lines)
    {
        var order = new Order { /* ... */ };
        order.AddDomainEvent(new OrderPlacedEvent(order.Id, buyerId, order.TotalAmount));
        return order;
    }
}

// 领域事件 handler（Application 层，与其他 handler 同一套写法）——与下单在同一事务内执行
public class BuyerEventHandler(IRepository<Buyer, long> buyerRepository)
{
    [LocalEventHandler]
    public async Task OnOrderPlacedAsync(OrderPlacedEvent @event, CancellationToken ct = default)
    {
        var buyer = await buyerRepository.FindAsync(@event.BuyerId, cancellationToken: ct)
                    ?? (await buyerRepository.InsertAsync(new Buyer(@event.BuyerId), cancellationToken: ct));
        buyer.AddOrder(@event.Amount);   // 失败 → 整个下单回滚（同事务）
    }
}

// 命令 handler 里只管聚合与仓储，事件在 CommitAsync 时自动发布
[LocalEventHandler]
public async Task CreateAsync(CreateOrderCommand command, CancellationToken ct = default)
{
    var order = Order.Place(command.BuyerId, command.Address, command.Lines);
    await orderRepository.InsertAsync(order, cancellationToken: ct);
}
```

### 配置

无（无 Options、无 appsettings 节）。

### 边界与反模式

1. **必须与 Local 一起加载**（详见"覆盖的包"陷阱）——`Cike.Data.EFCore` 只带 Adaptive，业务 Application 模块要显式依赖 Local。
2. **`autoSave: false` 陷阱**：领域事件入队发生在 `SaveChangesAsync`，而 `CommitAsync` 的 drain 检查先于最终 SaveChanges——`autoSave: false` 的写法其领域事件**错过本轮 drain**（延迟到同 Scope 下一次 CommitAsync，请求结束则丢失）。命令 handler 用默认 `autoSave: true`。
3. **同步 `SaveChanges()`（非 async 重载）不入队领域事件**——走 `SaveChangesAsync`。
4. **级联 drain 无循环上限**：聚合根每次保存都新增领域事件会死循环（`while (AnyQueueAsync())` 永不退出）。
5. 队列是 Scoped 实例字段：跨 Scope 不共享；后台事件不走队列。

## Cike.Cqrs

### 定位

CQRS 用例基类。**没有独立 Dispatcher**——Command/Query 的派发、事务、重试、补偿全部复用本地事件总线机制，本包只提供两个基类。

### 能力清单

| 类型 | 定义 |
|---|---|
| `Command : Event, ICommand` | 抽象 record；无返回值用例。输出数据用**可写属性**回传（handler 填充，`PublishAsync` 返回后调用方读取） |
| `Query<TResult> : Event<TResult>, IQuery<TResult>` | 抽象 record；有返回值用例，handler 给 `Result` 赋值 |
| `ICommand : IEvent` / `IQuery<TResult> : IEvent<TResult>` | 标记接口 |
| `CikeCqrsModule` | 模块类，无逻辑，依赖 `CikeEventBusModule` |

### 隐式行为

- Command/Query 即事件：继承 `Event` → 自动事件 Id / 创建时间；handler 用 `[LocalEventHandler]` 注册（Local 包）；派发 = `PublishAsync`；事务 / Saga / 后台 / 中间件全部复用事件总线语义。
- 同步执行保证（非后台事件）：`PublishAsync` 返回时 handler 已完成 → `command.Id` / `query.Result` 可直接读。
- 模块本身无服务注册、无逻辑。

### 示例

```csharp
using Cike.Cqrs;
using Cike.Data.Domain.Repositories;

// ── Application.Contracts 层 ──
public record CreateOrderDto(/* ... */);
public record OrderDto(/* ... */);

// Command：回传数据用可写属性（record 位置参数是 init-only，不能用于回传！）
public record CreateOrderCommand(CreateOrderDto Dto) : Command { public long Id { get; set; } }

// Query：handler 给 Result 赋值
public record GetOrderListQuery(string? Keyword, int Page, int PageSize) : Query<PagedResultDto<OrderDto>>;

// ── Application 层 ──
public class OrderCommandHandler(IRepository<Order, long> orderRepository)
{
    [LocalEventHandler]
    public async Task CreateAsync(CreateOrderCommand command, CancellationToken ct = default)
    {
        var order = Order.Place(command.Dto);       // 聚合工厂
        await orderRepository.InsertAsync(order, cancellationToken: ct);
        command.Id = order.Id;                      // 雪花 Id 在跟踪瞬间已生成，直接回传
    }
}

public class OrderQueryHandler(IReadOnlyRepository<Order, long> orderRepository)
{
    [LocalEventHandler]
    public async Task GetListAsync(GetOrderListQuery query, CancellationToken ct = default)
    {
        using (orderRepository.BeginAsNoTracking())  // 查询侧纪律：只读仓储 + 关跟踪
        {
            query.Result = await orderRepository.GetPagedListAsync(/* ... */);
        }
    }
}

// ── HTTP 端点（Service 层，MinimalAPI 端点类是 Singleton，ILocalEventBus 是 Scoped → [FromServices]）──
public class OrderService(ILocalEventBus? unused = null) : MinimalApiServiceBase
{
    public async Task<Results<Ok<long>, BadRequest<string>>> CreateAsync(
        [FromServices] ILocalEventBus eventBus, [AutoValidation] CreateOrderDto dto, CancellationToken ct = default)
    {
        var command = new CreateOrderCommand(dto);
        await eventBus.PublishAsync(command, ct);   // 返回时 handler 已完成（含领域事件 drain + COMMIT）
        return TypedResults.Ok(command.Id);
    }
}
```

### 配置

无。

### 边界与反模式

1. **不继承 `Command` / `Query<TResult>` 就不是 `IEvent`**：直接 `PublishAsync(new XxxCommand(...))` 会在调用点报泛型约束编译错误；若经基类型变量绕过，则运行时精确类型匹配不命中、**静默忽略**。排查"发布了没反应"先查继承链。
2. **回传数据必须用 `{ get; set; }` 可写属性**——record 位置参数是 init-only，handler 无法填充。
3. **一个 Command/Query 可挂多个 handler**（事件语义，无 single-handler 约束）——无意中写两个同事件 handler 会都执行，纪律上保持一用例一 handler。
4. **Query 不要进后台通道**——handler 异步执行时无人读 `Result`。
5. `query.Result` 可能为 null（handler 未赋值或无 handler）——读取前判空。
6. 命令参数校验发生在 HTTP 层（`[AutoValidation]` + FluentValidation 校验 Dto，见[HTTP 接口层](./http-api.md)）——Command 对象本身不自动校验。
