# Cike.Auth

当前用户/租户上下文（基于 ASP.NET Core Claims）与租户解析中间件。**不做认证**——JWT 的注册与配置由业务宿主自己写（见 sample `CQRS.WebApi`）。

## 提供的能力

| 类型 | 说明 |
|---|---|
| `ICurrentUser` / `CurrentUserContext` | 从 Claims 读 `Id`、`UserName`、`Name`、`PhoneNumber`、`Email`、`TenantId`（long）、`Roles`；单例，经 `IHttpContextAccessor` 取当前请求 |
| `CikeClaimTypes` | Claim/请求键名约定，如 `TenantId = "tenantid"` |
| `TenantMiddleware` | 解析租户 Id：**JWT Claims → Header `tenantid` → Cookie → Query**（long），成功后 `ICurrentTenant.Change(tenantId)` 包裹请求 |
| `ICurrentTenant` / `ICurrentTenantAccessor` | 当前租户（long，`AsyncLocal`），`Change(tenantId)` 返回 `IDisposable` 恢复 |

启用方式：`TenantMiddleware` 不自动挂载，宿主模块 `InitializeAsync` 中调用 `app.UseMultiTenant()`（扩展方法在 `Cike.AspNetCore.MinimalAPIs` 包）。

【陷阱】`ICurrentUser.TenantId`（Claims 来源，`CikeDbContext` 全局查询过滤用它）与 `ICurrentTenant.Id`（`TenantMiddleware` 来源，缓存键等用它）**是两个不同的来源**——`Change` 只影响后者。不调用 `UseMultiTenant()` 时 `ICurrentTenant.Id` 恒为 0。

## 模块信息

- 模块类：`CikeAuthModule`（无逻辑）
- 直接依赖：无

## 更多

多租户与查询过滤器：[AI 开发指南](../../docs/AI-GUIDE.md) 第 7.1、8.4 节。
