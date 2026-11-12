# Cike.Data.EFCore

EF Core 集成核心：`CikeDbContext` 基类（一大堆自动化）、默认/自定义仓储、DbContext 工厂与 `IUnitOfWork` 实现。数据库方言由 Provider 包（MySql / SqlServer）提供。

## 提供的能力

### `CikeDbContext<TDbContext>` —— 业务 DbContext 基类
- `public class XxxDbContext : CikeDbContext<XxxDbContext>`，**泛型参数必须是自身**；构造函数必须保留 `(DbContextOptions<T>, IServiceProvider)` 双参
- 自动化（全部在 ChangeTracker 钩子里，业务代码禁止手工做）：
  - 新增：雪花/顺序 Guid Id、`TenantId`（`IMultiTenant`，**跟踪时**按当前租户（`ICurrentTenant`，AsyncLocal）写入，`Add` 之后切租户不会改）、`CreatedAt/CreatedBy`
  - 修改：`UpdatedAt/UpdatedBy`、`ConcurrencyStamp` 刷新
  - 删除：软删（`Reload + IsDeleted=true`）
  - 全局查询过滤器：`IMultiTenant` → 按租户、`ISoftDelete` → 排除已删（由 `IDataFilter` 开关，默认开）
  - `SaveChangesAsync` 时聚合根领域事件入队 `IQueueEventBus` 并清空（工作单元提交时发布）；实体状态变化自动开事务（`UnitOfWorkOptions.Enable`）

### 仓储（接口定义在 Cike.Data.Domain）

- `services.AddCikeDbContext<T>()` 后，为每个实现了 `IEntity<TKey>` 的 DbSet 实体**自动注册默认仓储**（`IRepository` / `IBasicRepository` / `IReadOnlyRepository`，Scoped），注入即用：
  ```csharp
  public class OrderService(IRepository<Order, long> orderRepository)
  {
      public async Task DoAsync()
      {
          var order = await orderRepository.GetAsync(id);            // 未找到抛 UserFriendlyException
          var found = await orderRepository.FindAsync(id);           // 未找到返回 null
          var page = await orderRepository.GetPagedListAsync(request, o => o.Title.Contains("x"));
          await orderRepository.InsertAsync(order);                  // autoSave 默认 true
          await orderRepository.DeleteAsync(order.Id);               // 软删实体自动转软删；不存在静默返回
      }
  }
  ```
- **autoSave 语义**：默认 `true` 只负责立即 `SaveChanges`（含领域事件入队）；**事务提交是工作单元的职责**，配合 UoW 时传 `false` 由提交统一保存
- **复杂查询出口**：仓储方法不够用时——
  ```csharp
  var queryable = repository.GetQueryable();                  // 任意 LINQ，软删/多租户过滤器依然生效
  var detailed  = await repository.WithDetailsAsync(o => o.Lines);  // 指定导航预加载
  using (repository.BeginAsNoTracking())                      // 查询侧关闭跟踪，Dispose 恢复
  {
      var dto = await repository.GetListAsync();
  }
  ```
  IQueryable 挂在 scoped DbContext 上，scope 释放后不可再物化；取消令牌在物化处传入
- **自定义仓储**（覆盖默认，零配置）：
  ```csharp
  public interface IOrderRepository : IRepository<Order, long>
  {
      Task<long> CountPendingAsync();
  }

  public class OrderRepository(XxxDbContext dbContext)
      : EfCoreRepository<XxxDbContext, Order, long>(dbContext), IOrderRepository, IScopedDependency
  {
      public Task<long> CountPendingAsync() => GetCountAsync(o => o.Status == OrderStatus.Pending);
  }
  ```
  约定注册先于默认注册（TryAdd 语义），自定义仓储天然接管同实体的 `IRepository` 解析。多个 DbContext 含同一实体类型时先注册者保留，需要特定实现请写自定义仓储。

### 注册与工厂
- `services.AddCikeDbContext<T>()`：注册 `DbContextOptions` 工厂（内部走连接串解析）+ `IUnitOfWork → EFCoreUnitOfWork<T>` + 默认仓储；传 `addDefaultRepositories: false` 可关闭默认仓储
- 【陷阱】不要用 `AddDbContext/AddDbContextPool`——会绕开连接串解析与 UoW 集成

## 模块信息

- 模块类：`CikeDataEFCoreModule`（开启 Snowflake `IsEnable`）
- 直接依赖：`CikeSecurityModule`、`CikeMultiTenancyModule`、`CikeDomainModule`、`CikeUniversalIdModule`、`CikeUowModule`、`CikeEventBusAdaptiveModule`

## 已知技术债

- 领域层存在两份同名聚合根接口（`Cike.Data.IAggregateRoot` 与 `Cike.Data.Domain.AggregateRoots.IAggregateRoot`），实体基类实现的是后者；统一它们是破坏性变更，待规划
- 既有 `ToPaginationAsync` 分页扩展：Page=0/负数偏移未定义、部分路径丢弃 CancellationToken、`GetAsync` 扩展与仓储 `GetAsync` 双轨同形
- `DeleteAsync(id)` 的"不存在静默返回"存在并发窗口（Find 与 Save 之间被并发删除会抛并发异常）；框架尚无统一并发异常翻译层

## 更多

DbContext 标准写法、连接串解析、迁移 factory：[AI 开发指南](../../docs/AI-GUIDE.md) 第 7.2、7.3、7.5 节。
