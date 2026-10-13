# 框架内核

## 域定位

模块系统、依赖注入约定、异常体系、DTO 与分页契约——Cike.Framework 的地基。任何业务项目从建解决方案第一天起就与它打交道：

- 新建每个项目要写一个模块类（`CikeModule` 子类）并用 `[DependsOn]` 组装
- 宿主 `Program.cs` 第一行 `AddApplicationAsync<T>()` 引导整个模块树
- 业务服务类靠标记接口自动注册，不手写 `services.AddXxx`
- 业务校验失败抛 `UserFriendlyException`
- 应用层契约（Dto、分页请求/响应）继承 `Cike.Contracts` 的基类

什么时候不涉及它：几乎没有——HTTP 路由、数据访问、事件/CQRS 等能力域全部构建在它之上。仅当你只写纯算法/工具库（不参与模块树）时可以不引用。

## 覆盖的包

| 包 | 职责 | 依赖的其它 Cike 包 |
|---|---|---|
| `Cike.Core` | 模块系统、DI 约定扫描、异常体系、字符串/类型/集合/JSON 工具 | 无（它是依赖链的根） |
| `Cike.Contracts` | DTO 基类、分页请求/响应契约 | `Cike.Data`（传递依赖 `Cike.Core`、`Cike.EventBus`） |

依赖顺序（从底层到上层）：`Cike.Core` → `Cike.EventBus` → `Cike.Data` → `Cike.Contracts`。

注意：`Cike.Contracts` 并非"零依赖"包——`PagedAndSortedResultRequest` 实现的 `IPagedAndSortedRequest` 接口定义在 `Cike.Data`（命名空间 `Cike.Data`），因此契约包项目引用了 `Cike.Data`，并连带传递引入 `Cike.Core` 与 `Cike.EventBus`。另有 NuGet 包依赖 `Microsoft.AspNetCore.Http.Abstractions` 2.2.0。引用 `Cike.Contracts` 不会给模块树增加任何行为（`CikeContractsModule` 为空模块）。

## Cike.Core

### 定位

模块系统与 DI 约定的定义者：定义"什么是模块、模块如何被发现与排序、服务如何自动注册"，以及全框架共用的异常类型和基础工具。所有 Cike 包直接或间接依赖它；它自己不定义任何模块。不需要它：与框架无关的独立类库。

### 能力清单

#### 模块系统（`Cike.Core.Modularity`）

| 类型/方法 | 精确签名 | 说明 |
|---|---|---|
| `CikeModule` | `public abstract class CikeModule` | 模块基类，三个生命周期方法均为 `public virtual async Task`、空实现，可直接 override 成非 async 的 `Task` 返回 |
| — 注册服务 | `public virtual async Task ConfigureServicesAsync(ServiceConfigurationContext context)` | 容器构建前串行调用（按依赖序） |
| — 应用初始化 | `public virtual async Task InitializeAsync(ApplicationInitializationContext context)` | 容器构建后并发调用（`Task.WhenAll`） |
| — 应用停止 | `public virtual async Task ShutdownAsync(ApplicationShutdownContext context)` | 并发调用（`Task.WhenAll`），由宿主停止钩子触发 |
| `DependsOnAttribute` | `[DependsOn(params Type[]? dependedTypes)]`，`AttributeUsage(Class, AllowMultiple = true)` | 声明模块依赖；可标注多次；递归收集、`HashSet<Type>` 去重 |
| `CikeModuleContainer` | `CikeModuleContainer(List<Type> moduleTypes)`；`List<Type> ModuleTypes { get; }`；`List<CikeModule> CikeModules { get; }` | 已加载模块容器；`CikeModules` 即执行顺序：**被依赖的在前、启动模块在最后**（后序遍历收集） |
| `IModuleLoader` / `ModuleLoader` | `CikeModuleContainer LoadCikeModules(Type startupType)` | 从启动模块类型出发后序遍历 `[DependsOn]` 树构建容器；模块实例经 `Activator.CreateInstance` 创建 |
| `ModuleLoader.Services` | `public static IServiceCollection Services { get; set; }` | 全局静态：`AddApplicationAsync` 末尾被赋值为宿主的 `IServiceCollection`（框架内部供 AutoValidation 等使用，见下文反模式） |
| `ModularityFactory` | `public static async Task AddApplicationAsync<TStartupModule>(IServiceCollection services)` | 实际引导逻辑：加载模块树 + 按模块遍历做 DI 约定扫描 + 串行 `ConfigureServicesAsync`（无泛型约束；对外用下面的扩展方法） |
| `IServiceCollectionModularityExtensions` | `public static async Task AddApplicationAsync<TStartupModule>(this IServiceCollection services) where TStartupModule : CikeModule` | **宿主引导入口**（命名空间 `Cike.Core.Extensions`）。先 `AddSingleton<IApplicationWithExternalServiceProvider>`，再委托 `ModularityFactory` |
| `ServiceConfigurationContext` | `ServiceConfigurationContext(IServiceCollection services)`；`IServiceCollection Services { get; }` | 注册阶段上下文。**只有 `Services` 一个成员**；`GetConfiguration()` 是 `IServiceCollection` 的扩展（见工具节），写作 `context.Services.GetConfiguration()` |
| `ApplicationInitializationContext` | `ApplicationInitializationContext(IServiceProvider serviceProvider)`；`IServiceProvider ServiceProvider { get; }` | 初始化/停止阶段上下文 |
| `ApplicationShutdownContext` | 同上，仅 `IServiceProvider ServiceProvider { get; }` | — |
| `IApplicationWithExternalServiceProvider` | `Task InitializeAsync(IServiceProvider serviceProvider)`；`Task ShutdownAsync(IServiceProvider serviceProvider)` | 由 `ApplicationWithExternalServiceProvider` 实现：从容器解析 `CikeModuleContainer`，对所有模块并发执行 `InitializeAsync` / `ShutdownAsync`（`Task.WhenAll`，无顺序保证） |

由 `Cike.AspNetCore.MinimalAPIs` 包补充（物理在该包、部分扩展类命名空间仍为 `Cike.Core.Modularity`，详见 [HTTP 接口层](./http-api.md)）：

| 方法 | 说明 |
|---|---|
| `WebApplicationBuilderExtensions.AddApplicationAsync<TStartupModule>(WebApplicationBuilder builder)` | 先 `ReplaceConfiguration(builder.Configuration)` 再调用核心入口 |
| `WebApplicationExtensions.InitializeApplicationAsync(this WebApplication app)` | 设置 `ObjectAccessor<IApplicationBuilder>` / `<IEndpointRouteBuilder>` 的值、注册 `ApplicationStopping` 停止钩子（触发全部 `ShutdownAsync`）、触发全部 `InitializeAsync` |
| `ApplicationInitializationContext.GetApplicationBuilder()` / `GetEndpointRouteBuilder()` / `GetEnvironment()` / `GetEnvironmentOrNull()` / `GetConfiguration()` / `GetLoggerFactory()` | 模块 `InitializeAsync` 里拿 ASP.NET Core 对象的正规途径 |

#### DI 约定（`Cike.Core.DependencyInjection`）

标记接口：

| 接口 | 说明 |
|---|---|
| `IDependencyInjection` | 根标记接口；自动扫描的判定条件（实现它或其子接口的**非抽象类**都会被扫描） |
| `ISingletonDependency : IDependencyInjection` | 标记单例 |
| `IScopedDependency : IDependencyInjection` | 标记 Scoped |
| `ITransientDependency : IDependencyInjection` | 标记 Transient |

`DependencyAttribute`（用法 `[Dependency(Key = "x", ReplaceServices = true, TryRegister = true)]`，三个均为可写属性、默认 `false`/`null`，**不是构造函数参数**）：

| 属性 | 类型 | 作用 |
|---|---|---|
| `Key` | `string?` | 非空时把接口/基类注册为 keyed 服务（`ServiceDescriptor.DescribeKeyed`，key 全部相同） |
| `ReplaceServices` | `bool` | `true` → 接口注册用 `services.Replace(descriptor)`，替换该服务类型已有注册 |
| `TryRegister` | `bool` | `true` → 接口注册用 `services.TryAdd(descriptor)`，已存在则跳过 |

自动注册算法（`ModularityFactory.AddApplicationAsync` 内，对容器中每个模块按序执行，扫描范围 = `module.GetType().Assembly` 即**该模块类所在程序集的全部类型**）：

1. 选生命周期（优先级从高到低）：实现 `ITransientDependency` → Transient；否则 `IScopedDependency` → Scoped；否则 `ISingletonDependency` → Singleton；只实现 `IDependencyInjection` 而无三个标记 → **默认 Transient**
2. 注册自身：`services.TryAdd(Describe(实现类, 实现类, 生命周期))`——首个注册生效
3. 注册**全部接口 + 全部基类**（`type.GetInterfaces().Concat(type.GetBaseClasses())`，基类包含 `object`，接口包含 `IDependencyInjection` 和三个标记接口本身）：统一注册为转发工厂 `sp => sp.GetRequiredService(实现类)`，生命周期 = **类自身标记的生命周期**；再按 `ReplaceServices` / `TryRegister` / 默认 `services.Add` 三选一落库
4. 之后 `await module.ConfigureServicesAsync(...)`

由此：接口解析与自身解析共享同一实例注册；同一接口多实现（如自定义仓储覆盖默认仓储）靠"后注册者胜出单解析 + `IEnumerable<T>` 解析全部"工作，机制细节见 [数据访问](./data-access.md)。

#### 异常（`Cike.Core.Exceptions`）

```csharp
public class BusinessException : Exception
{
    public string? Code { get; set; }      // 业务错误码，如 "ORDER:Duplicated"
    public string? Details { get; set; }   // 附加细节（不直接展示给终端用户）
    public LogLevel LogLevel { get; set; } // 默认 LogLevel.Warning

    public BusinessException(string? code = null, string? message = null, string? details = null,
                             Exception? innerException = null, LogLevel logLevel = LogLevel.Warning);
}

public class UserFriendlyException : BusinessException   // 业务校验失败的标准抛法
{
    public UserFriendlyException(string message, string? code = null, string? details = null,
                                 Exception? innerException = null, LogLevel logLevel = LogLevel.Warning);
}
```

注意两者构造参数顺序不同：`BusinessException` 第一个是 `code`，`UserFriendlyException` 第一个是 `message`。

HTTP 行为：`BusinessExceptionMiddleware`（`Cike.AspNetCore.MinimalAPIs` 包）捕获 `BusinessException` 及其子类 → 按 `ex.LogLevel` 记日志 → 返回 `Results.BadRequest(ex.Message)`：**HTTP 400，响应体是消息纯文本**（非 JSON 包装）。仅 `Code`/`Details` 不会出现在响应中。详见 [HTTP 接口层](./http-api.md)。

#### 工具

| 类型 | 命名空间 | 关键成员 |
|---|---|---|
| `IObjectAccessor<T>` / `ObjectAccessor<T>` | `Cike.Core.ObjectAccessor` | `T? Value { get; set; }`——跨阶段传递尚不可 DI 的对象（如 `IApplicationBuilder`） |
| `ServiceCollectionObjectAccessorExtensions` | `Cike.Core.Extensions.DependencyInjection` | `AddObjectAccessor<T>()`（重复注册抛异常）/ `TryAddObjectAccessor<T>()` / `GetObjectOrNull<T>()` / `GetObject<T>()` |
| `ServiceCollectionConfigurationExtensions` | `Microsoft.Extensions.DependencyInjection` | `ReplaceConfiguration(IConfiguration)`、`GetConfiguration()`（找不到抛 `ApplicationException`）、`GetConfigurationOrNull()`——优先取 `HostBuilderContext.Configuration`，其次已注册的 `IConfiguration` 单例 |
| `ServiceCollectionCommonExtensions` | `Microsoft.Extensions.DependencyInjection` | `IsAdded<T>()`、`GetSingletonInstance<T>()` / `GetSingletonInstanceOrNull<T>()`、`BuildServiceProviderFromFactory()` |
| `LazyManualMemoryCache<TKey, TValue>` | `Cike.Core` | 线程安全内存缓存（`ConcurrentDictionary<K, Lazy<V>>`，`ExecutionAndPublication`）：`Get(key, out value)`、`TryAdd`、`GetOrAdd`、`TryUpdate`、`AddOrUpdate`、`Remove`、`ContainsKey`、`Clear`、`Keys`、`Values`；`Dispose()` 会清空并置空内部字典（之后再用抛 NRE） |
| `AsyncHelper` | `Cike.Core.Thireading`（**注意命名空间拼写就是 Thireading**） | `IsAsync(MethodInfo)`、`UnwrapTask(Type)`、`RunSync<TResult>(Func<Task<TResult>>)` / `RunSync(Func<Task>)`（基于 Nito.AsyncEx `AsyncContext`） |
| `IHasher` / `Hasher` | `Cike.Core.Hashers` | `Hash(string)`：SHA256 → 大写 hex；`Hash(params string?[])`：非空白值以 `\|` 连接再哈希；`Hash(object?[], JsonSerializerOptions?)`：JSON 序列化后连接再哈希。`Hasher : IHasher, ISingletonDependency`，但见反模式节——**它不会被自动注册** |
| `IRemoteService` | `Cike.Core` | 空标记接口，标记"可远程暴露"的服务类型 |
| `DisposeAction` / `DisposeAction<T>` / `NullDisposable` | `Cike.Core` | Dispose 时执行回调；`NullDisposable.Instance` 为共享空实现 |
| `ExpressionExtensions` | `Cike.Core.Extensions.Expressions` | `Expression<Func<T, bool>>` 组合扩展（**非 System 命名空间，需 using `Cike.Core.Extensions.Expressions`**）：`Or<T>(this expr1, expr2)` → `OrElse`、`And<T>(this expr1, expr2)` → `AndAlso`、`Not<T>(this expr)` → `Not` 取反；`expr2` 的 lambda 参数经内部 `RebindParameterVisitor` 重绑定到 `expr1` 的参数后合并，因此两表达式必须是同 `T` 的单参数 lambda，合并结果复用 `expr1` 的参数与签名 |
| `ChangeTrackingDictionary<TKey, TValue>` | `Cike.Core` 与 `Cike.Core.Models` 各有一份（同名两类，`new` 隐藏基类成员） | `Dictionary` 子类，`Add`/`Remove`/索引器写操作触发 `onChange` 回调 |
| `Brackets` | `Cike.Core.Models` | `Brackets.Angle`（`<>`）/ `Brackets.Square`（`[]`），配合 `GetFriendlyTypeName` |

System 扩展（均在 `System` / `System.Collections.Generic` 命名空间，随包隐式可用）：

| 扩展类 | 常用成员 |
|---|---|
| `CikeStringExtensions` | `RemovePostFix` / `RemovePreFix`、`EnsureStartsWith` / `EnsureEndsWith`、`Left` / `Right`、`Truncate` / `TruncateWithPostfix`、`ToCamelCase` / `ToPascalCase` / `ToKebabCase` / `ToSnakeCase` / `ToSentenceCase`、`ToMd5`（大写 hex）、`ToEnum<T>`、`IsNullOrEmpty` / `IsNullOrWhiteSpace`（带 `NotNullWhen`）、`ReplaceFirst`、`SplitToLines`、`WithDefault`、`EmptyIfNull`、`NullIfEmpty`、`NullIfWhiteSpace`、`FistCharToUpper` / `FistCharToLower`（方法名拼写如此） |
| `CikeTypeExtensions` | `GetBaseClasses(includeObject = true)`（自动注册用它扫基类）、`IsAssignableTo<TTarget>`、`IsNullableType` / `GetTypeOfNullable`、`IsCollectionType` / `GetCollectionElementType`、`GetEnumerableElementType`、`IsNumericType`、`GetDefaultValue`、`GetSimpleAssemblyQualifiedName`、`GetFriendlyTypeName(Brackets)` |
| `CikeCollectionExtensions` | `IsNullOrEmpty`、`AddIfNotContains`（3 个重载）、`RemoveAll(predicate)` / `RemoveAll(items)`、`AddRange` |
| `CikeDictionaryExtensions` | `GetOrDefault`、`GetOrAdd`、`ConvertToDynamicObject` |
| `ObjectExtensions` | `As<T>()`（强转）、`To<T>() where T : struct`（`Convert.ChangeType`，Guid 走字符串转换）、`IsIn(params T[])`、`If(condition, func/action)`（链式条件） |
| `JsonSerializerExtensions`（`System.Text.Json`） | `EnableDynamicTypes()`：`object` 反序列化为 `JsonDynamicObject/Array/String/Number/Boolean` 而非 `JsonElement` |
| `JsonSerializerOptionsExtensions`（`Cike.Core.Extensions.System`） | `WithConverters(params JsonConverter[])`、`Clone()` |
| `PropertyAccessorExtensions`（`Cike.Core.Extensions.System`） | `GetPropertyName` / `GetProperty` / `GetPropertyValue` / `SetPropertyValue`（表达式或字符串指定属性） |
| `ReflectionHelper`（`Cike.Core.Extensions.System`） | `IsAssignableToGenericType`、`GetImplementedGenericTypes`、`GetSingleAttributeOrDefault<TAttr>`、`GetValueByPath`、`GetPublicConstantsRecursively` |
| `DictionaryConvert`（`Cike.Core.Extensions.System`） | `Convert<TSource>(source)`：表达式编译把对象（含集合属性递归）转 `Dictionary<string, object>` |

### 隐式行为

依赖 `Cike.Core` 并调用 `AddApplicationAsync<TStartupModule>()` 后框架自动完成：

1. 注册 `IApplicationWithExternalServiceProvider` 单例、`IModuleLoader` 单例、`CikeModuleContainer` 单例（容器本身可从 DI 解析）
2. 从启动模块后序遍历 `[DependsOn]` 树收集模块类型并实例化（`Activator.CreateInstance`——**模块类必须有无参构造**，不支持构造注入）
3. 按依赖序（被依赖在前）对每个模块所在程序集执行 DI 约定扫描（算法见能力清单），再串行 `await ConfigureServicesAsync`
4. 引导结束后 `ModuleLoader.Services = services`（全局静态）
5. 宿主调用 AspNetCore 包的 `InitializeApplicationAsync()` 后：全部模块 `InitializeAsync` **并发**执行（`Task.WhenAll`，无顺序保证）；`ApplicationStopping` 时全部 `ShutdownAsync` 并发执行

约定扫描的隐式副作用（每实现一个标记接口类都会发生）：

- 自身类型注册为可解析服务（`TryAdd`，首个生效）
- **所有接口 + 所有基类（含 `object`、含 `IDependencyInjection` 与三个标记接口本身）** 都注册为指向该实现类的转发服务——意味着 `GetService<object>()`、`GetService<IScopedDependency>()` 等都能解析出业务实例
- 接口/基类注册默认走 `services.Add`（追加不覆盖）：同接口多实现时单解析取最后注册者，`IEnumerable<T>` 解析出全部

### 示例

定义一个模块类（每个项目一个，项目名去点 + `Module`）：

```csharp
using Cike.Core.Modularity;
using Microsoft.Extensions.DependencyInjection;

public class OrderServiceModule : CikeModule
{
    // 注册阶段：容器尚未构建，只能操作 IServiceCollection
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.Configure<OrderOptions>(
            context.Services.GetConfiguration().GetSection("Order"));
        return Task.CompletedTask;
    }

    // 初始化阶段：容器已可用，可解析服务、挂 ASP.NET Core 中间件
    public override Task InitializeAsync(ApplicationInitializationContext context)
    {
        // GetApplicationBuilder() 等扩展由 Cike.AspNetCore.MinimalAPIs 包提供
        // context.GetApplicationBuilder().UseSomething();
        return Task.CompletedTask;
    }
}
```

宿主引导（`Program.cs`，自包含）：

```csharp
using Cike.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 核心入口：加载模块树 + 约定扫描 + 按序 ConfigureServices
await builder.Services.AddApplicationAsync<OrderServiceOpenModule>();

var app = builder.Build();

// AspNetCore 包扩展：触发全部 InitializeAsync + 注册 Shutdown 钩子
await app.InitializeApplicationAsync();

app.Run();
```

非 HTTP 宿主（纯控制台）等价引导：

```csharp
var services = new ServiceCollection();
await services.AddApplicationAsync<MyHostModule>();
var provider = services.BuildServiceProvider();
await provider.GetRequiredService<IApplicationWithExternalServiceProvider>()
              .InitializeAsync(provider);
```

标记接口自动注册（无需任何手写注册代码）：

```csharp
public interface IOrderRepository { Task<Order?> FindAsync(long id, CancellationToken ct = default); }

public class OrderRepository : IOrderRepository, IScopedDependency   // 标记即注册
{
    // 自动注册等价于：
    //   services.TryAdd(Scoped<OrderRepository, OrderRepository>());
    //   services.Add(typeof(IOrderRepository), sp => sp.GetRequiredService<OrderRepository>(), Scoped);
    //   ...（其余全部接口与基类同样注册为 Scoped 转发）
}
```

抛业务异常：

```csharp
// 校验失败 → HTTP 400，响应体为消息纯文本
throw new UserFriendlyException("购买数量必须大于 0");

// 需要错误码 / 提高日志级别时（message 是第一个参数之外的第二参数）
throw new BusinessException(code: "ORDER:Duplicated", message: "重复下单", logLevel: LogLevel.Error);
```

组合查询条件（`using Cike.Core.Extensions.Expressions;`，常与仓储 `GetListAsync(predicate)` / `GetQueryable()` 配合）：

```csharp
Expression<Func<Order, bool>> status = o => o.Status == OrderStatus.Paid;
Expression<Func<Order, bool>> tenant  = o => o.Amount > 100;

// 两个独立 lambda 直接 And/Or —— 参数自动重绑定，等价于 o => o.Status == Paid && o.Amount > 100
var combined = status.And(tenant);

// 取反：o => !(o.Status == Paid)
var excluded = status.Not();

var orders = await repository.GetListAsync(combined);
```

### 配置

无。`Cike.Core` 自身不定义任何 appsettings 键或 Options 类；它只提供读取配置的途径（`context.Services.GetConfiguration()`、初始化阶段的 `context.GetConfiguration()`）。各能力域模块的配置键见对应文档。

### 边界与反模式

1. **【陷阱·已按源码修正】接口/基类注册的生命周期**：模块 README 与大纲第 4 节写的"接口/基类一律以 Singleton 硬编码注册"是**旧版行为**（commit `47ad963` 时代）。当前源码：接口/基类注册为转发工厂 `sp => sp.GetRequiredService(实现类)`，生命周期 = 类自身标记的生命周期，与自身注册一致。不要再按"接口是 Singleton"写代码或文档。
2. `object`、`IDependencyInjection`、三个标记接口也被注册为服务——不要依赖 `GetService<object>()` / `IEnumerable<IScopedDependency>()` 之类的解析结果；同一程序集若存在两个模块类，约定扫描会跑两遍，接口 `services.Add` 描述符会重复（`IEnumerable<T>` 出现重复项）。
3. 类实现多个标记接口时优先级为 Transient > Scoped > Singleton（`ITransientDependency` 先被检查）；只实现 `IDependencyInjection` 根接口而无标记 → 默认 Transient。
4. 约定扫描只覆盖**含模块类的程序集**。`Cike.Core` 自身没有模块类，因此 `Hasher`（`ISingletonDependency`）的标记是**惰性的、不会被注册**——注入 `IHasher` 会解析失败，需要时须手动 `services.AddSingleton<IHasher, Hasher>()`。业务类库不写模块类 = 整个程序集不参与扫描。
5. `ModuleLoader.Services` 是全局静态 `IServiceCollection`：同进程多宿主（集成测试常见）会互相覆盖，框架内 AutoValidation 端点过滤器依赖它——多宿主测试需注意。
6. 模块类必须无参构造（`Activator.CreateInstance`）；不要在模块里做构造注入，服务依赖请放到 `InitializeAsync` 阶段从 `context.ServiceProvider` 解析。
7. `ConfigureServicesAsync` 串行且阻塞容器构建——不要在里头解析服务（容器还没建），不要做耗时 IO。
8. `InitializeAsync` / `ShutdownAsync` 是 `Task.WhenAll` 并发、无顺序保证——模块间初始化顺序依赖是不成立的，需要顺序请合并到一个模块里。
9. `GetConfiguration()` 不是 `ServiceConfigurationContext` 的成员；直接 `context.GetConfiguration()` 编译不过，正确写法 `context.Services.GetConfiguration()`，且要求宿主已注册 `IConfiguration`（Web 宿主默认有；裸 `ServiceCollection` 场景需先 `ReplaceConfiguration` 或自行注册，否则抛 `ApplicationException`）。
10. `AddObjectAccessor<T>` 重复注册直接抛异常，不确定时用 `TryAddObjectAccessor<T>`。
11. `LazyManualMemoryCache.Dispose()` 会把内部字典置空，Dispose 后继续使用抛 `NullReferenceException`。
12. `AsyncHelper.RunSync` 基于 Nito.AsyncEx `AsyncContext`，不要在有同步上下文的线程里嵌套调用。
13. `Hasher.Hash` 输出为**大写** hex（SHA256），与常见小写 hex 约定不一致；跨系统比对哈希值时注意大小写。
14. 不要在业务代码里手写 `services.AddXxx` 注册可标记接口的类——走标记接口 / `[Dependency]`（见大纲第 5 节反模式 1）。

## Cike.Contracts

### 定位

跨层数据契约：应用层 Dto 的基类体系和分页请求/响应的标准形状。被 `*.Application.Contracts` 层引用，向 Domain/Application/HTTP 各层提供无逻辑的纯数据类型。不需要它：非 Dto 场景（实体基类在 [数据访问](./data-access.md)，实体契约接口 `IEntity`/`ISoftDelete` 等也在那边）。

### 能力清单

DTO 基类（全部位于 `Cike.Contracts.EntityDtos`，纯 POCO，无任何方法）：

| 类型 | 定义 | 字段 |
|---|---|---|
| `EntityDto<TKey>` | — | `TKey Id { get; set; }` |
| `AuditedEntityDto<TKey, TUserId>` | `: EntityDto<TKey>` | `DateTime CreatedAt`、`TUserId CreatedBy`、`DateTime UpdatedAt`、`TUserId UpdatedBy` |
| `AuditedEntityDto<TKey>` | `: AuditedEntityDto<TKey, long>` | 同上，`TUserId` 固定 `long`（**这是常用形态**；需要 `Guid`/`string` 用户 Id 时用双泛型版本） |
| `FullAuditedEntityDto<TKey, TUserId>` | `: AuditedEntityDto<TKey, TUserId>` | + `bool IsDeleted`（软删标记） |
| `FullAuditedEntityDto<TKey>` | `: FullAuditedEntityDto<TKey, long>` | 同上，`TUserId` 固定 `long` |
| `PagedAndSortedResultRequest` | `: IPagedAndSortedRequest`（接口定义在 `Cike.Data` 包：`int Page`、`int PageSize`、`string Sorting`） | `virtual int Page = 1`、`virtual int PageSize = 10`、`virtual string? Sorting = null`（System.Linq.Dynamic 语法，如 `"CreatedAt desc"`；多字段逗号分隔） |
| `PagedResultDto<T>` | — | `long Total`、`List<T> Items = []`；构造 `PagedResultDto(long total, List<T> items)` |

**Dto 编写约定**（按种类对号入座，不要自行发挥）：

| Dto 种类 | 写法 | 示例 |
|---|---|---|
| 输出 Dto（详情/列表项，`XxxDto`） | 继承 **`AuditedEntityDto<TKey>`**（Id + 审计四字段，`TKey=long` 用单泛型版）——**默认选它，不是 FullAudited** | `public class AppSystemDto : AuditedEntityDto<long>` |
| 输出 Dto 要携带软删标记（回收站/审计页） | 这时才用 `FullAuditedEntityDto<TKey>`（多 `IsDeleted`）；正常列表/详情**不外发** `IsDeleted` | `public class RecycledOrderDto : FullAuditedEntityDto<long>` |
| 轻量输出（下拉/选项，`XxxBriefDto`/`XxxOptionDto`） | 继承 `EntityDto<TKey>`（只有 Id），再补展示字段 | `public class UserBriefDto : EntityDto<long>` |
| 输入 Dto（`CreateXxxDto`/`UpdateXxxDto`/动作 Dto 如 `ResetXxxPasswordDto`） | **平铺，不继承任何基类**——字段是用户可填子集，与审计无关 | `public class CreateOrderDto` |
| 嵌套子 Dto（挂在输入/输出 Dto 内部，如 `AddressDto`/`OrderLineDto`） | **平铺**，不继承（无独立审计语义） | `public class AddressDto` |
| 分页请求 | 分页参数默认直接用 `PagedAndSortedResultRequest` 对象承载（端点 `[AsParameters]` 绑定，经 Query/Handler 透传到仓储）；**筛选字段多时才建继承它的 Dto**（`GetXxxPagedListDto`，只加筛选字段），少量筛选参数平铺即可（见[事件与 CQRS](./events-cqrs.md)） | `public class GetOrderPagedListDto : PagedAndSortedResultRequest` |
| 分页返回 | `PagedResultDto<T>`（`Total` + `Items`），不自己造分页形状 | `PagedResultDto<OrderDto>` |

| 类型 | 定义 | 字段 |
|---|---|---|
| `EntityDto<TKey>` | — | `TKey Id { get; set; }` |
| `AuditedEntityDto<TKey, TUserId>` | `: EntityDto<TKey>` | `DateTime CreatedAt`、`TUserId CreatedBy`、`DateTime UpdatedAt`、`TUserId UpdatedBy` |
| `AuditedEntityDto<TKey>` | `: AuditedEntityDto<TKey, long>` | 同上，`TUserId` 固定 `long`（**这是常用形态**；需要 `Guid`/`string` 用户 Id 时用双泛型版本） |
| `FullAuditedEntityDto<TKey, TUserId>` | `: AuditedEntityDto<TKey, TUserId>` | + `bool IsDeleted`（软删标记，输出 Dto 携带） |
| `FullAuditedEntityDto<TKey>` | `: FullAuditedEntityDto<TKey, long>` | 同上，`TUserId` 固定 `long` |
| `PagedAndSortedResultRequest` | `: IPagedAndSortedRequest`（接口定义在 `Cike.Data` 包：`int Page`、`int PageSize`、`string Sorting`） | `virtual int Page = 1`、`virtual int PageSize = 10`、`virtual string? Sorting = null`（System.Linq.Dynamic 语法，如 `"CreatedAt desc"`；多字段逗号分隔） |
| `PagedResultDto<T>` | — | `long Total`、`List<T> Items = []`；构造 `PagedResultDto(long total, List<T> items)` |

模块类：`CikeContractsModule : CikeModule`（空实现，无任何生命周期逻辑）。

查询辅助扩展（**命名空间 `Cike.Contracts.Extensions`，物理位于 `Cike.Data.EFCore` 包**的 `IQueryablePaginationExtensions`——只引用 `Cike.Contracts` 拿不到，必须直接或经分层传递引用 `Cike.Data.EFCore`。机制详解见 [数据访问](./data-access.md)）：

```csharp
// 条件 Where：ifExpression 为 true 时应用谓词，否则原样返回
public static IQueryable<TEntity> WhereIf<TEntity>(this IQueryable<TEntity> query,
    bool ifExpression, Expression<Func<TEntity, bool>> whereExpression) where TEntity : class;

// Count + OrderBy(Sorting) + Skip/Take：
//   total = query.LongCountAsync()；
//   total > 0 且 Sorting 非空 → query.OrderBy(Sorting)（System.Linq.Dynamic.Core）；
//   PageSize > 0 → Skip((Page - 1) * PageSize).Take(PageSize)；
//   PageSize <= 0 → 不分页，返回全部行
public static Task<(long Total, List<TEntity> Items)> ToPaginationAsync<TEntity>(
    this IQueryable<TEntity> query, IPagedAndSortedRequest pageAndSorted,
    CancellationToken cancellationToken = default);

// 按 Id 查询；未找到抛 UserFriendlyException($"Id {id} is NotFound.")（HTTP 400）
// 约束：实体必须实现 IEntity<TKey>，且 TKey 必须是值类型（struct）
public static Task<TEntity> GetAsync<TEntity, TKey>(this IQueryable<TEntity> query,
    TKey id, CancellationToken cancellationToken = default)
    where TEntity : IEntity<TKey> where TKey : struct;
```

同一扩展类中还有一个 `AsNoTracking(query, bool asNoTracking)` 开关方法（跟踪行为二选一，含源码注释记载的自递归重载陷阱），属数据访问域能力，见 [数据访问](./data-access.md)。

### 隐式行为

无。`CikeContractsModule` 是空模块：不注册服务、不读配置、不挂中间件；其存在意义仅是充当 `[DependsOn]` 锚点（如模板生成的 `*ApplicationContractsModule` 会声明 `DependsOn(typeof(CikeContractsModule))`）。契约类型是纯 POCO，不参与任何自动注册或扫描副作用。

### 示例

Dto 继承（自包含）：

```csharp
using Cike.Contracts.EntityDtos;

// 输出 Dto：默认继承 AuditedEntityDto（Id/CreatedAt/CreatedBy/UpdatedAt/UpdatedBy）
public class OrderDto : AuditedEntityDto<long>
{
    public string OrderNo { get; set; } = default!;
    public decimal Amount { get; set; }
}

// 确需向外暴露软删标记（回收站/审计页）才用 FullAuditedEntityDto（多 IsDeleted）
public class RecycledOrderDto : FullAuditedEntityDto<long> { /* ... */ }

// 只需要 Id 的轻量 Dto
public class OrderBriefDto : EntityDto<long>
{
    public string OrderNo { get; set; } = default!;
}

// 分页请求：继承默认 Page=1 / PageSize=10 / Sorting=null
public class GetOrderListRequest : PagedAndSortedResultRequest
{
    public string? BuyerName { get; set; }
}
```

分页查询（需引用 `Cike.Data.EFCore`）：

```csharp
using Cike.Contracts.Extensions;   // 扩展实际定义在 Cike.Data.EFCore 包

var (total, items) = await repository.GetQueryable()
    .WhereIf(!request.BuyerName.IsNullOrWhiteSpace(), x => x.BuyerName.Contains(request.BuyerName!))
    .ToPaginationAsync(request);          // 排序/分页按 request.Page/PageSize/Sorting 自动应用

return new PagedResultDto<OrderDto>(total, mapper.Map<List<OrderDto>>(items));

// 按 Id 单查：未找到自动抛 UserFriendlyException（HTTP 400，消息 "Id {id} is NotFound."）
var order = await repository.GetQueryable().GetAsync(orderId);
```

### 配置

无。纯数据契约，不读配置、不定义 Options。

### 边界与反模式

1. **【陷阱】`ToPaginationAsync` 把 `Sorting` 字符串原样透传给 `System.Linq.Dynamic.Core` 的 `OrderBy`**——任意动态 LINQ 注入面。绝不让前端直接传排序字段；必须白名单映射（如仅允许 `CreatedAt`/`Amount` 等已知列名 + `desc` 后缀），非法值回退默认排序。
2. `Page <= 0` 会产生负数 `Skip` → EF Core 运行时异常；`PageSize <= 0` 则**返回全表**（不分页）。接收外部输入时先钳制（`Math.Max(1, ...)`）。
3. `ToPaginationAsync` 内部 `LongCountAsync()` 与分页路径的 `ToListAsync()` **不带** `cancellationToken`（仅 `PageSize <= 0` 的全量路径传递）——长查询无法经由该参数取消。
4. 审计字段是 `DateTime`（本地时间语义，非 `DateTimeOffset`）——跨时区系统慎用，勿自行改为 Offset 并期望基类配合。
5. `GetAsync` 的 `TKey` 约束为 `struct`——`string`/`Guid?` 主键实体（后者本就不该是可空）不适用，`string` 主键需自行 `FirstOrDefaultAsync` 并自己抛 `UserFriendlyException`。
6. `WhereIf`/`ToPaginationAsync`/`GetAsync` 不在 `Cike.Contracts` 包内——只引用契约包的编译单元（如 `*.Domain`）使用这些扩展会编译失败，属预期分层约束，不要为此反向引用 `Cike.Data.EFCore`。
7. `PagedResultDto<T>.Items` 默认初始化为空列表——反序列化旧数据或手工构造时 `null` 会被属性初始化器覆盖，别依赖"传 null 进构造函数"的语义。
