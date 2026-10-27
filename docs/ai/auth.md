# 认证与当前用户

## 域定位

提供"当前用户"（`ICurrentUser`，JWT Claims 的只读投影）与"当前租户"（`ICurrentTenant`，`AsyncLocal<long>` 环境值）两个请求上下文抽象，以及把请求解析为租户 Id 的 `TenantMiddleware`——供业务代码读取当前身份，供数据访问层的多租户查询过滤与插入自动填值消费（见 [数据访问](./data-access.md)）。

**上下文不是认证**：本域不注册 AuthenticationHandler、不签发/校验 token——JWT 的注册与配置（`AddAuthentication().AddJwtBearer(...)` + `TokenValidationParameters`）完全由业务宿主自己写；需要 OpenIddict token 校验与端点权限特性时引用 [Cike.OpenIddict](./openiddict.md)（资源服务器能力域，产出本域消费的 Claims）。端点 401/403 的产生与认证开关见 [HTTP 接口层](./http-api.md)。本域只**消费**认证结果（`HttpContext.User` 的 Claims）。

什么时候不用：只做 token 签发/校验直接用 ASP.NET Core Authentication（`Microsoft.AspNetCore.Authentication.JwtBearer`），不需要本包；不读当前用户/租户、也未用 `IMultiTenant` 实体的项目不必引入。

## 覆盖的包

| 包 | 目录 | 定位 |
|---|---|---|
| `Cike.Auth` | `src/Cike.Auth`（net8.0） | `ICurrentUser`/`CurrentUserContext`（Claims 投影）、`ICurrentTenant`/`ICurrentTenantAccessor`（AsyncLocal 租户）、`CikeClaimTypes`（claim/请求键名约定）、`TenantMiddleware`（租户解析中间件）；模块类 `CikeAuthModule` |

包级依赖：项目 `Cike.Core`（模块系统、`DisposeAction`）+ 包 `Microsoft.AspNetCore.Http.Abstractions` 2.2.0。模块级：`CikeAuthModule` 无 `[DependsOn]`。注意：`UseMultiTenant()` 扩展方法**不在本包**，在 `Cike.AspNetCore.MinimalAPIs`（该模块 `[DependsOn(typeof(CikeAuthModule))]`，并负责注册 `AddHttpContextAccessor()`）。

## Cike.Auth

### 定位

- `ICurrentUser`：`HttpContext.User`（认证产出的 ClaimsPrincipal）的只读投影，singleton 实现——任意层注入读"当前登录用户"。
- `ICurrentTenant`：`AsyncLocal<long>` 环境租户值，由 `TenantMiddleware`（请求级）或 `Change()`（代码级）设置——数据访问层的租户过滤与插入自动填值消费的是**它**，不是 `ICurrentUser.TenantId`。
- `TenantMiddleware`：把请求中的租户 Id（Claims → Header → Cookie → Query）写入 `ICurrentTenant`，覆盖整个后续管线。
- 边界：不做认证、不签发/校验 token、没有"以某用户身份执行"的 API——认证配置由业务宿主自行注册，本包只读 Claims。

### 能力清单

命名空间一览：`Cike.Auth`（`ICurrentUser`/`CurrentUserContext`/`CikeClaimTypes`/`CikeAuthModule`）、`Cike.Auth.MultiTenant`（`ICurrentTenant`/`CurrentTenant`/`ICurrentTenantAccessor`/`CurrentTenantAccessor`）、`Cike.Auth.Middlewares`（`TenantMiddleware`）、`Cike.Auth.Extensions`（`ICurrentUserExtensions`/`ClaimsPrincipalExtensions`）。

**`ICurrentUser`（当前用户）**

```csharp
public interface ICurrentUser
{
    string? Id { get; }           // claim 键 CikeClaimTypes.UserId（默认 ClaimTypes.NameIdentifier），取首个匹配
    string? UserName { get; }     // 默认键 ClaimTypes.Name
    string? Name { get; }         // 默认键 ClaimTypes.GivenName
    string? SurName { get; }      // 默认键 ClaimTypes.Surname
    string? PhoneNumber { get; }  // 默认键 "phone_number"
    string? Email { get; }        // 默认键 ClaimTypes.Email
    long? TenantId { get; }       // 默认键 "tenantid"；long.TryParse 失败/缺失 → null
    string[] Roles { get; }       // 默认键 ClaimTypes.Role；全部匹配值 Distinct；无 → 空数组
    bool IsAuthorization { get; } // HttpContext.User.Identity?.IsAuthenticated ?? true
}
```

- 实现 `CurrentUserContext : ICurrentUser, ISingletonDependency`：singleton，构造器 `(IHttpContextAccessor)`，经 `IHttpContextAccessor.HttpContext?.User` 读 Claims。除接口成员外额外公开 `Claim[] FindClaims(string claimType)`。
- 每个 string 属性取**首个**匹配 claim；同一 claim 类型出现多次只读第一个（`Roles` 例外，读全部）。
- `IsAuthorization` 的两条特殊行为见"边界与反模式"。

**`ICurrentUserExtensions`（`Cike.Auth.Extensions`）**

| 扩展 | 签名 | 行为 |
|---|---|---|
| `GetLongId` | `long GetLongId(this ICurrentUser)` | `long.Parse(Id!)`——`Id` 为 null（未认证/后台线程）或非数字直接抛异常 |
| `GetGuidId` | `Guid GetGuidId(this ICurrentUser)` | `Guid.Parse(Id!)`——同上 |

**`ClaimsPrincipalExtensions`（对任意 `ClaimsPrincipal` 的读取扩展；`TenantMiddleware` 内部即用）**

| 扩展 | 签名 | 失败/缺失行为 |
|---|---|---|
| `FindClaims` | `Claim[] FindClaims(this ClaimsPrincipal, string claimType)` | 空数组 |
| `GetValue` | `string? GetValue(this ClaimsPrincipal, string claimType)` | null |
| `GetGuidValue` | `Guid? GetGuidValue(this ClaimsPrincipal, string key)` | null |
| `GetLongValue` | `long GetLongValue(this ClaimsPrincipal, string key)` | **0**（非 null——与 `ICurrentUser.TenantId` 的 `long?` 语义不同） |

**`CikeClaimTypes`（claim / 请求键名约定；全部 `static string { get; set; }`，启动期可改写）**

| 属性 | 默认值 | 消费点 |
|---|---|---|
| `UserId` | `ClaimTypes.NameIdentifier` | `ICurrentUser.Id` |
| `UserName` | `ClaimTypes.Name` | `ICurrentUser.UserName` |
| `Name` | `ClaimTypes.GivenName` | `ICurrentUser.Name` |
| `SurName` | `ClaimTypes.Surname` | `ICurrentUser.SurName` |
| `Role` | `ClaimTypes.Role` | `ICurrentUser.Roles` |
| `Email` | `ClaimTypes.Email` | `ICurrentUser.Email` |
| `PhoneNumber` | `"phone_number"` | `ICurrentUser.PhoneNumber` |
| `TenantId` | `"tenantid"`（小写、无分隔符） | `ICurrentUser.TenantId` 的 claim 键，**同时**是 `TenantMiddleware` 的 Header/Cookie/Query 键 |

**`ICurrentTenant` / `ICurrentTenantAccessor`（当前租户）**

```csharp
public interface ICurrentTenant
{
    long Id { get; }                    // 未设置 → 0
    IDisposable Change(long tenantId);  // 设置后返回恢复句柄；Dispose 恢复"进入前"的值（嵌套 Change 安全）
}

public interface ICurrentTenantAccessor
{
    long GetTenantId();
    void SetTenantId(long tenantId);
}
```

- `CurrentTenant : ICurrentTenant, ITransientDependency`：薄委托，状态全在 accessor。
- `CurrentTenantAccessor`：内部 `AsyncLocal<long>`（默认 0）；**不实现标记接口**，由 `CikeAuthModule` 手动 `AddSingleton` 注册——进程内唯一的 AsyncLocal 存储点。

**`TenantMiddleware`（租户解析中间件）**

```csharp
public class TenantMiddleware(ICurrentTenant _currentTenant) : IMiddleware, ITransientDependency
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next);
}
```

`GetTenantId(HttpContext)` 解析顺序（全部失败返回 0）：

| 顺序 | 来源 | 键 | 采纳条件 |
|---|---|---|---|
| 1 | `context.User`（JWT Claims） | `CikeClaimTypes.TenantId` = `"tenantid"` | 解析出的 long 必须 **> 0**（≤0/缺失落到下一来源） |
| 2 | `Request.Headers` | `"tenantid"` | `long.TryParse` 成功即可（无 >0 校验，0/负数也采纳） |
| 3 | `Request.Cookies` | `"tenantid"` | 同上 |
| 4 | `Request.Query` | `"tenantid"` | 同上 |

解析值 ≠ 当前 `ICurrentTenant.Id` 时以 `using (_currentTenant.Change(tenantId))` 包裹 `next(context)`；相等则直接放行。

**`UseMultiTenant`（挂载扩展，定义在 `Cike.AspNetCore.MinimalAPIs` 包）**

```csharp
// namespace Cike.AspNetCore.MinimalAPIs.Extensions（类 WebApplicationExtensions）
public static void UseMultiTenant(this IApplicationBuilder app);  // => app.UseMiddleware<TenantMiddleware>()
```

**`CikeAuthModule`**：`CikeModule` 子类，无 `[DependsOn]`；`ConfigureServicesAsync` 唯一动作是 `context.Services.AddSingleton<ICurrentTenantAccessor>(new CurrentTenantAccessor())`；`InitializeAsync` 无逻辑——`TenantMiddleware` **不会**自动挂载。

### 隐式行为

1. **DI 注册方式**：`CurrentUserContext`（→ `ICurrentUser`，singleton）、`CurrentTenant`（→ `ICurrentTenant`，transient）、`TenantMiddleware`（自类型 transient，满足 `IMiddleware` 模式的 `UseMiddleware<TenantMiddleware>()` 解析）走标记接口约定自动注册；`ICurrentTenantAccessor` 由模块手动注册 singleton（共享同一 `AsyncLocal`）。前提是 `CikeAuthModule` 在依赖图中（通常经 `CikeAspNetCoreMinimalApiModule`）——不在图中则本包**无任何**自动注册发生（模块系统见 [框架内核](./framework-core.md)）。
2. **`IHttpContextAccessor` 不由本包注册**：注册点在 `CikeAspNetCoreMinimalApiModule.ConfigureServicesAsync`（`AddHttpContextAccessor()`）。宿主只引 `Cike.Auth`（非 Web 宿主）时必须自行 `services.AddHttpContextAccessor()`，否则解析 `ICurrentUser` 失败（`CurrentUserContext` 构造依赖它）。
3. **`TenantMiddleware` 不自动挂载**：不调用 `app.UseMultiTenant()` 时 `ICurrentTenant.Id` 恒为 0。后果落在数据层（见 [数据访问](./data-access.md)）：`IMultiTenant` 实体的全局查询过滤（默认启用）按 `EF.Property<long>(e, "TenantId") == CurrentTenant.Id` 匹配——只会查到 `TenantId == 0` 的行；插入自动填 `TenantId = 0`。
4. **中间件顺序由模块初始化顺序保证**：模块按依赖排序初始化（被依赖在前，宿主启动模块最后）。`CikeAspNetCoreMinimalApiModule.InitializeAsync` 在 `GlobalMinimalApiRouteOptions.EnabledAuthorization`（默认 true）时已挂 `UseAuthentication`/`UseAuthorization`，因此宿主模块 `InitializeAsync` 里调 `UseMultiTenant()` 自然位于认证之后，Claims 分支能读到 JWT。`EnabledAuthorization = false` 时不挂认证中间件（总开关，端点也不再带认证要求）——Claims 分支永远取不到值，租户只能靠 Header/Cookie/Query。
5. **两个租户来源互不影响**：`TenantMiddleware` 与 `Change()` 只写 `ICurrentTenant`（AsyncLocal），从不写 Claims；`ICurrentUser.TenantId` 始终只反映 JWT 的 `"tenantid"` claim（无 claim 即 null）。同一请求内两者可以合法地不同：无 JWT 但 Header 带 `tenantid=7` 时，`ICurrentTenant.Id == 7`、`ICurrentUser.TenantId == null`。
6. **后台线程/非 HTTP 上下文**：`IHttpContextAccessor.HttpContext` 为 null 时 `ICurrentUser` 各 string 属性返回 null、`TenantId` null、`Roles` 空数组（`IsAuthorization` 例外，抛 `NullReferenceException`）。`ICurrentTenant` 的 AsyncLocal 值随异步调用链流动，但**不要假设**后台任务（Channel 后台消费、独立线程）继承发布时的租户——需要租户语义时把 `tenantId` 作为显式参数/事件字段传递，或用 `Change` 重新建立。
7. **`CikeClaimTypes` 是可变静态属性**：启动早期改键名对全进程所有读取点即时生效（含 `TenantMiddleware` 的 Header/Cookie/Query 键）；运行期修改非线程安全，禁止。

### 示例

读当前用户（应用层 Handler / 任意服务，构造器注入）：

```csharp
using Cike.Auth;
using Cike.Auth.Extensions;

public class OrderCommandHandler(ICurrentUser currentUser)
{
    public Task HandleAsync(CreateOrderCommand command)
    {
        // Id 是 string?（默认读 ClaimTypes.NameIdentifier claim）；确信在认证端点内才用强转扩展
        long userId = currentUser.GetLongId();          // Id 为 null/非数字会抛异常

        string? userName = currentUser.UserName;        // 无对应 claim → null
        string? email = currentUser.Email;
        string[] roles = currentUser.Roles;             // ["admin", ...]，Distinct，无 → 空数组

        // 只反映 JWT 的 "tenantid" claim；框架的数据层不过滤它（数据层读 ICurrentTenant.Id）
        long? tenantFromClaims = currentUser.TenantId;

        return Task.CompletedTask;
    }
}
```

启用租户中间件（宿主模块；JWT 注册配置由宿主自写）：

```csharp
using Cike.AspNetCore.MinimalAPIs;             // CikeAspNetCoreMinimalApiModule
using Cike.AspNetCore.MinimalAPIs.Extensions;  // UseMultiTenant()
using Cike.Core.Modularity;                    // CikeModule / GetApplicationBuilder()
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

[DependsOn(typeof(CikeAspNetCoreMinimalApiModule))]  // 该模块 [DependsOn] CikeAuthModule，自动带入本包
public class HostServiceModule : CikeModule
{
    // 认证（JWT）注册配置完全由宿主自己写——Cike.Auth 不做认证
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // 按项目实际填写 options.TokenValidationParameters（Issuer/SigningKey/Lifetime...）
                // token 内 claim 键名需与 CikeClaimTypes 默认值对齐，或改 CikeClaimTypes 对齐既有 token
            });
        return base.ConfigureServicesAsync(context);
    }

    // TenantMiddleware 不自动挂载，在宿主模块 InitializeAsync 中显式启用
    public override Task InitializeAsync(ApplicationInitializationContext context)
    {
        // MinimalAPIs 模块先初始化（EnabledAuthorization 默认 true 时已挂 UseAuthentication/UseAuthorization），
        // 这里挂的 TenantMiddleware 位于认证之后，Claims 分支可见 JWT
        context.GetApplicationBuilder().UseMultiTenant();
        return base.InitializeAsync(context);
    }
}
```

`ICurrentTenant.Change` 包裹用法（以指定租户身份执行一段逻辑）：

```csharp
using Cike.Auth.MultiTenant;
using Cike.Domain.Repositories;   // IRepository 命名空间以项目 _Imports.cs 为准

public class OrderMaintenanceService(IRepository<Order, long> orderRepository, ICurrentTenant currentTenant)
{
    public async Task RebuildAsync(long tenantId)
    {
        using (currentTenant.Change(tenantId))   // 只影响 ICurrentTenant.Id（AsyncLocal），不影响 ICurrentUser
        {
            // IMultiTenant 实体的全局查询过滤、插入自动填 TenantId 读的都是这个值
            var orders = await orderRepository.GetListAsync();
        }
        // Dispose 恢复"进入前"的值——嵌套 Change 时是外层值，不是恢复为 0
    }
}
```

### 配置

| 项 | 说明 |
|---|---|
| 框架 options / appsettings 绑定 | 无——本包不定义任何 options 类，不读任何配置节 |
| `CikeClaimTypes`（静态可写属性） | 唯一配置点：启动早期改写 claim/请求键名（如 `CikeClaimTypes.UserId = "uid";`）。运行期禁改（见隐式行为 7） |
| JWT 认证配置 | 宿主自写：`AddAuthentication().AddJwtBearer(...)` + `TokenValidationParameters`；token 的 claim 键名需与 `CikeClaimTypes` 默认值对齐（`UserId` → `ClaimTypes.NameIdentifier`、`TenantId` → `"tenantid"`、`Role` → `ClaimTypes.Role`），或反向改 `CikeClaimTypes` 适配既有 token |
| `GlobalMinimalApiRouteOptions.EnabledAuthorization` | HTTP 域选项（默认 true）**总开关**：控制 MinimalAPIs 模块是否自动挂 `UseAuthentication`/`UseAuthorization` 并给端点加 `RequireAuthorization`，间接决定 `TenantMiddleware` 的 Claims 分支是否可用，见 [HTTP 接口层](./http-api.md) |

### 边界与反模式

- **核心陷阱：`ICurrentUser.TenantId` 与 `ICurrentTenant.Id` 是两个不同来源**，不要混用：

  | | `ICurrentUser.TenantId` | `ICurrentTenant.Id` |
  |---|---|---|
  | 类型 | `long?` | `long` |
  | 来源 | JWT `"tenantid"` claim（`ClaimsPrincipal`） | `AsyncLocal<long>`（`TenantMiddleware` 写入 / `Change()` 手动设置） |
  | `Change()` 是否影响 | 否 | 是 |
  | 框架内消费方 | **无**（纯业务只读） | `CikeDbContext` 多租户查询过滤、`EntityHelper` 插入自动填 `TenantId` |
  | 未认证/未挂中间件时 | null | 恒 0（过滤匹配 `TenantId == 0`，插入填 0） |

  想通过切换租户影响数据过滤，必须走 `ICurrentTenant`；改 token/Claims 才会影响 `ICurrentUser`。注意：`src/Cike.Auth/README.md` 与 `docs/ai/README.md` 第 4 节第 7 条仍写"查询按 `ICurrentUser.TenantId` 过滤"——那是历史行为，当前源码（`CikeDbContext.CreateFilterExpression`）为 `CurrentTenant.Id`，以源码为准。
- **上下文不是认证**：本包不产生 401/403。端点鉴权用 MinimalAPI 的 `RequireAuthorization`（默认全局开启，见 [HTTP 接口层](./http-api.md)）。
- **`IsAuthorization` 两条坑**：`HttpContext` 为 null（后台线程）时抛 `NullReferenceException`；`Identity` 为 null 时返回 true（fail-open）。不要用它做安全决策，判定已认证请依赖端点鉴权或 `ICurrentUser.Id != null`。
- **`GetLongId()`/`GetGuidId()` 在未认证时抛异常**（`Id` 为 null）：先判空，或确保只在认证端点内调用。
- **不要重复注册 `ICurrentTenantAccessor`**：模块已注册 singleton；业务再 `AddSingleton` 一个实例（或 new `CurrentTenantAccessor()`）会产生第二个 `AsyncLocal` 存储，`Change` 互相不可见、租户静默丢失。
- **非 Web 宿主只引本包时**必须自行 `services.AddHttpContextAccessor()`，否则 `ICurrentUser` 解析失败。
- **租户解析的 >0 门槛只对 Claims 来源生效**：Header/Cookie/Query 可把 0/负数写入 `ICurrentTenant.Id`（`long.TryParse` 成功即采纳）——租户 Id 约定应为正数，依赖此行为做防御不可靠。键名固定为小写 `"tenantid"`。
- **不要在运行期改 `CikeClaimTypes`**：全进程即时生效且非线程安全。
- **后台任务不要假设继承请求租户**：把 `tenantId` 显式传递或用 `Change` 重建（见隐式行为 6）。
- 单例端点类可安全构造器注入 `ICurrentUser`/`ICurrentTenant`：前者本身 singleton，后者是无状态 transient（状态在全局 accessor 的 AsyncLocal）。

## 废弃的 Cike.MultiTenant

`src/Cike.MultiTenant` 是历史遗留目录，不在解决方案中、不参与编译，**勿用**。原因：

1. `CikeMultiTenantModule` 是普通空类，未继承 `CikeModule`——不是合法模块，模块系统不会加载；
2. 其 `ICurrentTenant` 用 **`Guid?`** 租户 Id，与框架现行 `long` 全面冲突（`IMultiTenant.TenantId`、`CikeClaimTypes.TenantId`、`TenantMiddleware` 均为 long）；
3. 其类型的命名空间写的也是 `Cike.Auth.MultiTenant`，与 `Cike.Auth` 包现行同名类型直接冲突——同时引用两个包会编译冲突。

替代：现行全部多租户能力在 `Cike.Auth`（本域文档）——`ICurrentTenant`（long）、`ICurrentTenantAccessor`、`TenantMiddleware`、`CikeClaimTypes`。
