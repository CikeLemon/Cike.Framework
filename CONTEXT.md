# Cike.Framework

.NET 业务框架：以 EF Core 为唯一存储实现，提供仓储、工作单元、审计、软删除、多租户等隐式契约。

## Language

### 删除

**Tracked 删除**:
基于已加载实体实例的删除（`DeleteAsync` / `DeleteManyAsync`），实体进入 ChangeTracker，软删转换与审计填充在 SaveChanges 前由框架钩子完成，领域事件可触发。
_Avoid_: 普通删除、常规删除

**Set-based 删除**:
按条件直接在数据库端执行、不加载实体实例的删除（`ExecuteDeleteAsync`），绕过 ChangeTracker；对 `ISoftDelete` 实体由框架改写为软删 UPDATE。
_Avoid_: 原生批量删除、硬删（对 ISoftDelete 实体并不成立）

**软删改写**:
Set-based 删除遇到 `ISoftDelete` 实体时，框架自动将 DELETE 改写为 UPDATE（置 `IsDeleted=true` 并写更新审计），使其语义与 Tracked 删除一致；并发戳不在改写范围内（与 Tracked 删除的差异点）。
