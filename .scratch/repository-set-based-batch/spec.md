---
title: 仓储 Set-based 批量操作（ExecuteUpdate / ExecuteDelete）
labels: [ready-for-agent]
---

# 仓储 Set-based 批量操作（ExecuteUpdate / ExecuteDelete）

> **范围调整（2026-10-13）**：批量更新整体挂起——setter 参数形态未定（"list of propertyExpression" 的极简方案待议），本期仅交付批量删除（`ExecuteDeleteAsync` ×2）。软删改写用最小实现：接口强转式静态 lambda（`(p as ISoftDelete)!.IsDeleted`），并写更新审计（UpdatedAt/UpdatedBy）；并发戳仍不刷（下文涉及 ExecuteUpdate / setter 包装 / 并发戳 bump 的条目随挂起失效，恢复时重议）。

## Problem Statement

业务方需要按条件批量修改或删除一批数据时，现有仓储只有 tracked 批量方法（`UpdateManyAsync` / `DeleteManyAsync`）：必须先把实体全部加载进内存，再逐行写库。一条业务意图变成"1 条查询 + N 条写入"，大表场景下内存占用与耗时都不可接受。

EF Core 7+ 原生提供 `ExecuteUpdateAsync` / `ExecuteDeleteAsync`（单条 SQL，set-based，不加载实体），但业务方若绕过仓储直接使用：

- 会打破框架的隐式契约——对 `ISoftDelete` 实体会直接物理删除，而不是软删除
- 审计字段（UpdatedAt / UpdatedBy）没人填充，并发戳（ConcurrencyStamp）不刷新
- 领域层仓储接口（`Cike.Data.Domain`）没有这层能力，业务被迫在"守契约"与"高性能"之间二选一

## Solution

在仓储写接口上新增 4 个 set-based 方法：`ExecuteDeleteAsync`（谓词式 / IQueryable 透传式）与 `ExecuteUpdateAsync`（谓词式 / IQueryable 透传式）。业务方按条件发起批量操作，一条 SQL 落库，不加载实体。

框架自动维持既有隐式契约：

- `ISoftDelete` 实体的 set-based 删除自动改写为软删除（与 tracked 删除语义完全一致）
- 审计字段自动追加进 UPDATE 的 SET 子句
- `IHasConcurrencyStamp` 实体的并发戳自动 bump
- 全局查询过滤器（软删过滤、多租户过滤）继续生效，跨租户误删与重复删已删行都有默认防护

## User Stories

1. 作为业务开发者，我想用 `ExecuteDeleteAsync(predicate)` 按条件批量删除且不把实体加载进内存，这样大表清理一条 SQL 完成，不再有 1 查 + N 写的性能悬崖
2. 作为业务开发者，我想用 `ExecuteDeleteAsync(query)` 传入自行组装的 `IQueryable`（含 join / 子查询），这样复杂跨表条件的批量删除不受谓词表达力限制
3. 作为业务开发者，我想用 `ExecuteUpdateAsync(predicate, setters)` 按条件批量更新指定列，这样只改一两个列的场景不用加载整批实体再逐行保存
4. 作为业务开发者，我想用 `ExecuteUpdateAsync(query, setters)` 传入自行组装的 `IQueryable` 做批量更新，这样复杂跨表条件的批量更新同样可达
5. 作为业务开发者，我想用常量值 setter（`SET Status = 'Shipped'` 这种），这样"把一批单据置为同一状态"的常见场景一行代码完成
6. 作为业务开发者，我想用按行计算的值 setter（`SET Total = Total + X` 引用同行列），这样相对量调整不必先读出旧值
7. 作为业务开发者，我想一次调用里设置多列，这样一条 SQL 同时完成多个字段的批量变更
8. 作为业务开发者，我想 setter 的属性选择器与值在编译期类型对齐（写错类型编译不过），这样批量更新不会在运行时才发现列与值不匹配
9. 作为业务开发者，我想方法返回受影响的行数，这样批量操作的结果可观测、可校验（例如"预期删 100 行只删了 3 行"能被发现）
10. 作为业务开发者，我想对 `ISoftDelete` 实体执行 set-based 删除时自动转为软删除，这样批量删除与单条删除（`DeleteAsync`）语义完全一致，不存在"批量路径误物理删"的数据事故面
11. 作为业务开发者，我想软删后默认查询（`GetListAsync` / `GetCountAsync` 等）自动不可见这些行，这样批量软删与现有查询过滤行为无缝衔接
12. 作为业务开发者，我想 set-based 写操作自动刷新更新审计字段（UpdatedAt / UpdatedBy），这样审计数据不出现"改了数据但不知道谁改的"空洞
13. 作为业务开发者，我想对 `IHasConcurrencyStamp` 实体的 set-based 更新与软删自动 bump 并发戳，这样内存中其他已跟踪副本不会在后续 tracked 保存时静默覆盖批量变更
14. 作为业务开发者，我想 set-based 操作立即执行、无需 `autoSave` 参数，这样"执行即落库"的心智不与 tracked 方法的挂起语义混淆
15. 作为业务开发者，我想 set-based 操作处于工作单元时自然加入环境事务，这样批量操作与同事务内其他写操作要么全成要么全滚
16. 作为业务开发者，我想条件无匹配时返回 0 静默完成，这样幂等重试场景不需要额外防御
17. 作为业务开发者，我想全局查询过滤器（多租户、软删）对 set-based 操作继续生效，这样框架默认就防止我跨租户误删、误改已删行
18. 作为业务开发者，我想框架文档明示 set-based 操作**不触发领域事件、不做行级乐观并发校验**，这样我在"要事件/要并发防护"与"要性能"之间能做知情取舍
19. 作为 CQRS 查询侧开发者，我想 `IReadOnlyRepository` 上没有这些方法，这样查询侧依旧编译期无法执行写操作（set-based 写与 tracked 写一样只存在于写接口）
20. 作为库维护者，我想这 4 个方法定义在 `Cike.Data.Domain` 的仓储接口上且领域层保持零 EF Core 依赖，这样抽象层不被单一 ORM 绑死
21. 作为库维护者，我想 EF 的 `SetPropertyCalls` 类型只出现在实现内部（pair 折叠成链），这样 EF 升级改 API 形状时业务代码不动
22. 作为库维护者，我想审计/软删/并发戳的判定复用现有 `EntityHelper` / `CikeDbContext` 的同款闭泛型判定逻辑，这样隐式契约只有一份事实来源
23. 作为自定义仓储作者，我想这些方法在 `EfCoreRepository` 上是 `virtual`，这样自定义仓储可以按领域语义重写（例如拦截特定条件的批量删除）
24. 作为测试工程师，我想从 DI 容器解析仓储、打真实 SQLite in-memory 库做集成测试，这样测试走与生产完全相同的注册与执行链路
25. 作为框架使用者，我想 `docs/ai/data-access.md` 同步说明这批方法的行为与边界，这样 AI 辅助开发时不会凭空臆测批量操作的软删/审计行为

## Implementation Decisions

- **接口位置与形态**：4 个方法加在 `IBasicRepository<TEntity, TKey>`（`IRepository` 经继承自然获得，`IReadOnlyRepository` 不含，维持 CQRS 读侧只读）。方法名沿用 EF 原生命名 `ExecuteDeleteAsync` / `ExecuteUpdateAsync`——`Execute*` 前缀即词汇表中 Set-based 操作的标记，与 tracked 系（`DeleteAsync` / `DeleteManyAsync`）形成命名分界
- **返回值**：`Task<int>`（受影响行数；软删改写后即软删行数）。无 `autoSave` 参数——立即执行，无 SaveChanges 环节；处于工作单元时自然加入环境事务
- **谓词式组装**：框架内部从 `GetQueryable()` 追加谓词后执行——全局查询过滤器（软删过滤、多租户过滤）自动生效。IQueryable 透传式原样执行调用方查询（EF 对含过滤器的查询仍应用过滤器；调用方显式 `IgnoreQueryFilters` 则自担后果）
- **软删改写**：实体实现 `ISoftDelete` 时，`ExecuteDeleteAsync` 不生成 DELETE，而是对同一查询执行 `ExecuteUpdateAsync`：SET `IsDeleted = true`、`UpdatedAt = now`、`UpdatedBy = 当前用户`（注意：本库的删除审计就是更新审计 + IsDeleted 标记，**没有**独立的 DeleterId / DeletionTime 列，不要引入 ABP 的字段假设）；实现 `IHasConcurrencyStamp` 则追加 SET `ConcurrencyStamp = 新值`。非软删实体走 EF 原生 DELETE
- **更新审计自动追加**：实体实现 `IAuditedEntity<long>` 或 `IAuditedEntity<Guid>`（闭泛型判定，与 `EntityHelper.SetUpdateAuditedProperty` 同款）时，框架在调用方 setter 之后自动追加 `UpdatedAt` / `UpdatedBy` 两个 pair。软删改写复用同一追加机制
- **并发戳 bump 一致性**：`ExecuteUpdateAsync` 对 `IHasConcurrencyStamp` 实体同样自动 bump 并发戳——与软删改写、tracked 更新路径（`UpdateConcurrencyStamp`）三方行为对齐（此项是会话中既定决策"软删改写 bump 戳"的直接推论，实现与评审时按此执行）
- **setter 形态（关键分层决策）**：领域接口不出现 EF 的 `SetPropertyCalls` 类型。setter 是中立不可变 pair：属性选择器 `Expression<Func<TEntity, TProperty>>` + 值（常量或按行 `Expression<Func<TEntity, TProperty>>`），泛型同型保证编译期对齐；工厂方法提供常量值 / 按行表达式两个重载。`Cike.Data.Domain` 保持零 EF Core 依赖
- **pair 折叠**：EFCore 实现内部把 pair 序列折叠为 `SetPropertyCalls<TEntity>` 链（自内向外构造嵌套 `SetProperty` MethodCall，按 pair 的 lambda 返回类型 `MakeGenericMethod`，常量值映射到 EF 的 value 重载、表达式映射到 valueExpression 重载），然后调用 EF 原生 `ExecuteUpdateAsync`
- **已声明的边界行为**（随实现写入 `docs/ai/data-access.md`）：不触发领域事件（不经过 ChangeTracker，`EnqueueDomainEventAsync` 无条目可扫）；不做行级乐观并发校验（只 bump 戳，不校验旧戳）；模型配置的客户端级联不触发（子表行是否删除取决于数据库端外键级联——实现时需用测试钉住含 owned/子表实体的删除行为）

## Testing Decisions

- **唯一接缝**：`IRepository` 接口，打 `CikeEfCoreTestHost`（共享单连接 SQLite in-memory + 完整模块加载 + `AddCikeDbContext`，与生产同构）。**不新增接缝**：软删改写是私有实现细节，只从外部可观察行为验证，不 mock DbContext、不暴露内部 API
- **好测试的标准**：只断言外部可观察行为——返回行数、默认过滤器下的可见性、禁用过滤器后的行状态（IsDeleted / 审计字段 / 并发戳字段值）；不断言生成的 SQL 形状或改写表达式的内部结构
- **先例**：`tests/Cike.Data.EFCore.Tests/WriteTests.cs` 的既有模式——`IClassFixture<CikeEfCoreTestHost>`、类内共享库 + 唯一数据隔离、`SeedAsync` 造数、跨 Scope 验证、`IDataFilter.Disable<ISoftDelete>()` 查行状态、`FakeCurrentUser` 设当前用户后复位
- **测试实体复用**：`Category`（非软删、非聚合根）验证物理删除路径；`Order`（`ISoftDelete` + `IHasConcurrencyStamp`）验证软删改写路径
- **用例清单**：谓词式/透传式 × 物理/软删 的删除（4 例，覆盖返回行数、过滤器可见性、禁过滤器后行状态与审计字段）；并发戳 bump（删除与更新各 1）；审计字段自动追加（更新场景，设置 FakeCurrentUser 后断言 UpdatedBy）；常量 setter 与按行表达式 setter 各 1；多列一次 SET；0 匹配返回 0；多租户过滤器下删除不越租户（SetTenant 后断言他租户行存活）；领域事件不入队（聚合根挂事件后 ExecuteUpdate/Delete，断言队列空——文档化例外的回归锚）

## Out of Scope

- 批量插入（tracked 的 `InsertManyAsync` 已覆盖；如需 set-based 意义的"批量插入"另行立项）
- 同步版本方法（现有仓储写方法以 Async 为主，不补同步对偶）
- 谓词式跨表 join（谓词限定单实体条件；join 场景走 IQueryable 透传式，不在谓词上做表达式魔改）
- 第二存储实现（Dapper / Mongo 等）；届时 set-based 方法本就需要按存储能力重新设计
- 领域事件的 set-based 补发机制（如"删除前加载并发布事件"违背 set-based 初衷，明确不做）
- EF 原生能力的全量透传（如 `IgnoreQueryFilters`、`TagWith` 等——透传式查询上调用方自行使用，框架不封装）
- 批量更新中对 `UpdatedAt`/`UpdatedBy` 之外字段的任何框架级自动改写

## Further Notes

- **文档同步义务**（CLAUDE.md 约定，同一提交内完成）：更新 `docs/ai/data-access.md` 的仓储章节（新方法、软删改写、审计追加、边界行为）；隐式行为新增了"set-based 删除改写软删""set-based 更新追加审计"两条，需核对 `docs/ai/README.md` 的隐式行为清单是否需要同步
- **术语**：本能力引入的词汇分界（tracked 删除 vs Set-based 删除、软删改写）已沉淀至仓库根 `CONTEXT.md`，实现与文档措辞遵循该表
- **EF 版本基线**：`ExecuteUpdateAsync` / `ExecuteDeleteAsync` 要求 EF Core 7+；框架当前 EF 依赖已满足，无需升级
- **术语纠正记录**：早期讨论口头提到的"DeleterId / DeletionTime"是 ABP 的字段，本库删除审计实际为 `UpdatedAt` / `UpdatedBy` + `IsDeleted`，以本 spec 为准
