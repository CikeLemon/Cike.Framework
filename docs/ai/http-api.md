# HTTP 接口层

## 域定位

HTTP 接口层负责把业务用例暴露为 HTTP 端点：约定式自动路由（零 `app.MapGet/MapPost`）、全局 HTTP 行为（业务异常转 400、CORS、JSON long 序列化、认证中间件）、Swagger 装配、参数校验自动化。模块系统与自动 DI 见[框架内核](./framework-core.md)；端点通常只做"收 Dto → 发布 Command/Query → 返回结果"，用例派发见[事件与 CQRS](./events-cqrs.md)。

本层**只属于 HTTP 宿主项目**（分层规范中的 `*.Service.Open`）。Domain / Application / Application.Contracts 等项目禁止引用 `Cike.AspNetCore.*`；`Results`、`[FromServices]`、`TypedResults` 等 HTTP 概念不得出现在 Dto 之外的层。

## 覆盖的包

| 包 | 职责 | 依赖（项目引用 / 包引用） |
|---|---|---|
| Cike.AspNetCore.MinimalAPIs | 自动路由生成 + 全局 HTTP 行为 + 宿主引导扩展 | Cike.Core、Cike.Auth、Cike.FluentValidation；FluentValidation.AspNetCore 11.3.0；FrameworkReference Microsoft.AspNetCore.App。模块级 `[DependsOn(CikeAuthModule)]` |
| Cike.AspNetCore.Swagger | Swagger/SwaggerUI 约定式装配（两个扩展方法） | Cike.Core；Swashbuckle.AspNetCore 6.6.2 |
| Cike.FluentValidation | Validator 自动注册 + 端点参数自动校验 | Cike.Core；FluentValidation.DependencyInjectionExtensions 12.0.0；FrameworkReference Microsoft.AspNetCore.App |

依赖顺序：Swagger 与 FluentValidation 相互独立、均只依赖 Cike.Core；MinimalAPIs 编译期引用 FluentValidation（`[AutoValidation]` 的执行体 `AutoFluentValidationEndpointFilter` 在该包内），模块级依赖 CikeAuthModule（引入 `ICurrentUser`/`ICurrentTenant` 基础设施，见[认证与当前用户](./auth.md)）。宿主典型引用：`Service.Open → MinimalAPIs + Swagger`，`Application → FluentValidation`（在 Application 层模块上 `[DependsOn(CikeFluentValidationModule)]`）。

## 章节目录

- [Cike.AspNetCore.MinimalAPIs](#cikeaspnetcoreminimalapis)
- [Cike.AspNetCore.Swagger](#cikeaspnetcoreswagger)
- [Cike.FluentValidation](#cikefluentvalidation)

每章固定六节：定位 / 能力清单 / 隐式行为 / 示例 / 配置 / 边界与反模式。

## Cike.AspNetCore.MinimalAPIs

### 定位

约定式 Minimal API：端点类继承 `MinimalApiServiceBase`，public 方法按命名约定自动生成 `api/v1/...` 路由，无任何手写 `MapXxx`。同时在模块 `InitializeAsync` 中装配全局 HTTP 行为（异常中间件、CORS、JSON 约定、认证中间件）。

### 能力清单

**端点基类**（`namespace Cike.AspNetCore.MinimalAPIs`）：

```csharp
public abstract class MinimalApiServiceBase : ISingletonDependency
{
    public MinimalApiRouteOptions RouteOptions { get; set; } = new MinimalApiRouteOptions();
    public string? ServiceName { get; set; }
    public MinimalApiServiceBase() { }
}
```

实现 `ISingletonDependency` → 自动 DI 注册为 **Singleton**（自身 + 全部接口 + 全部基类，同一实例，见[框架内核](./framework-core.md)）。

**路由推导算法**（执行于 `CikeAspNetCoreMinimalApiModule.InitializeAsync`，按序）：

1. **资源名**：`ServiceName`（非空时）否则类名 → `RemovePostFix(IgnoredUrlSuffixesInServiceNames)`（忽略大小写，只剥第一个命中的后缀）→ `EnglishPluralizationService.Pluralize`（EF6 移植版，保留大小写，含不规则词典：`Category`→`Categories`、`Person`→`People`）。
2. **根**：`RootUrl` = `{Prefix}/{Version}`（默认 `api/v1`；`Version` 为空串则无版本段）。
3. **HTTP 动词**：方法名以前缀开始（`CurrentCultureIgnoreCase`）→ 动词；遍历全部词条，多命中时后命中覆盖先命中；无命中 → **POST**。
4. **方法段**：方法名 `RemovePreFix(全部前缀词条)`（**Ordinal，区分大小写**，只剥第一个命中）→ `RemovePostFix("Async")`（Ordinal）→ 剩余部分。
5. **id 段**：存在参数名为 `id`（`OrdinalIgnoreCase`）且**无任何** `IBindingSourceMetadata` 特性（即没有 `[FromServices]`/`[FromQuery]`/`[FromBody]` 等）→ 参数类型为 `Nullable<>` 或带默认值时生成 `{id?}`，否则 `{id}`；方法段非空 → `方法段/{id}`，为空 → `{id}`。该参数由路由绑定，不走查询串。
6. **完整路由**：`{RootUrl}/{资源名}/{方法段}`（方法段为空则省略，id 段并入方法段）。
7. **显式覆盖**：`[MinimalApiRoute]` 的 `Pattern` 非空 → 整体替换路由（**不含** `api/v1` 前缀，需要须自己写全）；`HttpMethods` 非空 → 替换动词。

**方法名前缀 → HTTP 动词映射表**（`HttpMethodPrefixMapDic` 默认值，11 条）：

| 方法名前缀 | HTTP 动词 |
|---|---|
| `Get` | GET |
| `Find` | GET |
| `Post` | POST |
| `Create` | POST |
| `Add` | POST |
| `Upsert` | POST |
| `Put` | PUT |
| `Update` | PUT |
| `Modify` | PUT |
| `Delete` | DELETE |
| `Remove` | DELETE |
| （无匹配） | POST |

**方法扫描规则**：`BindingFlags.Public | Instance | DeclaredOnly`——只扫**本类声明**的 public 方法（中间基类的方法不会被映射）；`IsSpecialName` 排除，但 `get_` 前缀（属性 getter）**保留**（本类声明的 public 属性 getter 会被当端点）；`[NonAction]` 标注的方法排除。

**参数绑定**（ASP.NET Core Minimal API 规则）：`CancellationToken` → `RequestAborted`；`[FromServices] T` → DI 解析；复杂类型 → JSON body（与动词无关，GET 也走 body，因此 GET 端点不要声明复杂参数）；简单类型 → 名字命中路由段（`{id}`）则路由绑定，否则查询串。

**特性签名**：

```csharp
// 显式路由（仅方法级；Pattern 整体替换，HttpMethods 可多个）
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class MinimalApiRouteAttribute : Attribute
{
    public MinimalApiRouteAttribute(string pattern, params string[] httpMethods);
    public string Pattern { get; set; }
    public IEnumerable<string> HttpMethods { get; set; }
}

// 排除方法，不生成端点（System.ComponentModel.Mvc 的 NonActionAttribute）
[NonAction]

// 开启参数自动校验（类级=全部方法，方法级覆盖类级；详见 FluentValidation 章）
public class AutoValidationAttribute : EndpointFilterBaseAttribute<AutoValidationEndpointFilterProvider>
{
    public AutoValidationAttribute(int order = 999);
}

// 自定义端点过滤器的扩展点（类级/方法级）
public abstract class EndpointFilterBaseAttribute : Attribute
{
    public Type ServiceType { get; }   // 必须是注册在 DI 中的 IEndpointFilterProvider 实现
    public int Order { get; }          // 升序执行，值小者先执行、可短路
}
public abstract class EndpointFilterBaseAttribute<TEndpointFilterProvider> : EndpointFilterBaseAttribute
    where TEndpointFilterProvider : IEndpointFilterProvider;

public interface IEndpointFilterProvider
{
    ValueTask<object?> HandlerAsync(EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next);
}
```

**路由选项全集**（`namespace Cike.AspNetCore.MinimalAPIs.Options`）：

```csharp
public class MinimalApiRouteOptions
{
    public string Prefix { get; set; } = "api";
    public string Version { get; set; } = "v1";
    public bool DisablePluralizeServiceName { get; set; } = false;   // 死配置：路由构建从不读取
    public Action<RouteHandlerBuilder>? RouteHandlerBuilder { get; set; }
        = routeHandlerBuilder => routeHandlerBuilder.RequireAuthorization();  // 端点级默认：要求认证
    public bool EnabledAuthorization { get; set; } = true;           // 认证开关（全局实例为总开关，端点级在其上逐服务收紧）
    public List<string> IgnoredUrlSuffixesInServiceNames { get; set; } = ["AppService", "Service"];
    public Dictionary<string, string> HttpMethodPrefixMapDic { get; set; } = /* 上表 11 条 */;
    public string RootUrl => $"{Prefix}{(Version.IsNullOrEmpty() ? "" : $"/{Version}")}";
}

public class GlobalMinimalApiRouteOptions : MinimalApiRouteOptions
{
    public IEnumerable<Assembly>? AdditionalAssemblies { get; set; } // 死配置：扫描范围固定为全部已加载模块程序集
    public string GetPluralizationName(string name);                 // 复数化入口（内置 EnglishPluralizationService）
}

[Obsolete("直接 DependsOn 引用 CikeAspNetCoreMinimalApiModule 即自动扫描")]
public class MinimalApiOptions { public List<Assembly> MinimalApiAsseblies { get; } ... }
```

**模块与全局行为**（模块类 `CikeAspNetCoreMinimalApiModule`，`[DependsOn(CikeAuthModule)]`）：

- `ConfigureServicesAsync`：`AddHttpContextAccessor`；`TryAddSingleton<AutoValidationEndpointFilterProvider>()`；`Configure<GlobalMinimalApiRouteOptions>`（Prefix=api, Version=v1）；`AddObjectAccessor<IApplicationBuilder/IEndpointRouteBuilder>()`；注册 CORS 策略（名 `CikeAspNetCoreMinimalApiModule`）；`ConfigureHttpJsonOptions` 注册 `LongToStringConverter` + `NullableLongToStringConverter`。
- `InitializeAsync` 按序装配中间件：`UseMiddleware<BusinessExceptionMiddleware>()` → `UseCors(...)` → 若 `GlobalMinimalApiRouteOptions.EnabledAuthorization` 则 `UseAuthentication()` + `UseAuthorization()` → 扫描映射全部端点（总开关同时门控端点级 `RequireAuthorization`，见隐式行为）。
- 端点扫描范围：`CikeModuleContainer.CikeModules` 全部模块的程序集中所有非抽象、继承 `MinimalApiServiceBase` 的类。

**宿主引导扩展**（`namespace Cike.AspNetCore.MinimalAPIs.Extensions`，`GetApplicationBuilder` 等在 `Cike.Core.Modularity`）：

```csharp
// 引导终章：写入 IApplicationBuilder/IEndpointRouteBuilder 访问器、注册 Shutdown 钩子、
// 触发全部模块 InitializeAsync（自动路由映射在此发生）
public static Task InitializeApplicationAsync(this WebApplication app);

// 挂载 TenantMiddleware（多租户解析，见 auth 文档）
public static void UseMultiTenant(this IApplicationBuilder app);

// 等价 builder.Services.AddApplicationAsync<T>() + ReplaceConfiguration(builder.Configuration)
public static Task AddApplicationAsync<TStartupModule>(this WebApplicationBuilder builder) where TStartupModule : CikeModule;

// Cike.Core.Extensions：加载模块树 + DI 约定扫描 + 按序 ConfigureServices
public static Task AddApplicationAsync<TStartupModule>(this IServiceCollection services) where TStartupModule : CikeModule;

// ApplicationInitializationContext 扩展（宿主模块 InitializeAsync 内取管线对象）
IApplicationBuilder GetApplicationBuilder(this ApplicationInitializationContext context);
IEndpointRouteBuilder GetEndpointRouteBuilder(this ApplicationInitializationContext context);
IWebHostEnvironment GetEnvironment(...); IWebHostEnvironment? GetEnvironmentOrNull(...);
IConfiguration GetConfiguration(...); ILoggerFactory GetLoggerFactory(...);
```

标准宿主引导（`Program.cs`）：

```csharp
var builder = WebApplication.CreateBuilder(args);
await builder.Services.AddApplicationAsync<HostModule>();
var app = builder.Build();
await app.InitializeApplicationAsync();
app.Run();
```

### 隐式行为

- **端点类是 Singleton**：`MinimalApiServiceBase : ISingletonDependency` → 自动注册为单例（自身+接口+基类）。
- **认证双层开关，默认全开**：全局 `GlobalMinimalApiRouteOptions.EnabledAuthorization`（默认 `true`）是**总开关**——开时挂 `UseAuthentication/UseAuthorization` 中间件；关时不挂中间件**且**不给任何端点加认证元数据。总开关为开时再看端点级 `RouteOptions.EnabledAuthorization`（默认 `true`）→ 该服务全部端点调用 `RouteHandlerBuilder`（默认 `RequireAuthorization()`）；端点级置 `false` 可让单个服务免认证。
- **业务异常转 400**：`BusinessExceptionMiddleware` 捕获 `BusinessException`（含子类 `UserFriendlyException`，异常体系见[框架内核](./framework-core.md)），按 `ex.LogLevel` 记日志后返回 `Results.BadRequest(ex.Message)`——**400 + 纯文本消息**（`text/plain`），非 ProblemDetails。其他异常不处理（走默认 500）。
- **JSON long 序列化**（仅 HTTP 请求/响应的 `JsonOptions`，不影响其他序列化器）：响应中 `long` 与 `long?` 一律输出字符串（防前端精度丢失）；请求中 `long`/`long?` 接受字符串或数字字面量。**陷阱**：`long?` 为 null 时输出 `"0"` 而非 `null`（`NullableLongToStringConverter` 的 `value?.ToString() ?? "0"`）。
- **CORS**：来源读配置节 `CorsDomains`（string 数组），缺省 `["localhost"]`；`AllowAnyHeader` + `AllowAnyMethod` + `AllowCredentials` + `SetPreflightMaxAge(2520 秒)`。
- **方法名大小写不对称**：动词前缀匹配忽略大小写，但方法段剥离用 Ordinal——非 PascalCase 命名（如 `getOrderAsync`）会命中 GET 但路由段保留 `getOrder`。
- **类级过滤器元数据**：类级 `EndpointFilterBaseAttribute`（未被方法级同类型覆盖时）同时写入端点 metadata（`WithMetadata`）。

### 示例

端点类（自包含）：

```csharp
[AutoValidation]
public class OrderService : MinimalApiServiceBase
{
    public OrderService()
    {
        RouteOptions.EnabledAuthorization = false; // 开发期关闭本服务端点认证
    }

    public async Task<Results<Ok<long>, BadRequest<string>>> CreateAsync(
        [FromServices] ILocalEventBus eventBus, CreateOrderDto dto, CancellationToken ct = default)
    {
        var command = new CreateOrderCommand(dto);
        await eventBus.PublishAsync(command, ct);
        return TypedResults.Ok(command.Id);
    }

    public async Task<Results<Ok<OrderDto>, NotFound>> GetAsync(
        [FromServices] ILocalEventBus eventBus, long id, CancellationToken ct = default)
        => TypedResults.Ok(await queryAsync(id));

    public Task<Ok<PagedResultDto<OrderDto>>> GetPagedListAsync(
        [FromServices] ILocalEventBus eventBus,
        string? keyword, long? buyerId,
        [AsParameters] PagedAndSortedResultRequest paging, CancellationToken ct = default)
        => ...;

    public Task<Ok> PayAsync([FromServices] ILocalEventBus eventBus, long id, CancellationToken ct = default)
        => ...;

    public Task<Ok> DeleteAsync([FromServices] ILocalEventBus eventBus, long id, CancellationToken ct = default)
        => ...;
}
```

生成路由对照表（资源名推导：`OrderService` 去后缀 `Service` → `Order` → 复数化 `Orders`）：

| 方法 | 推导过程 | HTTP | 路由 |
|---|---|---|---|
| `CreateAsync` | `Create`→POST；剥前缀+`Async` 后方法段为空 | POST | `/api/v1/Orders` |
| `GetAsync(long id)` | `Get`→GET；方法段空 + 必填 id 段 | GET | `/api/v1/Orders/{id}` |
| `GetPagedListAsync(...)` | `Get`→GET；方法段 `PagedList`；简单参数与 `[AsParameters]` 对象的属性按名走查询串 | GET | `/api/v1/Orders/PagedList?keyword=&buyerId=&page=1&pageSize=10` |
| `PayAsync(long id)` | 无匹配前缀→默认 POST；方法段 `Pay` + id 段 | POST | `/api/v1/Orders/Pay/{id}` |
| `DeleteAsync(long id)` | `Delete`→DELETE；方法段空 + id 段 | DELETE | `/api/v1/Orders/{id}` |

路由段冲突说明：`/Orders/List` 与 `/Orders/{id}` 同为 GET，字面量段优先于参数段（ASP.NET Core 路由优先级），不冲突。

显式覆盖示例：

```csharp
public class ReportService : MinimalApiServiceBase
{
    public ReportService() => ServiceName = "SalesReport"; // 资源名 SalesReports（ServiceName 同样剥后缀+复数化）

    [MinimalApiRoute("api/v2/reports/daily", "GET")]       // 整体替换路由与动词（多个动词：..., "GET", "POST"）
    public Task<Ok> DailyAsync(CancellationToken ct = default) => ...;

    [NonAction]                                             // 不生成端点
    public Task<Ok> InternalAsync() => ...;
}
```

### 配置

全局路由选项（宿主模块 `ConfigureServicesAsync`）：

```csharp
context.Services.Configure<GlobalMinimalApiRouteOptions>(options =>
{
    options.Prefix = "api";
    options.Version = "v2";                               // 全局根 → api/v2
    options.EnabledAuthorization = false;                 // 总开关：同时关闭认证中间件与全部端点的 RequireAuthorization
    options.IgnoredUrlSuffixesInServiceNames = ["AppService", "Service", "Endpoint"];
    options.RouteHandlerBuilder = b => b.RequireAuthorization("ApiPolicy"); // 端点级默认策略
    options.HttpMethodPrefixMapDic = new Dictionary<string, string>         // 全量替换（覆盖默认 11 条）
    {
        ["Get"] = "GET", ["Create"] = "POST", ["Delete"] = "DELETE",
    };
});
```

单服务覆盖（构造函数内改 `RouteOptions` / `ServiceName`）：

```csharp
public class AdminService : MinimalApiServiceBase
{
    public AdminService()
    {
        RouteOptions.Prefix = "admin-api";                // 本服务根 → /admin-api/v1
        RouteOptions.EnabledAuthorization = false;        // 本服务端点免认证（总开关为开时逐服务收紧；总开关关了则全体已免认证）
        RouteOptions.RouteHandlerBuilder = b => b.RequireAuthorization("Admin");
        RouteOptions.IgnoredUrlSuffixesInServiceNames = ["AppService", "Service", "Controller"];
        // 非空列表全量替换全局列表；设为空数组 [] 则完全不剥后缀
    }
}
```

appsettings.json：

```json
{
  "CorsDomains": [ "https://app.example.com", "http://localhost:5173" ]
}
```

模块依赖（宿主启动模块）：

```csharp
[DependsOn(typeof(CikeAspNetCoreMinimalApiModule))]
public class HostModule : CikeModule { }
```

### 边界与反模式

- 【陷阱】**端点默认全部 `RequireAuthorization`**：没配认证时请求失败（未注册认证方案通常直接 500 `InvalidOperationException`，配置了 JWT 但缺 token 才是 401）。开发期一步关闭：全局 `Configure<GlobalMinimalApiRouteOptions>(o => o.EnabledAuthorization = false)`——总开关同时控制中间件与端点元数据。只想让个别服务免认证时，在全局开启的前提下于端点类构造函数设 `RouteOptions.EnabledAuthorization = false`。
- 【陷阱】**Singleton 端点类不能构造注入 Scoped 服务**（仓储、DbContext、EventBus）：开发期（作用域校验）抛 `InvalidOperationException`，生产期形成 captive dependency。一律改为方法参数 `[FromServices]` 注入。
- 【陷阱】**两种 400 响应形状并存**，前端分别处理：`[AutoValidation]` 校验失败 → `application/problem+json` ProblemDetails（`{"errors": {属性: [消息数组]}}`）；`UserFriendlyException`/`BusinessException` → `text/plain` 纯文本消息。详见 FluentValidation 章。
- 不要手写 `app.MapGet/MapPost`、`AddControllers`——继承 `MinimalApiServiceBase` 走命名约定。
- 不要在非宿主项目（Domain/Application 等）引用 `Cike.AspNetCore.*`。
- id 段规则细节：`long? id` 或 `long id = 0` 生成 `{id?}`（可选段），`long id` 生成 `{id}`；`[FromQuery] long id` 不会生成路由段（有绑定特性即不参与 id 推导）。
- `DisablePluralizeServiceName`、`AdditionalAssemblies` 是**死配置**（声明但路由逻辑从不读取），不要设置并期望生效；`MinimalApiOptions` 已 `[Obsolete]`。
- 中间基类的 public 方法不会被映射（`DeclaredOnly`）；本类声明的 public 属性 getter 会被当端点——属性请用非 public 或放到非端点类。
- GET 端点不声明复杂类型参数（复杂类型一律 body 绑定）；列表过滤参数逐个声明为简单类型走查询串。
- 需要跨域私有端点集合时新建独立端点类（`ServiceName`/`Prefix` 可按服务粒度定制），不要在一个类里混杂多资源。

## Cike.AspNetCore.Swagger

### 定位

Swagger/SwaggerUI 的约定式封装：两个扩展方法完成全部装配，模块类 `CikeAspNetCoreSwaggerModule` 无任何逻辑、无依赖。

### 能力清单

```csharp
// services 端（命名空间 Cike.AspNetCore.Swagger）
public static IServiceCollection AddCikeSwagger(this IServiceCollection services,
    string projectName, Action<SwaggerGenOptions> configOptions = null);

// app 端
public static IApplicationBuilder UseCikeSwaggerUI(this IApplicationBuilder app,
    string projectName, Action<SwaggerUIOptions> configureOptions = null);
```

`AddCikeSwagger` 一次性做：

- `AddEndpointsApiExplorer()`（Minimal API 元数据必需）
- `SwaggerDoc("v1", OpenApiInfo { Title = projectName, Version = "v1" })`
- Bearer JWT 安全定义（`Name = "Authorization"`、`Scheme = "Bearer"`、`Type = SecuritySchemeType.Http`、`BearerFormat = "JWT"`、`In = Header`）+ 全局 `AddSecurityRequirement`——SwaggerUI 右上角 Authorize 粘贴 token 即可调试受保护端点
- `IncludeXmlComments`：**自动包含输出目录（BaseDirectory）下全部 `*.xml`**（所有开启 XML 输出的项目注释都会进来）
- 多态支持：`UseAllOfForInheritance()` + `UseOneOfForPolymorphism()`
- `SchemaFilter<EnumDescriptionSupplementSchemaFilter>()`
- 最后执行 `configOptions?.Invoke(options)`（可继续追加 `SupportNonNullableReferenceTypes()` 等，默认**未**开启）

`UseCikeSwaggerUI` = `UseSwagger().UseSwaggerUI(...)`，文档端点固定 `/swagger/v1/swagger.json`，UI 标题 = `projectName`。

`EnumDescriptionSupplementSchemaFilter`（`ISchemaFilter`）：对枚举类型 schema 的 `Description` 追加 `{"成员名":数值,...}` 映射（成员名带引号、数值为底层 int，逗号分隔）。枚举成员的 `<summary>` XML 注释文本由 Swashbuckle `IncludeXmlComments` 机制附加到同一 Description（形如 `1: 注释文本` 的行）。

### 隐式行为

- 文档 id 与 JSON 路径硬编码为 `v1`，不提供多版本文档配置；`projectName` 只进 Title/UI 名。
- XML 注释收集是"输出目录全量 `*.xml`"——新增项目只要开启了 XML 输出就自动纳入，无需逐个注册。
- 枚举描述追加不依赖 XML 文件（即使无 XML 也会追加成员名→数值映射）；非 int 底层类型的枚举会在文档生成时抛 `InvalidCastException`（按 `(int)` 强转）。
- `configOptions` 在全部默认配置**之后**执行，可覆盖默认值。

### 示例

宿主模块标准装配：

```csharp
[DependsOn(typeof(CikeAspNetCoreMinimalApiModule), typeof(CikeAspNetCoreSwaggerModule))]
public class HostModule : CikeModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddCikeSwagger("Cike.Demo", options =>
        {
            options.SupportNonNullableReferenceTypes(); // 示例：追加可选配置
        });
        return base.ConfigureServicesAsync(context);
    }

    public override Task InitializeAsync(ApplicationInitializationContext context)
    {
#if DEBUG
        context.GetApplicationBuilder().UseCikeSwaggerUI("Cike.Demo");
#endif
        return base.InitializeAsync(context);
    }
}
```

### 配置

需要 XML 注释出现在文档时，在**业务项目**的 csproj 开启输出（框架不做任何自动开启；两个扩展方法之外没有本包专属配置节）：

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

### 边界与反模式

- 项目未开启 XML 输出时，输出目录无 `*.xml` → XML 注释与枚举成员注释链路整体不生效（`IncludeXmlComments` 无文件可读）；这是常见"注释不显示"根因。
- 枚举成员注释要写 `/// <summary>`（XML 文档注释），不是 `[Display]`/`[Description]` 特性（本封装不读特性）。
- `SupportNonNullableReferenceTypes` 等可选项默认未开启，必须经 `configOptions` 委托自行追加，不要误以为引用即生效。
- 本包只做装配，不生成任何端点、不注册服务——不要在 `AddCikeSwagger` 之外再手写 `AddSwaggerGen`（会得到两份冲突配置）。
- Bearer 定义是全局安全要求：所有端点在 UI 中都会带上 Authorize 头（无 token 时也能发送请求，由服务端 401/500 兜底）。

## Cike.FluentValidation

### 定位

参数校验自动化：模块加载时自动注册全部 Validator；配合 `Cike.AspNetCore.MinimalAPIs` 的 `[AutoValidation]` 端点过滤器，对请求参数自动执行 FluentValidation 校验。校验规则定义在 Application 层（Validator 类），HTTP 层只标注特性。

### 能力清单

- `CikeFluentValidationModule`：`ConfigureServicesAsync` 中对 `CikeModuleContainer.CikeModules` 的**每个已加载模块的程序集**调用 `AddValidatorsFromAssembly`（FluentValidation.DependencyInjectionExtensions，Validator 默认注册为 Scoped）。**不要再手写 `AddValidatorsFromAssembly`**——重复注册。
- `AutoFluentValidationEndpointFilter`（`IEndpointFilter`）：遍历端点参数，凡**运行时类型**在启动时注册的 `IValidator<T>` 泛型参数集合内的参数，从请求容器解析 `IValidator<T>` 执行 `ValidateAsync`；失败返回 `Results.ValidationProblem(errors)`——**400 + `application/problem+json`**，`errors` 为 `{属性名: [消息数组]}`。
- `AdvancedJsonCamelCaseNamingPolicy`（`JsonNamingPolicy`）：当应用 JSON `PropertyNamingPolicy` 为 CamelCase（默认）时，把错误键转为 camelCase（`BuyerId` → `buyerId`；分隔符空格/点号处理，嵌套路径 `Address.Province` → `address.Province`）。
- 校验类型发现机制：`AutoValidationEndpointFilterProvider`（MinimalAPIs 包，`TryAddSingleton` 注册）在首次构造时从 `ModuleLoader.Services`（启动期 `IServiceCollection` 快照）扫描全部 `IValidator<>` 注册项，缓存被校验类型集合（静态、进程级）。

### 隐式行为

- `[AutoValidation]` 标在端点**类**上 → 该类全部端点校验；标在**方法**上 → 覆盖类级同类型特性（不会重复执行）。默认 `order = 999`（最内层、紧贴 handler 执行）。
- 自定义端点过滤器（继承 `EndpointFilterBaseAttribute<T>`）按 `Order` 升序执行，值小者先执行、可短路（短路时不进入后续 filter 与 handler，也不触发校验）。
- 校验对象约定是 **Dto**（如 `CreateOrderDto`，Validator 命名 `XxxDtoValidator`），不是 Command/Query——机制上任何有注册 Validator 的参数类型都会被校验，但端点参数应只收 Dto（Command/Query 由端点内部构造后经 `ILocalEventBus` 派发，见[事件与 CQRS](./events-cqrs.md)）。
- **静默不校验的情形**：`CikeAspNetCoreMinimalApiModule` 不 `[DependsOn(CikeFluentValidationModule)]`——若应用模块图中没有 `CikeFluentValidationModule`，没有任何 Validator 被注册，`[AutoValidation]` 不报错也不校验。需要 Application 层模块显式 `[DependsOn(typeof(CikeFluentValidationModule))]`。
- Validator 扫描范围 = **已加载模块的程序集**：Validator 所在项目必须有 `CikeModule` 类且进入模块依赖图；无模块类的纯类库不被扫描。

### 示例

Dto 与 Validator（Application.Contracts + Application 层）：

```csharp
// 契约层：Dto
public class CreateOrderDto
{
    public long BuyerId { get; set; }
    public AddressDto Address { get; set; } = null!;
    public List<OrderLineDto> Lines { get; set; } = new();
}

// 应用层：Validator（自动注册，无需任何手写 services.AddXxx）
public class CreateOrderDtoValidator : AbstractValidator<CreateOrderDto>
{
    public CreateOrderDtoValidator()
    {
        RuleFor(x => x.BuyerId).GreaterThan(0).WithMessage("买家Id必须大于0。");
        RuleFor(x => x.Address).NotNull().WithMessage("收货地址不能为空。");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("订单至少包含一个订单行。");
    }
}
```

端点标注（HTTP 宿主层）：

```csharp
[AutoValidation]                                        // 类级：全部方法自动校验 Dto 参数
public class OrderService : MinimalApiServiceBase
{
    public async Task<Results<Ok<long>, BadRequest<string>>> CreateAsync(
        [FromServices] ILocalEventBus eventBus, CreateOrderDto dto, CancellationToken ct = default)
        => ...;                                         // dto 不合法时根本不会进入方法体
}
```

校验失败响应（**形状一**，ProblemDetails）：

```json
HTTP 400
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "buyerId": ["买家Id必须大于0。"],
    "address": ["收货地址不能为空。"]
  }
}
```

业务异常响应（**形状二**，纯文本；`UserFriendlyException` 抛自 Handler/端点，由 `BusinessExceptionMiddleware` 转换）：

```
HTTP 400
Content-Type: text/plain; charset=utf-8

订单不存在。
```

### 配置

本包唯一"配置"是模块依赖声明——放在 Validator 所在项目的模块上（通常 Application 层）：

```csharp
[DependsOn(typeof(CQRSDomainModule), typeof(CikeCqrsModule), typeof(CikeFluentValidationModule))]
public class XxxApplicationModule : CikeModule { }
```

除此之外无选项类、无配置节；Validator 行为全部由 FluentValidation 自身 API（`RuleFor`/`SetValidator`/`RuleForEach` 等）决定。嵌套校验用 `SetValidator`/`RuleForEach(...).SetValidator(...)` 组合（子 Dto 的 Validator 同样会被自动注册并参与嵌套校验）。

### 边界与反模式

- 【陷阱】依赖本模块后**不要手写 `AddValidatorsFromAssembly`**——框架已对全部已加载模块程序集扫描，重复注册导致解析到多份 Validator。
- 【陷阱】**校验失败 ProblemDetails 与 `UserFriendlyException` 纯文本 400 是两种响应形状**——前端必须分别处理：前者解析 `errors` 字典做字段级提示，后者直接展示文本。
- 校验对象是 Dto，不是 Command：端点收 Dto → 校验 → 构造 Command 发布；不要给 Command 写 Validator 并期待 `[AutoValidation]` 校验它（Command 不是端点参数）。
- `[AutoValidation]` 不校验简单类型参数（`long`、`string` 查询串参数没有 Validator 机制）——简单参数合法性在端点内用 `UserFriendlyException` 兜底。
- Validator 放在无模块类的类库中不会被扫描（见隐式行为）。
- `CancellationToken`、`[FromServices]` 参数天然跳过校验；不要为它们写 Validator。
- 校验失败短路发生在端点过滤器层，先于业务异常中间件可见的范围——两者不会叠加（不会出现"校验失败又被包一层文本 400"）。
