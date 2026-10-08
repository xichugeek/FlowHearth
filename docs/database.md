# Database and migrations

FlowHearth 使用只进不退、带 SHA-256 校验的 MySQL 迁移。`database/migrations` 当前包含 `0001` 至 `0017`，覆盖平台、安全、核心业务、经营财务、客户分类和行政区划。

## 迁移清单

- `0001`：迁移账本与业务编号序列
- `0002`：用户、角色、权限与登录安全
- `0003`：客户、联系人与跟进
- `0004`：商机与项目基础
- `0005`：项目交付、成员与里程碑
- `0006`：设备、部件、参数与版本
- `0007`：服务工单与处理记录
- `0008`：附件元数据与审计查询
- `0009`：受控字典与系统设置
- `0010`：审计查询索引
- `0011`：供应商、应收、收款与核销
- `0012`：采购、到货、应付、付款与核销
- `0013`：出货及出货明细
- `0014`：应付类型与付款收款方
- `0015`：出货制造商与签收索引
- `0016`：客户状态、等级和默认每页 10 行
- `0017`：客户省/市/区县行政区划代码

迁移只包含结构、权限和系统默认值，不包含客户、联系人或交易数据。

## 不可变规则

迁移器在 `schema_migrations` 中记录 ID、SHA-256 和执行时间，并使用 MySQL named lock 防止并发迁移。

一旦迁移在任何共享环境执行，就不得：

- 修改文件内容；
- 重命名或删除文件；
- 改变编号顺序；
- 手工修改迁移账本以绕过校验。

任何修正都必须新增下一个编号的迁移。

## 本地验证

```powershell
dotnet run --project .\src\FlowHearth.DbMigrator -- status
dotnet run --project .\src\FlowHearth.DbMigrator -- migrate
dotnet run --project .\src\FlowHearth.DbMigrator -- validate
dotnet run --project .\src\FlowHearth.DbMigrator -- migrate
```

最后一次 `migrate` 必须报告应用 0 个迁移。提交前还应运行 `scripts/release/Test-DatabaseUpgradeReadiness.ps1`，验证旧核心结构升级、全新安装、备份恢复和重复迁移。

## 数据规则

- 引擎：InnoDB；字符集：utf8mb4。
- 金额：`DECIMAL(18,2)`，数量按业务表使用固定小数位。
- 时间戳：UTC；经营日期由配置时区解释。
- 业务行使用乐观 `version`；竞争写入还使用事务锁。
- 应收应付余额来自有效核销聚合，不维护可编辑缓存总额。
- 归档和取消保留历史，不用硬删除改写财务事实。

生产迁移必须遵循 [部署指南](deployment.md)：先核对基线与备份，再迁移、校验、重复迁移和切换应用。
