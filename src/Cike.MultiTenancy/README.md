# Cike.MultiTenancy

当前租户上下文（`AsyncLocal<long>`）与租户解析中间件。不依赖 `Cike.Security`。

## 提供的能力

| 类型 | 说明 |
|---|---|
| `ICurrentTenant` / `ICurrentTenantAccessor` | 当前租户（long，`AsyncLocal`，默认 0），`Change(tenantId)` 返回 `IDisposable`，Dispose 恢复进入前的值 |
| `TenantMiddleware` | 解析租户 Id：**Claims → Header → Cookie → Query**，键固定为 `tenant_id`（long），解析值与当前不同时以 `ICurrentTenant.Change(tenantId)` 包裹请求 |
| `CikeTenantConsts` | `TenantIdKey = "tenant_id"`（常量，不可配置） |

启用方式：`TenantMiddleware` 不自动挂载，宿主模块 `InitializeAsync` 中调用 `app.UseMultiTenant()`（扩展方法在 `Cike.AspNetCore.MinimalAPIs` 包），此时它位于认证中间件之后，Claims 分支才能读到 JWT。

【陷阱】`ICurrentTenant.Id`（本包，`CikeDbContext` 多租户过滤与插入填 TenantId 用它）与 `ICurrentUser.TenantId`（`Cike.Security`，JWT claim 只读投影）**是两个不同的来源**——`Change` 只影响前者。不调用 `UseMultiTenant()` 时 `ICurrentTenant.Id` 恒为 0。

【陷阱】不要再注册一个 `ICurrentTenantAccessor`——第二个 `AsyncLocal` 存储会让 `Change` 互相不可见。

## 模块信息

- 模块类：`CikeMultiTenancyModule`（注册 `ICurrentTenantAccessor` 单例）
- 直接依赖：无

## 更多

[AI 开发文档：当前用户与多租户](../../docs/ai/auth.md)
