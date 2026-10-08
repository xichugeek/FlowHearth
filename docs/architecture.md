# FlowHearth 技术架构

## 1. 目标与边界

FlowHearth 服务于中小型自动化项目团队，覆盖从客户线索到项目交付、设备履历、售后服务和经营财务的闭环。系统定位是项目型业务管理平台，不是会计 ERP、库存/WMS、MES、PLC 实时采集平台或通用工作流引擎。

架构优先级是：业务一致性、安全审计、低运维成本、可测试性和单机可部署性。

## 2. 运行时拓扑

```text
Browser
   |
 HTTPS
   v
Nginx ------------------------------------ static Vue assets
   |
   +---- /api/* -> Kestrel 127.0.0.1:5100
                         |
                         +---- MySQL (private network / loopback)
                         |
                         +---- /opt/flowhearth/shared/uploads
                         +---- Data Protection key ring
```

- SPA 和 API 使用同一来源，减少 CORS 与 Cookie 配置复杂度。
- Nginx 是唯一公网入口；Kestrel 不应直接暴露。
- MySQL 使用独立数据库与最小权限运行账户。
- 附件、日志和 Data Protection 密钥位于不可变 release 目录之外。
- 生产环境不需要 Node.js 运行时。

## 3. 后端分层

```text
FlowHearth.Api
      |                HTTP、认证、授权、Problem Details、健康检查
      v
FlowHearth.Application
      |                用例、DTO、权限策略、事务边界接口
      v
FlowHearth.Domain
                       领域枚举、状态机、金额与编号规则

FlowHearth.Infrastructure
      ^                Dapper/MySQL、文件存储、查询实现
      |
Application abstractions
```

依赖方向：

- `Api -> Application + Infrastructure`
- `Infrastructure -> Application + Domain`
- `Application -> Domain`
- `Domain` 不依赖其他项目

`FlowHearth.DbMigrator` 复用基础设施层的迁移与管理员引导能力，但与 API 进程分开执行。API 启动时不会自动修改数据库结构。

## 4. 业务模块

- 安全：用户、角色、权限、会话安全版本、登录锁定
- 客户：客户、联系人、跟进、分类、行政区划
- 商机：阶段看板、输单原因、赢单转项目
- 项目：成员、里程碑、进度、交付状态
- 设备：部件、字符串参数、版本履历
- 服务：工单、指派、状态流转、处理记录
- 附件与审计：聚合根授权、软删除、前后快照
- 设置：受控字典与白名单运行参数
- 经营财务：供应商、采购、应收应付、收付款、核销、出货和汇总看板
- 搜索与工作台：按权限裁剪的全局搜索和聚合指标

所有跨模块写操作都在服务端校验所有权、状态和并发版本。财务余额来自交易明细与有效核销的查询聚合，不在客户或项目表中维护可漂移的汇总字段。

## 5. 数据与迁移

- MySQL 8、InnoDB、`utf8mb4`。
- 金额使用 `DECIMAL(18,2)` / .NET `decimal`。
- 时间戳以 UTC 存储；经营日期按配置的业务时区计算。
- 行版本用于乐观并发；分配、到货和正式出货等竞争写入额外使用事务锁。
- 迁移按 `0001_*.sql` 顺序执行，写入 `schema_migrations` 和 SHA-256。
- 已应用迁移不可修改、重命名、删除或重排；修正必须新增更高编号迁移。
- 迁移器使用 MySQL named lock 防止多实例同时迁移。

仓库只含结构迁移和系统种子，不含客户、联系人、交易或生产快照。

## 6. 身份与安全

- ASP.NET Core Cookie Authentication；Cookie 为 HttpOnly，生产环境要求 Secure。
- 所有非幂等 API 请求要求 antiforgery token。
- 后端权限策略是最终授权边界，前端仅用于隐藏无权操作。
- 五次失败登录触发锁定；密码或角色变更通过安全版本使旧会话失效。
- 查询排序字段使用白名单，值使用 Dapper 参数；LIKE 输入按字面量转义。
- 文件名只作为显示元数据，物理文件使用随机存储键并做根路径约束。
- API 返回 RFC Problem Details；生产环境不泄露内部异常细节。
- 审计记录业务操作和安全管理操作，但不记录密码、Cookie、连接字符串或物理存储路径。

## 7. 前端

Vue 3 SPA 使用 TypeScript、Pinia、Vue Router 和 Element Plus。模块路由按需加载；Axios 统一处理 Cookie、antiforgery token 与 Problem Details。ECharts 仅用于经营财务看板。

表格采用服务端分页、排序和筛选，默认每页 10 行。宽表在窄屏内横向滚动，不压缩关键业务字段。Element Plus 使用简体中文 locale。

## 8. 可运维性

- `/health/live`：进程存活检查。
- `/health/ready`：包含数据库就绪检查。
- Serilog 结构化控制台日志和关联 ID。
- release 目录不可变，`current` 符号链接原子切换。
- 发布包包含外部 archive SHA-256、内部 `SHA256SUMS` 和 `release.json`。
- 应用回滚与数据库恢复是两个独立操作；数据库恢复始终是破坏性操作。

## 9. 扩展原则

在实测规模证明必要之前，不引入微服务、Redis、消息队列、全文检索集群或物化财务汇总。新增生命周期状态、财务口径、外部存储或异步任务时，应先记录架构决策并补充跨层回归测试。
