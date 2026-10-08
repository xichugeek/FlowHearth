# FlowHearth Episode 006 Demo Seed

Demo Seed 是独立的 `FlowHearth.DemoSeed` 命令，面向本机隔离 MySQL。没有默认清库命令，也不修改已发布迁移。源数据全部是虚构名称、`example.test` 邮箱、模拟流水及模拟序列号。Seed 不插入 `audit_logs`。到货记录使用一个禁用且无法登录的 `demo-seed-receiver` 标识；实际录制登录账号需在隔离库中另行创建。

## 初始化顺序

前置条件：.NET SDK 10、MySQL 8；前端使用 Node.js 22。PowerShell 验证脚本在 PowerShell 7 执行。本轮实际数据库版本为 MySQL 8.4.11。

1. 创建**全新空库**，名字必须以 `flowhearth_demo_` 开头。MySQL 仅绑定本机回环地址，使用非 `3306` 端口（例如 `3307`）。不要把生产库镜像或业务导出导入其中。
2. 初始化阶段设置 `DOTNET_ENVIRONMENT=Development`。用 `ConnectionStrings__FlowHearth` 环境变量或本机 user-secrets 向迁移器提供连接字符串，执行 `dotnet run --project .\src\FlowHearth.DbMigrator -- migrate` 和 `validate`。必须应用全部 17 个迁移及原始校验和。
3. 在该隔离库中使用 `bootstrap-development-admin` 创建**仅供本地/隔离 Demo 环境**的录制账号，密码由操作者本机生成并保存在 user-secrets。不要复用任何生产账号、密码或 Cookie。管理员引导产生的真实安全审计记录会保留。
4. 使用仅存于本机的环境变量 `FLOWHEARTH_DEMO_MYSQL` 提供同一库的连接字符串，执行：

   ```powershell
   dotnet run --project .\src\FlowHearth.DemoSeed -- initialize flowhearth_demo_episode006 --confirm-isolated-demo
   ```

5. API 与前端连接这个库后再录制。立即重复步骤 4 验证幂等：输出应为 `already present and verified; no rows changed`。在录制过程中人为修改数据后，完整性检查可能拒绝再次初始化；请新建另一个空隔离库重新开始，不能清空既有业务库。

命令会检查环境名、显式确认标志、连接主机是否为 `127.0.0.1` / `localhost` / `::1`、非标准端口、数据库名及 MySQL `DATABASE()` 身份、17 个迁移、已引导的本地管理员，以及首次导入时所有业务表为空。安全引导的审计记录允许存在，Seed 自身不会增加审计记录。数据库命名锁、单事务、固定 `DEMO-` 编号和导入后关系校验防止并发及重复导入。任何条件不满足时拒绝写入。

Seed 也接受 `DOTNET_ENVIRONMENT=Demo`；管理员引导工具仅接受 Development / Testing，因此需先在 Development 完成引导。连接字符串只读取 `FLOWHEARTH_DEMO_MYSQL`，不会使用 API 的默认连接配置。Seed 会逐一比较随程序复制的已发布迁移与数据库校验和。

管理员引导所需配置为 `DevelopmentAdministrator__Username`、`DevelopmentAdministrator__DisplayName`、`DevelopmentAdministrator__Password`。显示名称应明确写“本地演示”；密码在本机随机生成，通过环境变量或 user-secrets 提供，不写进仓库、文档或截图。运行 API 时使用同一个隔离库的 `ConnectionStrings__FlowHearth`。四个历史人员标识均为禁用账号，不提供可登录密码。

## 数据规模与业务口径

| 对象 | 数量 | 说明 |
| --- | ---: | --- |
| 客户 / 联系人 / 跟进 | 30 / 50 / 80 | 客户按约 8 个月分布；联系人和跟进归属同一客户，时间不早于建档。 |
| 商机 / 项目 | 20 / 12 | 12 个赢单商机转为同客户项目；含进行中、完成、暂停与规划项目。 |
| 设备 / 供应商 | 20 / 12 | 设备关联项目和对应客户。 |
| 采购 / 到货 | 20 / 14 | 一单一明细；10 单全到货、4 单部分到货，数量不超订单。 |
| 应收 / 收款 / 应付 / 付款 | 12 / 9 / 20 / 12 | 每笔核销关联同一客户或供应商；金额不超任一端余额。 |
| 出货 / 售后工单 / 服务记录 | 8 / 15 / 26 | 工单归属正确；诊断与处理记录明确标为虚构，无伪造的状态审计。 |

前 3 组 `DEMO-C-001`～`003`、`DEMO-O-001`～`003`、`DEMO-P-001`～`003` 构成完整的商机、项目、采购、到货、付款、交付、售后和收款闭环。后续记录包含部分核销、延期、待到货及未关闭工单，用于看板与风险展示。

前三个项目验收后分别开立并全额收回 180,000 / 227,000 / 274,000 元；其采购应付分别为 16,400 / 25,740 / 35,840 元并已付清。全部财务快照合计：应收 1,450,500，收款及核销 1,056,500，应付 836,200，付款及核销 376,140 元。财务主记录共 53 条，略高于参考规模，以保留每条采购的独立应付及三条完整闭环。

日期相对首次初始化当天生成；之后重复执行保留原快照，不滚动重写历史数据。若在其他日期新建隔离库，月度指标会随业务日期变化。演示毛利沿用现有系统口径，仅考虑采购成本，不能解说为含人工税费的净利润。

## 可重复验证命令

先构建 Release，再对已完整初始化的隔离库执行：

```powershell
dotnet build FlowHearth.sln -c Release
$env:FLOWHEARTH_DEMO_MYSQL = $env:ConnectionStrings__FlowHearth
./scripts/Test-DemoSeed.ps1 -Database flowhearth_demo_episode006
```

该脚本验证重复运行、生产环境拒绝、缺少确认、目标不匹配、非回环主机、默认端口及非 Demo 库名拒绝；不创建数据库、不清库，也不输出连接字符串。真实 MySQL 回滚、并发首次初始化、已有业务资料及校验和篡改拒绝的实测结果见 [REGRESSION_REPORT.md](REGRESSION_REPORT.md)。

## 验证与限制

成功输出是种子校验完成的必要证据；仍需执行真实 MySQL 集成测试、迁移验证和页面验收。Seed 直接写入隔离数据库以提供展示快照，因此不会制造看似由真实用户逐步点击产生的审计日志。数据库内已有业务资料时命令拒绝首次导入；它不提供覆盖或删除选项。
