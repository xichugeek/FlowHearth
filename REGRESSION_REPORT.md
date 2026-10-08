# FlowHearth V1.0.2 回归验收

日期：2026-10-08。分支：`codex/episode-006-demo-ui`。测试使用本机全新隔离 MySQL 8.4.11 实例及独立测试库；没有使用生产库、生产凭据或业务导出。运行环境：.NET SDK 10.0.400、Node.js 22.22.2、Chromium。代码和截图仍待人工验收，未合并、部署或发布 Release。

## 构建与自动测试

| 检查 | 命令/范围 | 实际结果 |
| --- | --- | --- |
| .NET Restore | `dotnet restore FlowHearth.sln` | 通过 |
| .NET Build | `dotnet build FlowHearth.sln -c Release --no-restore` | 通过，0 警告、0 错误；包含独立 DemoSeed 项目 |
| .NET Format | `dotnet format FlowHearth.sln --verify-no-changes --no-restore` | 通过 |
| .NET 单元测试 | Release、`--no-build` | 178 通过，0 失败，0 跳过 |
| .NET 集成测试 | 设置 `FLOWHEARTH_TEST_MYSQL` 后执行整个解决方案测试 | 95 通过，0 失败，0 跳过 |
| 真实 MySQL 专项 | `--filter FullyQualifiedName~RealMySql` | 21 通过，0 失败，0 跳过；属于上述 95 项的子集 |
| 前端 Lint | `npm run lint` | 通过，max-warnings=0 |
| TypeScript | `npm run typecheck` | 通过 |
| Vitest | `npm run test:run` | 34 文件、112 项通过 |
| Vite Production Build | `npm run build` | 通过；保留大于 500 kB chunk 的非阻断警告 |
| 数据库迁移 | 正式 DbMigrator `migrate`、`validate` | 全新演示库应用并校验全部 17 个迁移；已发布 SQL 文件无修改 |

真实 MySQL 专项执行前后，服务端 `SHOW GLOBAL STATUS LIKE 'Questions'` 增量为 **4,568**，测试中实际读写 MySQL 8.4.11。不是内存替身或跳过结果。部分原有测试在未配置连接字符串时会提前返回，因此本轮明确设置独立测试库连接，另行运行该专项并核实数据库查询证据。

测试结果文件保存在本机忽略目录 `.artifacts/final-test-results`，不提交凭据、请求记录或本机连接配置。

## 核心流程及安全覆盖

| 风险 | 已执行证据 |
| --- | --- |
| 客户 → 商机 → 项目 | `RealMySqlCustomerFlowTests`、`OpportunityFlowTests`、`ProjectFlowTests`：联系人归属、跟进、阶段推进、事务转化、项目状态、版本冲突及并发编号。 |
| 采购金额与到货 | `RealMySqlPurchaseFlowTests`：明细金额计算、部分/累计收货、超量拒绝、并发收货和财务汇总。 |
| 付款/收款核销 | `RealMySqlFinanceFlowTests`、`PayablePaymentFlowTests`：部分及多笔核销、余额、取消、重复/并发写入和审计。 |
| 设备 → 出货 → 售后 | `RealMySqlEquipmentFlowTests`、`ShipmentFlowTests`、`ServiceFlowTests`：设备关联、签收、状态机、解决信息、历史保护及并发。 |
| 报表与明细 | `RealMySqlOperatingSummaryTests`、`FinanceIntegrityTests`：公司、客户、项目汇总、核销事实、账龄、采购与设备约束。 |
| 登录/权限/CSRF | `SecurityApiTests`、`FinanceAuthorizationMatrixTests` 等：匿名 401、无权限 403、写请求 CSRF、登录退出及权限边界。浏览器也实际登录并读取完整隔离业务数据。 |
| 跨账号越权 | `SecurityAdministrationServiceTests`、`RealMySqlSecurityHardeningTests`：委派管理员不能授权更高角色/自身无权权限，不能管理或重置更高权限账号。财务/附件访问矩阵验证不同权限用户。 |

权限验收沿用平台现有 RBAC 模型。拥有某模块查看权限的协作用户可查看该模块资料；本轮没有增加按记录所有者隔离或多租户机制。

## Demo Seed 安全、幂等与完整性

| 实测 | 结果 |
| --- | --- |
| 首次导入及再次导入 | 新库成功；重复运行输出 `already present and verified; no rows changed`。 |
| 并发首次导入 | 最终种子版本在新测试库启动两个进程：一份初始化，一份验证已有数据；没有重复记录。 |
| 缺少管理员 | 迁移完成但尚未引导本地管理员的空库拒绝导入。 |
| 中途故障回滚 | 隔离测试库用临时触发器在联系人写入处强制失败；客户数回到 0、用户只剩原管理员 1 个。移除该测试触发器后正常初始化。 |
| 已有业务数据 | 单独测试库预置 1 条虚构业务记录；拒绝导入，原记录仍为 1、DEMO 记录为 0。 |
| 迁移校验和不匹配 | 仅篡改隔离负例测试库的迁移登记值，命令拒绝；未修改任何发布迁移文件。 |
| 闭环核销被取消 | 单独种子测试库取消首个案例核销后，完整闭环验证拒绝；恢复测试夹具后重复验证成功。已取消核销不会被计作付清证据。 |
| 环境及目标拒绝 | `scripts/Test-DemoSeed.ps1` 的 8 项检查全部通过：重复验证、缺少确认、Production、身份不匹配、非回环主机、3306、非 Demo 名称及拒绝后的基线验证。 |
| 审计真实性 | 最终演示库保留 1 条真实管理员引导审计；Seed 前后数量不变。服务诊断/处理是显式虚构业务记录，不是状态或用户操作审计。 |
| 业务完整性 | 校验全部预期数量、DEMO 编号、同客户/项目归属、单价 × 数量、采购总额、收货上限、核销双方与额度、时间先后及 3 条完整闭环。 |

最终快照数量：客户 30、联系人 50、跟进 80、商机 20、项目 12、设备 20、供应商 12、采购 20、到货 14、应收 12、收款 9、应付 20、付款 12、出货 8、工单 15、服务记录 26。项目成员 24、里程碑 36；设备组件/参数/版本各 20。

数据库明细与经营看板核对：

| 指标 | 明细 | 页面 |
| --- | ---: | ---: |
| 累计应收 | 1,450,500 | 1,450,500 |
| 实际收款 / 有效收款核销 | 1,056,500 / 1,056,500 | 1,056,500 |
| 应收余额 | 394,000 | 394,000 |
| 累计应付 | 836,200 | 836,200 |
| 实际付款 / 有效付款核销 | 376,140 / 376,140 | 376,140 |
| 应付余额 | 460,060 | 460,060 |
| 本月收款 / 付款 / 净现金流 | 131,500 / 61,900 / 69,600 | 一致 |

## 浏览器及截图验收

- 桌面 1600×900、手机 390×844，实际导航全部 17 个业务/管理路由：工作台、客户、商机、项目、设备、服务、财务总览及 7 个财务业务页、审计、字典设置、用户角色。文档宽度均等于视口宽度，宽表在自身容器内滚动。
- 打开客户、商机、项目、采购、应付、付款、设备、出货、工单、应收、收款详情；逐项核对第一条业务闭环。
- 手机打开客户创建表单和收款/设备详情。修复趋势图裁切及详情标签竖排；全局搜索聚焦后展开，输入 DEMO-E-001 并选择结果，实际跳转设备详情。
- 客户搜索不存在的编号显示无结果状态；新建表单打开及关闭正常，没有写入测试客户。
- 对工作台请求模拟 503，显示持久错误提示；解除模拟后点击刷新，指标与趋势恢复。该故障截图为验收材料，不用于业务录制。
- 本次浏览器主要验收快照展示与 UI 操作；完整业务写入使用上述真实 MySQL/API 回归。没有把未点击的浏览器成交、到货、签收及核销动作列为通过。

所有公开交付截图均为虚构业务和本地演示账号；不包含凭据、私有域名、真实服务器地址或真实客户资料。文件、视口和用途见 [SCREENSHOT_MANIFEST.md](docs/demo-screenshots/SCREENSHOT_MANIFEST.md)。

## 限制与人工验收

生产构建的财务页 chunk 约 556 kB，仍有打包体积警告；本轮未改变 ECharts 或拆分架构。浏览器测试覆盖 Chromium 及两种视口，没有声称验证所有浏览器或实体手机。正式录制动作需在另一份隔离快照中排练，避免修改已验收基线。

```text
FLOWHEARTH_UI = PASS
FLOWHEARTH_DEMO_SEED = PASS
FLOWHEARTH_REGRESSION = PASS
FLOWHEARTH_EPISODE_006_READY = READY_FOR_MANUAL_ACCEPTANCE
```

等待人工验收；未自动合并 main、部署生产或发布 GitHub Release。
