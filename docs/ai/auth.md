# 当前用户与多租户

## 域定位

提供两个互相独立的请求上下文抽象：

- "当前用户"（`ICurrentUser`，`Cike.Security` 包）：JWT Claims 的只读投影，供业务代码读取当前身份、供数据层填审计字段。
- "当前租户"（`ICurrentTenant`，`Cike.MultiTenancy` 包）：`AsyncLocal<long>` 里的环境值。`TenantMiddleware` 把请求解析成租户 Id 写进去，数据访问层的多租户查询过滤和插入自动填值读的就是它（见 [数据访问](./data-access.md)）。

**上下文不是认证**：本域不注册 AuthenticationHandler，也不签发、不校验 token。JWT 的注册与配置（`AddAuthentication().AddJwtBearer(...)` + `TokenValidationParameters`）完全由业务宿主自己写。框架当前不提供端点级权限特性。端点 401/403 怎么产生、认证开关怎么用，见 [HTTP 接口层](./http-api.md)。本域只**消费**认证结果，也就是 `HttpContext.User` 里的 Claims。

什么时候不用：只做 token 签发或校验时，直接用 ASP.NET Core Authentication（`Microsoft.AspNetCore.Authentication.JwtBearer`）。项目不读当前用户、不读当前租户、也没用 `IMultiTenant` 实体时，不必显式引入这两个包。

## 覆盖的包

| 包 | 目录 | 定位 |
|---|---|---|
| `Cike.Security` | `src/Cike.Security` | `ICurrentUser`/`CurrentUserContext`（Claims 投影）、`CikeClaimTypes`（claim 键名约定）、`ClaimsPrincipalExtensions`/`ICurrentUserExtensions`；模块类 `CikeSecurityModule` |
| `Cike.MultiTenancy` | `src/Cike.MultiTenancy` | `ICurrentTenant`/`ICurrentTenantAccessor`（AsyncLocal 租户）、`TenantMiddleware`（租户解析中间件）、`CikeTenantConsts`（租户键名常量）；模块类 `CikeMultiTenancyModule` |

两包的包级依赖一样：项目 `Cike.Core`（模块系统、`DisposeAction`）加包 `Microsoft.AspNetCore.Http.Abstractions` 2.2.0。**两包互不引用**，两个模块类都没有 `[DependsOn]`。

谁把它们带进模块图：

| 依赖方 | 带入 |
|---|---|
| `CikeAspNetCoreMinimalApiModule` | 只带 `CikeMultiTenancyModule`，**不带** `CikeSecurityModule` |
| `CikeDataEFCoreModule` | `CikeSecurityModule` 和 `CikeMultiTenancyModule` 都带 |

`UseMultiTenant()` 扩展方法**不在** `Cike.MultiTenancy`，在 `Cike.AspNetCore.MinimalAPIs`。`AddHttpContextAccessor()` 也由 MinimalAPIs 模块负责注册。

> 历史：两包由原 `Cike.Auth` 拆分而来（用户上下文 → `Cike.Security`，租户 → `Cike.MultiTenancy`），`Cike.Auth` 包与 `CikeAuthModule` 已删除。迁移时改命名空间（`Cike.Auth` → `Cike.Security`，`Cike.Auth.MultiTenant` → `Cike.MultiTenancy`），`ICurrentUser.SurName` 改为 `NickName`，claim/租户键名默认值也变了（见下文 `CikeClaimTypes`、`CikeTenantConsts`）。

## Cike.Security

### 定位

- `ICurrentUser` 是 `HttpContext.User`（认证产出的 ClaimsPrincipal）的只读投影，实现是 singleton，任意层注入即可读"当前登录用户"。
- `CikeClaimTypes` 是 `ICurrentUser` 读取 claim 时用的键名约定，启动期可以改写。
- 边界：不做认证，不签发、不校验 token，也没有"以某用户身份执行"的 API。本包只读 Claims。

### 能力清单

命名空间：

- `Cike.Security`：`ICurrentUser`、`CikeClaimTypes`、`CikeSecurityModule`
- `Cike.Security.Internals`：`CurrentUserContext`（public 类）
- `Cike.Security.Extensions`：`ICurrentUserExtensions`、`ClaimsPrincipalExtensions`

**`ICurrentUser`（当前用户）**

```csharp
public interface ICurrentUser
{
    string? Id { get; }           // claim 键 CikeClaimTypes.UserId（默认 "sub"），取首个匹配
    string? UserName { get; }     // 默认键 "username"
    string? Name { get; }         // 默认键 "name"
    string? NickName { get; }     // 默认键 "nickname"
    string? PhoneNumber { get; }  // 默认键 "phone_number"
    string? Email { get; }        // 默认键 "email"
    long? TenantId { get; }       // 默认键 "tenant_id"；long.TryParse 失败或缺失 → null
    string[] Roles { get; }       // 默认键 "role"；全部匹配值 Distinct；无 → 空数组
    bool IsAuthorization { get; } // HttpContext.User.Identity?.IsAuthenticated ?? false
}
```

- 实现是 `CurrentUserContext : ICurrentUser, ISingletonDependency`：singleton，构造器参数 `(IHttpContextAccessor)`，通过 `IHttpContextAccessor.HttpContext?.User` 读 Claims。除接口成员外，它还额外公开 `Claim[] FindClaims(string claimType)`。全部属性都是 `virtual`。
- 每个 string 属性只取**第一个**匹配的 claim，同一 claim 类型出现多次时只读第一个。`Roles` 例外，读全部。
- `IsAuthorization` 的特殊行为见"边界与反模式"。

**`ICurrentUserExtensions`（`Cike.Security.Extensions`）**

| 扩展 | 签名 | 行为 |
|---|---|---|
| `GetLongId` | `long GetLongId(this ICurrentUser)` | `long.Parse(Id!)`。`Id` 为 null（未认证、后台线程）或不是数字时直接抛异常 |
| `GetGuidId` | `Guid GetGuidId(this ICurrentUser)` | `Guid.Parse(Id!)`，同上 |

**`ClaimsPrincipalExtensions`（`Cike.Security.Extensions`，可用于任意 `ClaimsPrincipal`）**

| 扩展 | 签名 | 失败/缺失行为 |
|---|---|---|
| `FindClaims` | `Claim[] FindClaims(this ClaimsPrincipal, string claimType)` | 空数组 |
| `GetValue` | `string? GetValue(this ClaimsPrincipal, string claimType)` | null |
| `GetGuidValue` | `Guid? GetGuidValue(this ClaimsPrincipal, string key)` | null |
| `GetLongValue` | `long GetLongValue(this ClaimsPrincipal, string key)` | 返回 **0**，不是 null（和 `ICurrentUser.TenantId` 的 `long?` 语义不同） |

**`CikeClaimTypes`（claim 键名约定）**

全部属性都是 `static string { get; set; }`，启动期可以改写。默认值是 OIDC 风格的短键：

| 属性 | 默认值 | 消费点 |
|---|---|---|
| `UserId` | `"sub"` | `ICurrentUser.Id` |
| `UserName` | `"username"` | `ICurrentUser.UserName` |
| `Name` | `"name"` | `ICurrentUser.Name` |
| `NickName` | `"nickname"` | `ICurrentUser.NickName` |
| `Role` | `"role"` | `ICurrentUser.Roles` |
| `Email` | `"email"` | `ICurrentUser.Email` |
| `PhoneNumber` | `"phone_number"` | `ICurrentUser.PhoneNumber` |
| `TenantId` | `"tenant_id"` | **只**被 `ICurrentUser.TenantId` 使用；`TenantMiddleware` 不读它（见 Cike.MultiTenancy） |

> 源码中 `TenantId` 的 XML 注释还写着 `Default: "tenantid"`，那是历史残留，实际值是 `"tenant_id"`，以代码为准。

**`CikeSecurityModule`**：`CikeModule` 子类，没有 `[DependsOn]`，两个生命周期方法都没有逻辑。它存在的意义是让 `Cike.Security` 程序集进入模块图，从而触发约定式 DI 扫描。

### 隐式行为

1. **DI 注册**：`CurrentUserContext`（注册为 `ICurrentUser`，singleton）走标记接口约定自动注册。前提是 `CikeSecurityModule` 在依赖图中。**`CikeAspNetCoreMinimalApiModule` 不带入它**，通常经 `CikeDataEFCoreModule` 带入。只用 MinimalAPIs、不用 EF Core 的宿主，如果要注入 `ICurrentUser`，必须自己 `[DependsOn(typeof(CikeSecurityModule))]`，否则解析失败。模块系统见 [框架内核](./framework-core.md)。
2. **`IHttpContextAccessor` 不由本包注册**：注册点在 `CikeAspNetCoreMinimalApiModule.ConfigureServicesAsync`（`AddHttpContextAccessor()`）。非 Web 宿主（没有 MinimalAPIs 模块）必须自己 `services.AddHttpContextAccessor()`，否则 `ICurrentUser` 解析失败，因为 `CurrentUserContext` 的构造器依赖它。
3. **后台线程或非 HTTP 上下文**：`IHttpContextAccessor.HttpContext` 为 null 时，`ICurrentUser` 的各 string 属性返回 null、`TenantId` 返回 null、`Roles` 返回空数组。`IsAuthorization` 例外，会抛 `NullReferenceException`。
4. **`CikeClaimTypes` 是可变静态属性**：启动早期改键名，对全进程所有 `ICurrentUser` 读取点立即生效。运行期修改不是线程安全的，禁止这样做。
5. **键名必须和 token 实际 claim 类型一致**：`ICurrentUser` 按 `CikeClaimTypes` 原样匹配 `Claim.Type`，没有别名或映射层。JwtBearer 的 `MapInboundClaims` 默认为 `true`，会把 token 里的部分短键改写成 `ClaimTypes.*` 长 URI（例如 `sub` → `ClaimTypes.NameIdentifier`、`role` → `ClaimTypes.Role`），导致 `ICurrentUser.Id`/`Roles` 恒为空。使用 JwtBearer 时必须设 `options.MapInboundClaims = false`（写法见 Cike.MultiTenancy 章的宿主模块示例）；或者反过来，把 `CikeClaimTypes` 改成映射后的长 URI。

### 示例

读当前用户（应用层 Handler 或任意服务，构造器注入）：

```csharp
using Cike.Security;
using Cike.Security.Extensions;

public class OrderCommandHandler(ICurrentUser currentUser)
{
    public Task HandleAsync(CreateOrderCommand command)
    {
        // Id 是 string?（默认读 "sub" claim）；确信在认证端点内才用强转扩展
        long userId = currentUser.GetLongId();          // Id 为 null 或非数字会抛异常

        string? userName = currentUser.UserName;        // 无对应 claim → null
        string? nickName = currentUser.NickName;
        string[] roles = currentUser.Roles;             // ["admin", ...]，Distinct，无 → 空数组

        // 只反映 JWT 的 "tenant_id" claim；框架的数据层不按它过滤（数据层读 ICurrentTenant.Id）
        long? tenantFromClaims = currentUser.TenantId;

        return Task.CompletedTask;
    }
}
```

启动早期改写键名，对齐既有 token：

```csharp
CikeClaimTypes.UserId = "uid";
CikeClaimTypes.Role = "roles";
```

### 配置

| 项 | 说明 |
|---|---|
| 框架 options / appsettings 绑定 | 无。本包不定义任何 options 类，也不读任何配置节 |
| `CikeClaimTypes`（静态可写属性） | 唯一的配置点：启动早期改写 claim 键名。运行期禁改（见隐式行为 4） |
| JWT 认证配置 | 宿主自己写：`AddAuthentication().AddJwtBearer(...)` + `TokenValidationParameters`。token 的 claim 键名要和 `CikeClaimTypes` 默认值对齐（`sub`、`role`、`tenant_id`……），或者反过来改 `CikeClaimTypes` 去适配既有 token。使用 JwtBearer 时必须设 `MapInboundClaims = false`（见隐式行为 5） |

### 边界与反模式

- **`IsAuthorization` 的坑**：`HttpContext` 为 null（后台线程）时会抛 `NullReferenceException`。在 HTTP 请求内它按 `Identity?.IsAuthenticated ?? false` 判定。后台代码不要调用它；判定"已认证"时，优先依赖端点鉴权或 `ICurrentUser.Id != null`。
- **`GetLongId()`/`GetGuidId()` 在未认证时抛异常**（因为 `Id` 为 null）：先判空，或者确保只在认证端点内调用。
- **非 Web 宿主**必须自己 `services.AddHttpContextAccessor()`。**只依赖 MinimalAPIs 的宿主**必须自己 DependsOn `CikeSecurityModule`（见隐式行为 1）。
- **不要在运行期改 `CikeClaimTypes`**：改动全进程立即生效，而且不是线程安全的。
- 单例端点类可以安全地构造器注入 `ICurrentUser`，因为它本身就是 singleton。

## Cike.MultiTenancy

### 定位

- `ICurrentTenant` 是 `AsyncLocal<long>` 里的环境租户值，由 `TenantMiddleware`（请求级）或 `Change()`（代码级）设置。数据访问层的租户过滤和插入自动填值读的是**它**，不是 `ICurrentUser.TenantId`。
- `TenantMiddleware` 把请求中的租户 Id 写入 `ICurrentTenant`，作用于整个后续管线。解析顺序是 Claims → Header → Cookie → Query。
- 边界：本包不依赖 `Cike.Security`，租户解析不经过 `ICurrentUser`/`CikeClaimTypes`，直接读 `HttpContext.User`。

### 能力清单

命名空间：

- `Cike.MultiTenancy`：`ICurrentTenant`、`ICurrentTenantAccessor`、`CikeTenantConsts`、`CikeMultiTenancyModule`
- `Cike.MultiTenancy.Internals`：`CurrentTenant`（**internal**）、`CurrentTenantAccessor`（public）
- `Cike.MultiTenancy.Middlewares`：`TenantMiddleware`

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

- `CurrentTenant : ICurrentTenant, ITransientDependency`：internal 薄委托，状态全在 accessor 里。业务只依赖接口。
- `CurrentTenantAccessor`：内部是 `AsyncLocal<long>`（默认 0）。它**不实现标记接口**，由 `CikeMultiTenancyModule` 手动 `AddSingleton` 注册，是进程内唯一的 AsyncLocal 存储点。

**`CikeTenantConsts`（静态类）**

```csharp
public static class CikeTenantConsts
{
    public const string TenantIdKey = "tenant_id";   // TenantMiddleware 的 claim / Header / Cookie / Query 键

    // ClaimsPrincipal 扩展：取首个匹配 claim 并 long.TryParse，失败/缺失 → 0
    public static long GetLongValue(this ClaimsPrincipal claimsPrincipal, string key);
}
```

**`TenantMiddleware`（租户解析中间件）**

```csharp
public class TenantMiddleware(ICurrentTenant _currentTenant) : IMiddleware, ITransientDependency
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next);
}
```

`GetTenantId(HttpContext)` 的解析顺序如下，全部失败时返回 0：

| 顺序 | 来源 | 键 | 采纳条件 |
|---|---|---|---|
| 1 | `context.User`（JWT Claims） | `CikeTenantConsts.TenantIdKey` = `"tenant_id"` | 解析出的 long 必须 **> 0**，≤0 或缺失就落到下一来源 |
| 2 | `Request.Headers` | `"tenant_id"` | `long.TryParse` 成功即可（不校验 >0，0 和负数也采纳） |
| 3 | `Request.Cookies` | `"tenant_id"` | 同上 |
| 4 | `Request.Query` | `"tenant_id"` | 同上 |

解析值与当前 `ICurrentTenant.Id` 不同时，用 `using (_currentTenant.Change(tenantId))` 包裹 `next(context)`；相同则直接放行。

**`UseMultiTenant`（挂载扩展，定义在 `Cike.AspNetCore.MinimalAPIs` 包）**

```csharp
// namespace Cike.AspNetCore.MinimalAPIs.Extensions（类 WebApplicationExtensions）
public static void UseMultiTenant(this IApplicationBuilder app);  // => app.UseMiddleware<TenantMiddleware>()
```

**`CikeMultiTenancyModule`**：`CikeModule` 子类，没有 `[DependsOn]`。`ConfigureServicesAsync` 只做一件事：`context.Services.AddSingleton<ICurrentTenantAccessor>(new CurrentTenantAccessor())`。它**不会**自动挂载 `TenantMiddleware`。

### 隐式行为

1. **DI 注册方式**：`CurrentTenant`（注册为 `ICurrentTenant`，transient；约定扫描用 `Assembly.GetTypes()`，internal 类同样会被注册）和 `TenantMiddleware`（注册自身类型，transient，供 `UseMiddleware<TenantMiddleware>()` 按 `IMiddleware` 模式解析）走标记接口约定自动注册。`ICurrentTenantAccessor` 由模块手动注册为 singleton，大家共享同一个 `AsyncLocal`。前提是 `CikeMultiTenancyModule` 在依赖图中，MinimalAPIs 模块和 EF Core 模块都会带入它。
2. **`TenantMiddleware` 不自动挂载**：不调用 `app.UseMultiTenant()` 时，`ICurrentTenant.Id` 恒为 0。后果落在数据层（见 [数据访问](./data-access.md)）：`IMultiTenant` 实体的全局查询过滤（默认启用）按 `TenantId == CurrentTenant.Id` 匹配，只能查到 `TenantId == 0` 的行，插入时也自动填 `TenantId = 0`。
3. **中间件顺序靠模块初始化顺序保证**：模块按依赖顺序初始化，被依赖的先初始化，宿主启动模块最后。`CikeAspNetCoreMinimalApiModule.InitializeAsync` 在 `GlobalMinimalApiRouteOptions.EnabledAuthorization`（默认 true）为 true 时已经挂了 `UseAuthentication`/`UseAuthorization`，所以宿主模块在 `InitializeAsync` 里调 `UseMultiTenant()`，它自然排在认证之后，Claims 分支能读到 JWT。另一种挂法是在 `app.InitializeApplicationAsync()` 之后调用，效果相同。`EnabledAuthorization = false` 时 MinimalAPIs 模块不挂认证中间件，Claims 分支取不到值，租户只能靠 Header/Cookie/Query。
4. **租户键名固定，不跟随 `CikeClaimTypes`**：`TenantMiddleware` 用的是常量 `CikeTenantConsts.TenantIdKey = "tenant_id"`，不可配置。改了 `CikeClaimTypes.TenantId` 只影响 `ICurrentUser.TenantId`，中间件的 claim、Header、Cookie、Query 键都不变。
5. **两个租户来源互不影响**：`TenantMiddleware` 和 `Change()` 只写 `ICurrentTenant`（AsyncLocal），从不写 Claims。`ICurrentUser.TenantId` 始终只反映 JWT 中键为 `CikeClaimTypes.TenantId` 的 claim，没有这个 claim 就是 null。所以同一请求内两者可以不同，而且是合法的：没有 JWT 但 Header 带了 `tenant_id=7` 时，`ICurrentTenant.Id == 7`，`ICurrentUser.TenantId == null`。
6. **后台任务**：`ICurrentTenant` 的 AsyncLocal 值会随异步调用链流动，但**不要假设**后台任务（Channel 后台消费、独立线程）继承了发布时的租户。需要租户语义时，把 `tenantId` 作为显式参数或事件字段传递，或者用 `Change` 重新建立。

### 示例

启用租户中间件（宿主模块；JWT 注册配置由宿主自己写）：

```csharp
using Cike.AspNetCore.MinimalAPIs;             // CikeAspNetCoreMinimalApiModule
using Cike.AspNetCore.MinimalAPIs.Extensions;  // UseMultiTenant()
using Cike.Core.Modularity;                    // CikeModule / GetApplicationBuilder()
using Cike.Security;                           // CikeSecurityModule
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

// MinimalAPIs 模块已带入 CikeMultiTenancyModule；要注入 ICurrentUser 且未依赖 EF Core 模块时显式加 CikeSecurityModule
[DependsOn(typeof(CikeAspNetCoreMinimalApiModule), typeof(CikeSecurityModule))]
public class HostServiceModule : CikeModule
{
    // 认证（JWT）注册配置完全由宿主自己写，框架不做认证
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // 按项目实际填写 options.TokenValidationParameters（Issuer/SigningKey/Lifetime...）

                // 必须关闭入站 claim 映射：默认 true 会把 sub/role 改写成 ClaimTypes.* 长 URI，ICurrentUser 将读不到
                options.MapInboundClaims = false;
                // 让 Identity.Name / [Authorize(Roles = ...)] 也按短键工作
                options.TokenValidationParameters.NameClaimType = CikeClaimTypes.UserName;
                options.TokenValidationParameters.RoleClaimType = CikeClaimTypes.Role;
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

用 `ICurrentTenant.Change` 包裹，以指定租户身份执行一段逻辑：

```csharp
using Cike.MultiTenancy;
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
        // Dispose 恢复"进入前"的值：嵌套 Change 时恢复成外层值，不是 0
    }
}
```

### 配置

| 项 | 说明 |
|---|---|
| 框架 options / appsettings 绑定 | 无。本包不定义 options 类，也不读配置节 |
| 租户键名 | 常量 `CikeTenantConsts.TenantIdKey = "tenant_id"`，**不可配置** |
| `GlobalMinimalApiRouteOptions.EnabledAuthorization` | HTTP 域选项（默认 true），是**总开关**：控制 MinimalAPIs 模块是否自动挂 `UseAuthentication`/`UseAuthorization`，从而间接决定 `TenantMiddleware` 的 Claims 分支能不能用，见 [HTTP 接口层](./http-api.md) |

### 边界与反模式

- **核心陷阱：`ICurrentUser.TenantId` 和 `ICurrentTenant.Id` 是两个不同的来源**，不要混用：

  | | `ICurrentUser.TenantId` | `ICurrentTenant.Id` |
  |---|---|---|
  | 所在包 | `Cike.Security` | `Cike.MultiTenancy` |
  | 类型 | `long?` | `long` |
  | 来源 | JWT 中 `CikeClaimTypes.TenantId`（默认 `"tenant_id"`）claim | `AsyncLocal<long>`（`TenantMiddleware` 写入，或 `Change()` 手动设置） |
  | `Change()` 是否影响 | 否 | 是 |
  | 框架内消费方 | **无**（纯业务只读） | `CikeDbContext` 多租户查询过滤、`EntityHelper` 插入自动填 `TenantId` |
  | 未认证或未挂中间件时 | null | 恒为 0（过滤匹配 `TenantId == 0`，插入填 0） |

  想通过切换租户影响数据过滤，必须走 `ICurrentTenant`。改 token/Claims 只会影响 `ICurrentUser`。
- **不要重复注册 `ICurrentTenantAccessor`**：模块已经注册了 singleton。业务再 `AddSingleton` 一个实例（或者自己 new `CurrentTenantAccessor()`），会产生第二个 `AsyncLocal` 存储，`Change` 互相看不见，租户会静默丢失。
- **租户解析的 >0 门槛只对 Claims 来源生效**：Header/Cookie/Query 只要 `long.TryParse` 成功就采纳，可以把 0 或负数写进 `ICurrentTenant.Id`。租户 Id 约定应为正数，不要依赖这个行为做防御。键名固定为 `"tenant_id"`，旧客户端如果还在用 `tenantid`，必须迁移。
- **扩展方法同名冲突**：`Cike.MultiTenancy.CikeTenantConsts.GetLongValue` 和 `Cike.Security.Extensions.ClaimsPrincipalExtensions.GetLongValue` 签名完全相同。同一文件同时 `using` 这两个命名空间并调用 `principal.GetLongValue(...)` 时会报二义性编译错误，这时改用静态调用形式，或者只 `using` 其中一个。
- **后台任务不要假设继承了请求租户**：把 `tenantId` 显式传递，或者用 `Change` 重建（见隐式行为 6）。
- 单例端点类可以安全地构造器注入 `ICurrentTenant`：它是无状态的 transient，状态在全局 accessor 的 AsyncLocal 里。
