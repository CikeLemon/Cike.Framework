# 接入指引：让 AI 助手使用本套文档

本页说明如何在**基于 Cike.Framework 开发的业务项目**中接入这套 AI 参考文档。

## 接入方式（一步）

在业务项目解决方案根目录的 `CLAUDE.md`（或 `AGENTS.md`）中加入以下内容：

```markdown
## 框架参考

本项目基于 Cike.Framework 开发。开始任何框架相关编码前，先抓取并遵循：

    https://raw.githubusercontent.com/MapleWithoutWords/Cike.Framework/main/docs/ai/README.md

按其路由表按需抓取同目录下的能力域详解文档（如 data-access.md、events-cqrs.md）。
框架行为以文档与源码为准，不要凭训练记忆推测；文档与源码冲突时以源码为准。
```

就这些。大纲内已包含阅读协议、路由表和全局约定，业务项目里不需要复制任何文档内容——远程引用保证永远读到最新版。

## 文档地图

| 文件 | 内容 |
|---|---|
| [README.md](./README.md) | 大纲：阅读规则、框架速览、能力域路由表、全局分层规范、隐式行为汇总、跨模块反模式 |
| [framework-core.md](./framework-core.md) | 模块系统、自动 DI、异常体系、DTO 契约 |
| [http-api.md](./http-api.md) | 自动路由、Swagger、参数校验 |
| [data-access.md](./data-access.md) | 实体基类、仓储、DbContext 自动化、事务、方言 |
| [events-cqrs.md](./events-cqrs.md) | Command/Query、领域事件、Saga、后台事件 |
| [auth.md](./auth.md) | 当前用户（Cike.Security）/ 多租户（Cike.MultiTenancy）上下文 |
| [caching.md](./caching.md) | Redis / 两级缓存 |
| [locks.md](./locks.md) | 进程内锁 / 分布式锁 |
| [id-generation.md](./id-generation.md) | 雪花 Id / 顺序 Guid |
| [localization.md](./localization.md) | 本地化（现状使用率低） |

## 维护约定（对框架维护者）

- `docs/ai/` 的文件名与路径是**对外 API**（业务项目 CLAUDE.md 直接引用其 URL），禁止重命名或移动。
- 修改 `src/**` 公共 API 必须同步更新对应文档（规则见仓库根 `CLAUDE.md`）。
- 每份详解文档按包分章、每章六节（定位/能力清单/隐式行为/示例/配置/边界与反模式），新增包按此结构追加。
