# Cike.Security

当前用户上下文（基于 ASP.NET Core Claims）。**不做认证**——JWT 的注册与配置由业务宿主自己写；租户上下文在 [Cike.MultiTenancy](../Cike.MultiTenancy/README.md)。

## 提供的能力

| 类型 | 说明 |
|---|---|
| `ICurrentUser` / `CurrentUserContext` | 从 Claims 读 `Id`、`UserName`、`Name`、`NickName`、`PhoneNumber`、`Email`、`TenantId`（long?）、`Roles`；单例，经 `IHttpContextAccessor` 取当前请求 |
| `CikeClaimTypes` | Claim 键名约定（静态可写），默认 OIDC 短键：`UserId = "sub"`、`UserName = "username"`、`Name = "name"`、`NickName = "nickname"`、`Role = "role"`、`Email = "email"`、`PhoneNumber = "phone_number"`、`TenantId = "tenant_id"` |
| `ICurrentUserExtensions` | `GetLongId()` / `GetGuidId()`（`Id` 为 null 时抛异常） |
| `ClaimsPrincipalExtensions` | `FindClaims` / `GetValue` / `GetGuidValue` / `GetLongValue` |

【陷阱】`ICurrentUser.TenantId` 只是 JWT claim 的只读投影，框架数据层**不**用它过滤——多租户过滤与插入填值读的是 `Cike.MultiTenancy` 的 `ICurrentTenant.Id`。

【陷阱】`CikeAspNetCoreMinimalApiModule` 不依赖本模块；只用 MinimalAPIs、未依赖 `CikeDataEFCoreModule` 的宿主要注入 `ICurrentUser` 时须自行 `[DependsOn(typeof(CikeSecurityModule))]`。非 Web 宿主须自行 `AddHttpContextAccessor()`。

## 模块信息

- 模块类：`CikeSecurityModule`（无逻辑）
- 直接依赖：无

## 更多

[AI 开发文档：当前用户与多租户](../../docs/ai/auth.md)
