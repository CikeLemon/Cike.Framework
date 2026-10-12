# CQRS 示例：订单域

基于 Cike.Framework 的完整 CQRS + DDD 示例，演示三大能力：

1. **增删改查**——默认仓储（`IRepository` 全量 / `IReadOnlyRepository` 查询侧只读注入）+ 分页 + 软删
2. **DDD**——聚合根 + 子实体 + 值对象（OwnsOne 映射）+ 领域事件发布与消费（同事务联动）
3. **FluentValidation**——DTO 自动校验（`[AutoValidation]` 端点过滤器 + Validator 自动注册）

## 分层结构

```
CQRS.Domain.Shared        // 枚举等无依赖常量（OrderStatus）
CQRS.Domain               // 聚合根 Order/Buyer、子实体 OrderLine、值对象 Address/Money、领域事件
CQRS.Application.Contracts// DTO（纯数据契约，一类型一文件）
CQRS.Application          // Command、Query、业务事件定义（与 Handler 同层）+ [LocalEventHandler] handler、FluentValidation Validator
CQRS.EntityFrameworkCore  // CqrsDbContext（OwnsOne 映射）、模块（UseSqlite + AddCikeDbContext）
CQRS.Service.Open         // 宿主：模块组装、MinimalAPI 服务、Swagger、EnsureCreated
CQRS.Tests                // xUnit：SQLite in-memory，经 ILocalEventBus 走与 API 等价的完整链路
```

## 运行

```bash
dotnet run --project samples/cqrs/CQRS.Service.Open
# Swagger: http://localhost:5208/swagger
```

SQLite 文件库（`cqrs.db`）开箱即跑，无需外部数据库；建表走启动时 `EnsureCreatedAsync`（生产请改迁移）。

## 请求链路（一次下单）

```
POST /api/v1/Orders
  └─ OrderService.CreateAsync                    [AutoValidation] 先校验 CreateOrderDto
       └─ ILocalEventBus.PublishAsync(CreateOrderCommand)
            └─ DbTransactionLocalEventMiddleware  ← 事务边界（自动提交/回滚）
                 └─ OrderCommandHandler.CreateAsync
                      └─ Order.Place(...)          聚合工厂：校验 + AddDomainEvent(OrderPlacedEvent)
                      └─ repository.InsertAsync    雪花 Id/审计字段自动填充，事件入队
                 └─ IUnitOfWork.CommitAsync        提交前 drain 领域事件队列
                      └─ BuyerEventHandler.OnOrderPlacedAsync   ← 领域事件消费（同事务）
                           └─ Buyer 统计自动创建/累加
                 └─ COMMIT
```

## 接口清单（约定路由：类名去 Service 后缀复数 = /api/v1/Orders）

| 方法 | 路由 | 说明 |
|---|---|---|
| POST | `/api/v1/Orders` | 下单（body: CreateOrderDto），返回雪花 Id |
| GET | `/api/v1/Orders/{id}` | 详情（含值对象地址与订单行） |
| GET | `/api/v1/Orders/List?keyword=&buyerId=&page=&pageSize=&sorting=` | 分页列表 |
| POST | `/api/v1/Orders/Pay/{id}` | 支付（Pending→Paid） |
| POST | `/api/v1/Orders/Ship/{id}` | 发货（Paid→Shipped） |
| POST | `/api/v1/Orders/Cancel/{id}` | 取消（统计回退） |
| DELETE | `/api/v1/Orders/{id}` | 软删（幂等，查询面不可见） |
| GET | `/api/v1/Buyers/{id}` | 买家统计（领域事件维护的读侧数据） |

```bash
curl -X POST http://localhost:5208/api/v1/Orders -H "Content-Type: application/json" -d '{
  "buyerId": 42,
  "address": {"province": "Guangdong", "city": "Shenzhen", "street": "Tech Park", "zipCode": "518000"},
  "lines": [{"productName": "Keyboard", "quantity": 2, "unitPrice": 199.5}]
}'
# → "2098428588603740164"（雪花 Id）
```

## 测试

```bash
dotnet test samples/cqrs/CQRS.Tests
```

- `CrudTests`——增删改查 + 分页 + 软删幂等 + 非法查询
- `DomainEventTests`——下单→买家统计创建、取消→统计回退、非法流转异常回滚
- `ValueObjectTests`——值对象判等 + OwnsOne 持久化往返
- `ValidationTests`——CreateOrderDtoValidator 规则（嵌套地址/集合行）

## 示例中体现的框架要点

- **Command/Query 即事件**：`CreateOrderCommand : Command`（回传 Id 用可写属性），`GetOrderQuery : Query<OrderDto>`（handler 填 `Result`），无独立 Dispatcher
- **handler 写法**：任意类 + `[LocalEventHandler]` 方法，自动注册、自动事务
- **查询侧纪律**：只注入 `IReadOnlyRepository` + `BeginAsNoTracking()`；子实体经 `WithDetails(o => o.Lines)` 预加载（IQueryable 出口）
- **值对象**：继承 `ValueObject` 实现 `GetEqualityComponents`，DbContext 里 `OwnsOne` 映射
- **示例无认证**：服务构造里 `RouteOptions.EnabledAuthorization = false`（默认 true 会加 RequireAuthorization）

## 已知注意点

- 服务类是 Singleton（`MinimalApiServiceBase : ISingletonDependency`），Scoped 依赖（`ILocalEventBus`）必须用 `[FromServices]` 方法参数注入
- long 类型 Id 在 JSON 响应中序列化为字符串（框架 `LongToStringConverter`，防前端精度丢失）
- 校验失败（ValidationProblem）与业务异常（`UserFriendlyException` 纯文本 400）是两种响应形状，前端需分别处理
