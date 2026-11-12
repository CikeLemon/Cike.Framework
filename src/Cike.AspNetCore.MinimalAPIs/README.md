# Cike.AspNetCore.MinimalAPIs

约定式 Minimal API：**零 `app.MapXxx` 的自动路由生成** + 全局 HTTP 行为装配。框架 Web 层的核心。

## 提供的能力

### 自动路由（`MinimalApiServiceBase`）
继承它（含 `ISingletonDependency`）+ public 方法 = 自动注册端点。路由规则：
- 前缀 `api/v1`（`GlobalMinimalApiRouteOptions`）
- 资源名 = 类名去 `Service`/`AppService` 后缀 + 英文复数化（保留大小写）：`FolderService` → `api/v1/Folders`
- HTTP 动词由方法名前缀映射：`Get`/`Find`→GET，`Post`/`Create`/`Add`/`Upsert`→POST，`Put`/`Update`/`Modify`→PUT，`Delete`/`Remove`→DELETE；无匹配前缀默认 **POST**
- 参数名恰好为 `id`（无绑定特性）→ 路由段 `{id}`；其他简单参数走查询串
- 显式覆盖：`[MinimalApiRoute("pattern", "GET")]`；排除方法：`[NonAction]`
- 端点过滤器：`[AutoValidation]` 等继承 `EndpointFilterBaseAttribute`

### 全局行为（模块 InitializeAsync 自动装配）
- `BusinessExceptionMiddleware`：`UserFriendlyException`/`BusinessException` → 400 + 消息文本
- CORS（`appsettings` → `CorsDomains`）
- JSON：`long`/`long?` 序列化为字符串（防前端精度丢失）
- 认证中间件（`GlobalMinimalApiRouteOptions.EnabledAuthorization`）
- 【陷阱】端点**默认全部 `RequireAuthorization`**——没配认证时会 500（缺中间件或缺认证方案），配置了 JWT 但缺 token 才是 401。开发期一步关闭：全局 `EnabledAuthorization = false`（总开关，同时关中间件与端点元数据）；个别服务免认证则在全局开启时设 `RouteOptions.EnabledAuthorization = false`

### 宿主引导扩展
- `WebApplication.InitializeApplicationAsync()`：触发所有模块 `InitializeAsync` + 注册 Shutdown 钩子
- `app.UseMultiTenant()`：挂载 `Cike.MultiTenancy` 的 `TenantMiddleware`（不自动挂载；在宿主模块 `InitializeAsync` 中调用即位于认证中间件之后）
- `ApplicationInitializationContext.GetApplicationBuilder()` / `GetEndpointRouteBuilder()`

## 模块信息

- 模块类：`CikeAspNetCoreMinimalApiModule`
- 直接依赖：`CikeMultiTenancyModule`（**不**带入 `CikeSecurityModule`；需要 `ICurrentUser` 又不依赖 `CikeDataEFCoreModule` 时宿主自行 DependsOn）

## 更多

路由推导表与端点写法：[AI 开发指南](../../docs/AI-GUIDE.md) 第 6 节。
