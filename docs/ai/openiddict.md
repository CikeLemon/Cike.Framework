# OpenIddict 资源服务器：token 校验与端点权限

## 域定位

提供**资源服务器**能力：接入 OpenIddict Validation 校验外部签发的 token（签名/签发方/受众/有效期），产出标准 `ClaimsPrincipal`；在此之上提供端点级权限声明——`[RequirePermission("code")]` 标在端点方法上，经 ASP.NET Core 标准授权管线最终调用框架内的 `IPermissionChecker` 判定。

**不是授权服务器**：本域不签发 token、没有 `/connect/*` 端点、没有客户端注册与 OpenIddict 实体落库——token 由框架外的 IdP/网关签发。也不含权限存储（权限判定逻辑在框架内留空待补，见隐式行为 2）。

与 [认证与当前用户](./auth.md) 的分工：`Cike.Auth` 只**消费**认证结果（`ICurrentUser` 读 Claims）；本域负责**产出**认证结果（token 校验）与端点级授权（权限特性）。

什么时候不用：token 只由业务宿主自己注册 JwtBearer 校验且不需要端点级权限特性的项目，不必引入本包；只想签发 token 的场景本包不适用。

## 覆盖的包

| 包 | 目录 | 定位 |
|---|---|---|
| `Cike.OpenIddict` | `src/Cike.OpenIddict` | `CikeOpenIddictModule`（校验方案注册 + 授权管线零件）、`[RequirePermission]` 端点特性、`IPermissionChecker`/stub、claim 键名映射 |

包级依赖：项目 `Cike.Auth`、`Cike.AspNetCore.MinimalAPIs`（读全局认证总开关）、`Cike.Core`；NuGet `OpenIddict.Validation.AspNetCore` + `OpenIddict.Validation.SystemNetHttp` 7.7.2（仅校验侧，不含 Server）。模块级：`CikeOpenIddictModule` `[DependsOn(CikeAuthModule)]`。

## Cike.OpenIddict

### 定位

- token 校验：`AddOpenIddict().AddValidation()` 注册 ASP.NET Core 认证方案，issuer 发现或逃生舱口自定义
- 端点权限：特性 → 动态策略（按策略名现场解析，无需预注册）→ 授权处理器 → `IPermissionChecker`
- claim 对齐：校验成功后把签发方短名 claim 重写为 `CikeClaimTypes` 约定键名

### 能力清单

```csharp
// 模块
[DependsOn([typeof(CikeAuthModule)])]
public class CikeOpenIddictModule : CikeModule { }

// 配置（DI Configure 注入）
public class CikeOpenIddictValidationOptions
{
    string? Issuer { get; set; }                 // 签发方地址；必配（否则启动抛异常），走 OIDC 发现
    string? Audience { get; set; }               // 接受的 audience（token aud 必须匹配）
    Dictionary<string, string> ClaimTypeMapping { get; }  // claim 键名映射覆盖（叠加在默认映射之上）
    Action<OpenIddictValidationOptions>? Configure { get; set; }  // 逃生舱口：完全自定义原生校验选项
}

// 端点特性（标在端点方法上，AllowMultiple）
public class RequirePermissionAttribute : Attribute, IAuthorizeData
{
    public RequirePermissionAttribute(string permission);  // → 策略名 "Cike:Permission:{permission}"
    public string? Policy { get; set; }
    public string? Roles { get; set; }
    public string? AuthenticationSchemes { get; set; }
}

// 权限判定（框架内留空，业务不实现）
public interface IPermissionChecker
{
    Task<bool> IsGrantedAsync(string permission);   // stub 恒返回 false
}
```

- `PermissionPolicyProvider : IAuthorizationPolicyProvider`：`"Cike:Permission:"` 前缀的策略名现场构造策略（`RequireAuthenticatedUser` + `PermissionRequirement`），结果缓存；非前缀策略名交还 `DefaultAuthorizationPolicyProvider`
- `PermissionAuthorizationHandler`：调 `IPermissionChecker`，true 才 `Succeed`，否则 403
- `ClaimTypeMappingHandler`（OpenIddict `ProcessAuthenticationContext` 事件）：校验成功后重写 claim 键名，默认映射 `sub→CikeClaimTypes.UserId`、`name→UserName`、`given_name→Name`、`family_name→SurName`、`email→Email`、`phone_number→PhoneNumber`、`role→Role`；`ClaimTypeMapping` 中的条目覆盖默认值；映射后 Identity 的 `NameClaimType`/`RoleClaimType` 对齐 `CikeClaimTypes.UserId`/`Role`

### 隐式行为

1. **注册方式**：`IAuthorizationPolicyProvider`/`IAuthorizationHandler`/`IPermissionChecker` 由模块手动注册 singleton（不走约定注册，替换 stub 时保持确定性）。模块同时无条件调用 `AddAuthorization()` 与 `AddAuthentication()`——因此 **WebApplication 宿主会自动挂载认证/授权中间件**（平台行为），`GlobalMinimalApiRouteOptions.EnabledAuthorization` 的"关 → 不挂中间件"在本包引入后不再成立（见 3）。
2. **stub 恒 false**：`PermissionChecker.IsGrantedAsync` 留空、恒返回 false——补全判定逻辑之前，标注 `[RequirePermission]` 的端点对**任何**已认证用户一律 403（fail-closed 安全兜底）。补全后无需业务侧任何改动。
3. **两级开关语义**：
   - **全局总开关** `GlobalMinimalApiRouteOptions.EnabledAuthorization = false`：端点不加默认认证要求（[HTTP 接口层](./http-api.md)），且权限策略降级为**允许模式**（`[RequirePermission]` 不强制，200）——开发期一键全开；
   - **端点级开关** `RouteOptions.EnabledAuthorization = false`：只关掉该服务端点的默认"要求登录"，方法上**显式声明的** `[RequirePermission]`/`[Authorize]` 仍强制执行（显式要求不被服务级开关绕过）。
4. **claim 映射在认证后重写**：`HttpContext.User`/`ICurrentUser` 读到的是映射后的键名（`CikeClaimTypes` 约定）；签发方原始短名 claim 不再保留。映射表在**执行时**与 `CikeClaimTypes` 静态属性求值——启动早期改写 `CikeClaimTypes` 对映射同样生效。
5. **issuer 必配**：未配置 Issuer（且逃生舱口未提供静态配置）时 OpenIddict 在启动期抛异常（"OAuth 2.0/OpenID Connect server configuration or an issuer URI must be registered"），属 fail-fast。
6. **校验方式默认走发现**：配置 Issuer 后 OpenIddict 经 OIDC discovery（`.well-known/openid-configuration` + JWKS）拉取签名密钥，本地验签；需要 introspection/本地密钥/静态配置时走逃生舱口。
7. **角色走标准管线**：token 的 `role` claim 经映射变为 `ClaimTypes.Role`，内置 `[Authorize(Roles = "admin")]` 直接可用，本包不提供角色特性。
8. **权限特性不进 MinimalAPIs 扫描器**：`[RequirePermission]` 实现 `IAuthorizeData`，经 .NET minimal API 对 handler 方法特性的自动元数据复制进入授权管线，MinimalAPIs 模块无需任何改动。

### 示例

宿主模块配置与端点使用：

```csharp
using Cike.OpenIddict;
using Cike.OpenIddict.Authorization;
using Microsoft.AspNetCore.Authorization;

[DependsOn(typeof(CikeOpenIddictModule))]
public class HostServiceModule : CikeModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.Configure<CikeOpenIddictValidationOptions>(o =>
        {
            o.Issuer = "https://idp.example.com";   // 签发方，走 discovery
            o.Audience = "order-api";
            // 签发方键名非标准时追加映射：
            // o.ClaimTypeMapping["upn"] = CikeClaimTypes.UserId;
        });
        return base.ConfigureServicesAsync(context);
    }
}

public class OrderService : MinimalApiServiceBase
{
    [RequirePermission("Order.Create")]   // 判定逻辑补全前一律 403
    public Task CreateOrderAsync(...) { ... }

    [Authorize(Roles = "admin")]          // 标准角色授权，立即生效
    public Task DeleteOrderAsync(...) { ... }

    [RequirePermission("Order.Read")]     // 多特性叠加 = 全部满足才放行
    [RequirePermission("Order.Export")]
    public Task ExportAsync(...) { ... }
}
```

逃生舱口（本地签名密钥 + 静态配置，免发现请求）：

```csharp
context.Services.Configure<CikeOpenIddictValidationOptions>(o =>
{
    o.Issuer = "https://idp.example.com";
    o.Configure = validation =>
    {
        validation.SigningCredentials.Add(new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256));
        validation.ConfigurationManager = new StaticConfigurationManager<OpenIddictConfiguration>(staticConfig);
    };
});
```

### 配置

| 项 | 说明 |
|---|---|
| `CikeOpenIddictValidationOptions.Issuer` | 签发方地址，必配；走 OIDC discovery 拉签名密钥 |
| `CikeOpenIddictValidationOptions.Audience` | 接受的 audience；不配则不校验 aud |
| `CikeOpenIddictValidationOptions.ClaimTypeMapping` | claim 键名映射覆盖（默认表见能力清单） |
| `CikeOpenIddictValidationOptions.Configure` | 逃生舱口：原生 `OpenIddictValidationOptions` 完全自定义 |
| `GlobalMinimalApiRouteOptions.EnabledAuthorization` | HTTP 域全局总开关（默认 true）；false 时权限策略降级允许模式，见隐式行为 3 |
| 框架 options / appsettings 绑定 | 无——本包不读任何配置节，全部 DI Configure |

### 边界与反模式

- **核心陷阱：stub 恒 403**。判定逻辑补全前，所有 `[RequirePermission]` 端点对已认证用户也返回 403——这不是故障，是 fail-closed 设计。排查"端点莫名 403"先确认判定逻辑是否已补全。
- **不要把权限 code 塞进 token claim**：权限数量大时 token 膨胀（曾评估并被否决的方案）；权限判定属于 `IPermissionChecker` 的职责，claim 只放身份与少量角色。
- **不要在业务项目实现 `IPermissionChecker`**：判定逻辑在框架内补全（stub 替换），业务注册自己的实现会与模块注册冲突（后注册者覆盖，行为不确定）。
- **`ICurrentUser` 读到的是映射后键名**：排查 claim 问题时先过一遍 `ClaimTypeMappingHandler` 的映射表，签发方的原始短名（`sub`/`role`）在校验后被重写。
- **`Configure` 逃生舱口后执行**：与 `Issuer`/`Audience` 叠加时本类属性先应用、委托后应用——委托里可覆盖前两者。
- **总开关关闭 ≠ 全免认证**：WebApplication 自动挂载认证/授权中间件（本包注册了认证/授权服务），全局关掉后免掉的只有端点默认"要求登录"与权限特性强制；`[Authorize]` 元数据仍由平台标准管线执行（未认证仍 401）。
- **非 WebApplication 宿主**（如纯 `HostBuilder` + 手动装配管道）：认证中间件不会自动挂载，需自行 `UseAuthentication`/`UseAuthorization`，否则权限特性不生效。
- **Swagger 无需额外配置**：Bearer 安全定义由 `Cike.AspNetCore.Swagger` 全局提供（见 [HTTP 接口层](./http-api.md)），受权限保护的端点在 SwaggerUI 中照常通过右上角 Authorize 粘贴 token 调试。
