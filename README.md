# FlowHearth

FlowHearth 是一个面向自动化项目型团队的开源业务管理系统。它把客户、商机、项目、设备、服务和经营财务放在同一个可审计的工作流中，同时保持单体部署和较低的运维成本。

```text
客户 -> 商机 -> 项目 -> 设备 -> 服务
客户 -> 应收 -> 收款 -> 核销
项目 -> 采购 -> 应付 -> 付款 -> 核销
项目 -> 出货；采购 -> 供应商
```

本仓库是独立、无历史包袱的开源快照，不包含任何真实客户资料、生产数据库、生产域名、账号、Cookie、连接字符串、私钥或内部发布记录。

## 功能

- 客户、联系人、跟进、客户分级与省/市/区县筛选
- 商机看板与成交转项目
- 项目成员、里程碑、进度与状态流转
- 设备、部件、参数和版本历史
- 服务工单与可审计处理时间线
- 供应商、采购、部分到货、应收应付、收付款与核销
- 出货、签收、项目/客户/公司经营财务汇总
- 经营财务看板、全局搜索、附件、审计日志
- Cookie 登录、CSRF 防护、RBAC、登录锁定和安全响应头

## 技术栈

- 后端：.NET 10、ASP.NET Core、Dapper、MySqlConnector、Serilog
- 前端：Vue 3、TypeScript、Vite、Pinia、Element Plus、ECharts
- 数据库：MySQL 8 / InnoDB / utf8mb4
- 部署：Nginx、systemd、Kestrel（仅监听回环地址）

FlowHearth 采用模块化单体架构，不依赖 Redis、消息队列、Elasticsearch、Kubernetes 或生产 Node.js 进程。完整说明见 [技术架构](docs/architecture.md)。

## 目录

```text
src/       .NET API、应用层、领域层、基础设施层和迁移器
tests/     xUnit 单元测试与集成测试
web/       Vue 3 + TypeScript 单页应用
database/  只进不退、带校验和的 MySQL 迁移
deploy/    Nginx、systemd 和原子发布脚本模板
scripts/   本地开发、数据库演练和发布构建脚本
docs/      API、数据库、开发、架构和部署文档
```

## 本地启动

前置条件：.NET SDK 10、Node.js 22、npm 和 MySQL 8。

1. 创建空数据库 `flowhearth`，并为运行账户与迁移账户分别授权。
2. 用 .NET user-secrets 或环境变量配置 `ConnectionStrings__FlowHearth`，不要把连接字符串写入仓库。
3. 执行迁移并创建首个本地管理员：

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project .\src\FlowHearth.DbMigrator -- migrate
dotnet user-secrets set 'DevelopmentAdministrator:Username' 'admin' --project .\src\FlowHearth.DbMigrator
dotnet user-secrets set 'DevelopmentAdministrator:DisplayName' 'Local Administrator' --project .\src\FlowHearth.DbMigrator
dotnet user-secrets set 'DevelopmentAdministrator:Password' '<strong local password>' --project .\src\FlowHearth.DbMigrator
dotnet run --project .\src\FlowHearth.DbMigrator -- bootstrap-development-admin
Remove-Item Env:\DOTNET_ENVIRONMENT
```

4. 分别启动 API 和前端：

```powershell
dotnet run --project .\src\FlowHearth.Api --launch-profile http

Set-Location .\web\flowhearth-web
npm ci
npm run dev
```

浏览器访问 `http://127.0.0.1:5173`。更完整的本地数据库说明见 [开发指南](docs/development.md)。

## Episode 006 演示环境

V1.0.2 演示候选变更提供独立、显式的 Demo Seed。仅接受本机非标准端口、`flowhearth_demo_*` 隔离库和开发/演示环境，不自动向默认数据库写入，也不提供清库命令。

初始化、虚构数据规模及本地演示账号说明见 [DEMO_SEED_GUIDE.md](DEMO_SEED_GUIDE.md)。视频路径见 [DEMO_SCENARIOS.md](DEMO_SCENARIOS.md) 和 [截图清单](docs/demo-screenshots/SCREENSHOT_MANIFEST.md)；界面审查、优化和验收分别见 [UI_AUDIT.md](UI_AUDIT.md)、[UI_OPTIMIZATION_REPORT.md](UI_OPTIMIZATION_REPORT.md)、[REGRESSION_REPORT.md](REGRESSION_REPORT.md)。

## 构建与测试

```powershell
dotnet restore .\FlowHearth.sln
dotnet format .\FlowHearth.sln --verify-no-changes --no-restore
dotnet build .\FlowHearth.sln -c Release --no-restore
dotnet test .\FlowHearth.sln -c Release --no-build --no-restore

Set-Location .\web\flowhearth-web
npm ci
npm run lint
npm run typecheck
npm run test:run
npm run build
```

真实 MySQL 集成测试通过 `FLOWHEARTH_TEST_MYSQL` 接收专用测试数据库连接字符串。先迁移测试库并创建测试管理员；运行测试时不要同时设置 `ConnectionStrings__FlowHearth`，以保留未配置数据库的健康检查测试前提。未提供 `FLOWHEARTH_TEST_MYSQL` 时，数据库用例会提前返回，测试报告中的通过数量不能作为真实数据库验证依据。GitHub CI 会执行完整数据库场景。

## 部署

生产构建应在 CI 或开发机完成，服务器只接收不可变发布包。模板按 `/opt/flowhearth`、`flowhearth.service`、`flowhearth.example.com` 和回环端口 `5100` 编写，使用前请替换示例域名。完整步骤、备份和回滚边界见 [部署指南](docs/deployment.md)。

## 版本

公开仓库首个版本为 `V1.0.0`，当前修订版本为 `V1.0.1`。版本标签统一使用大写 `V`，发布内容见 [CHANGELOG.md](CHANGELOG.md)。

## 安全与隐私

请勿提交真实业务数据或任何密钥。安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。示例部署默认要求 MySQL 不公网暴露、Kestrel 仅监听 `127.0.0.1`，附件保存在 Web 根目录之外。

## 许可证

[MIT](LICENSE)
