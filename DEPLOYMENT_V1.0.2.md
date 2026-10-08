# FlowHearth v1.0.2 部署说明

本文件随 `v1.0.2` 标签提供，覆盖人工部署和 Episode 006 隔离演示初始化。代码推送与打标签只触发 GitHub CI 验证；当前工作流没有生产部署或 GitHub Release 发布步骤。部署前须完成项目人工验收。

## 1. 版本与升级范围

| 项目 | v1.0.2 |
| --- | --- |
| 前后端版本 | 1.0.2 |
| Git 标签 | `v1.0.2`，历史 `V1.0.0` / `V1.0.1` 保留 |
| 运行环境 | .NET 10 ASP.NET Core Runtime、MySQL 8、Nginx、systemd |
| 构建环境 | .NET SDK 10、Node.js 22、npm、PowerShell 7、tar |
| 数据库 | 17 个既有迁移，最后编号 `0017`；本轮新增 0 个 |
| 主要变更 | 界面与响应式优化、独立虚构 Demo Seed、视频场景与截图 |

从 `V1.0.1` 升级不需要新服务或额外数据库字段。仍须核对目标数据库身份、备份、校验迁移与发布包，再由操作者切换版本。既有数据库中的用户、业务资料、附件和 Data Protection 密钥沿用原配置。

## 2. 获取版本并验证

在新的本地目录获取标签，避免覆盖已有工作树：

```powershell
git clone --branch v1.0.2 https://github.com/xichugeek/FlowHearth.git FlowHearth-v1.0.2
Set-Location FlowHearth-v1.0.2
git show --no-patch v1.0.2
git status --short
```

完整验收结果与范围见 [REGRESSION_REPORT.md](REGRESSION_REPORT.md)。重新构建前依次执行后端 Restore / Format / Build / Test，以及前端 `npm ci`、Lint、Typecheck、Vitest 和 Production Build，命令见 [README](README.md#构建与测试)。真实 MySQL 测试的准备步骤见 [开发指南](docs/development.md)。

`FLOWHEARTH_TEST_MYSQL` 必须指向已迁移并已创建测试管理员的专用测试库。测试连接通过本机受控环境提供，不能使用生产库，也不能提交连接字符串。后端测试时移除 `ConnectionStrings__FlowHearth`，保留未配置数据库健康检查用例的前提。执行前至少检查：

```powershell
if (-not $env:FLOWHEARTH_TEST_MYSQL) { throw '专用 MySQL 测试连接未配置。' }
Remove-Item Env:\ConnectionStrings__FlowHearth -ErrorAction SilentlyContinue
dotnet test .\FlowHearth.sln -c Release --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw '后端测试失败。' }
```

保存测试输出并确认数据库场景实际执行；没有连接 MySQL 的提前返回用例不能写成数据库测试通过。当前回归基线为后端 178 个单元 / 95 个集成测试、前端 34 个文件 / 112 个用例。构建包 manifest 中的数量只是参数记录，不会触发测试，也不能代替测试输出。

## 3. 构建不可变发布包

在验证完成的干净工作树中，用 PowerShell 7 执行。从已完整应用 `0017` 的 `V1.0.1` 环境升级：

```powershell
.\scripts\deploy\Build-Release.ps1 `
  -Version v1.0.2 `
  -BaseProductionVersion V1.0.1 `
  -MigrationFrom 0017 `
  -MigrationTo 0017
```

首次部署使用 `-BaseProductionVersion none -MigrationFrom 0000 -MigrationTo 0017`。`migrationCount=17` 表示包内迁移总数，升级新增数为 0；打包参数不负责读取服务器的实际迁移状态。若使用仓库外的 .NET SDK，可先将 `DOTNET_ROOT` 指向 SDK 所在目录。

输出位于 `.artifacts/`：

- `FlowHearth-<releaseId>.tar.gz`：API、迁移器及原始迁移、前端静态文件。
- `FlowHearth-<releaseId>-manifest.json`：版本、Git 提交、迁移范围与归档 SHA-256。
- `releases/<releaseId>/release.json` 与 `SHA256SUMS`：包内元数据与逐文件校验和。

包内没有业务数据、演示账号、生产连接字符串或 Demo Seed 程序。需要演示数据时按第 5 节单独初始化。不要使用 `-AllowDirty` 构建验收候选包。

## 4. 人工部署与升级

完整 Linux 命令、数据库账户权限、Nginx/systemd 模板及密钥配置见 [部署指南](docs/deployment.md)。按以下顺序执行：

1. 记录当前版本、提交与目标数据库；检查运行账户和迁移账户配置。
2. 备份数据库，校验非空、gzip 和 SHA-256，并确保已有恢复演练；保留 uploads 和 Data Protection 密钥目录。
3. 上传归档和 manifest，从可信构建端核对外部 SHA-256。检查 tar 路径后解压到新的 `/opt/flowhearth/releases/<releaseId>`，执行 `sha256sum --check SHA256SUMS`。
4. 核对 `release.json` 的 `version=v1.0.2`、Git 提交及 `dirty=false`。
5. 用迁移账户执行 `status -> migrate -> validate -> migrate`。从完整 `V1.0.1` 升级，两次 migrate 都应应用 0 个迁移；首次部署第一次为 17 个、第二次为 0 个。
6. 人工执行 `switch-release.sh <releaseId>`，检查 live/readiness、HTTPS 登录、权限、CSRF、客户和财务只读页面，以及桌面/移动端布局。
7. 保存验收记录并观察 API/Nginx 错误、服务重启与资源使用。发布后业务写入检查须使用已授权的测试记录。

应用回滚仅在旧版本与当前数据库兼容时切换到旧 release；数据库恢复需要独立人工处置。不要改写已发布迁移、删除业务数据或修改 `schema_migrations` 绕过校验。切换脚本不会自动恢复数据库。

## 5. Episode 006 本地隔离演示

此流程仅供本地或隔离 Demo 环境，账号与密码必须新建。先在绑定回环地址、非 `3306` 端口（例如 `3307`）的独立 MySQL 实例创建全新空库 `flowhearth_demo_episode006`。数据库账户只授权该隔离库；不要导入生产数据库备份。

在源码根目录使用 PowerShell 7，输入仅供本地演示的连接和密码（不写入命令历史）：

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ConnectionStrings__FlowHearth = Read-Host '隔离 Demo 库连接字符串（回环地址、非 3306 端口）' -MaskInput
$env:DevelopmentAdministrator__Username = 'demo-local-admin'
$env:DevelopmentAdministrator__DisplayName = '本地演示管理员'
$env:DevelopmentAdministrator__Password = Read-Host '新生成的本地演示强密码' -MaskInput

dotnet run --project .\src\FlowHearth.DbMigrator -- migrate
if ($LASTEXITCODE -ne 0) { throw '隔离库迁移失败。' }
dotnet run --project .\src\FlowHearth.DbMigrator -- validate
if ($LASTEXITCODE -ne 0) { throw '隔离库迁移校验失败。' }
dotnet run --project .\src\FlowHearth.DbMigrator -- bootstrap-development-admin
if ($LASTEXITCODE -ne 0) { throw '本地管理员引导失败。' }
Remove-Item Env:\DevelopmentAdministrator__Password

$env:FLOWHEARTH_DEMO_MYSQL = $env:ConnectionStrings__FlowHearth
dotnet run --project .\src\FlowHearth.DemoSeed -- initialize flowhearth_demo_episode006 --confirm-isolated-demo
if ($LASTEXITCODE -ne 0) { throw 'Demo 初始化失败。' }
dotnet run --project .\src\FlowHearth.DemoSeed -- initialize flowhearth_demo_episode006 --confirm-isolated-demo
if ($LASTEXITCODE -ne 0) { throw 'Demo 重复验证失败。' }
```

重复执行应输出 `already present and verified; no rows changed`。Seed 还会验证数据库命名、真实 `DATABASE()` 身份、环境、17 个迁移原始校验和和业务表空库条件；它没有覆盖或清空已有资料的命令。初始化的完整说明及安全验证见 [DEMO_SEED_GUIDE.md](DEMO_SEED_GUIDE.md)。

保留当前 PowerShell 的隔离库连接配置，启动 `dotnet run --project .\src\FlowHearth.Api --launch-profile http`。另开终端在 `web/flowhearth-web` 执行 `npm ci`、`npm run dev`，访问 `http://127.0.0.1:5173`，使用刚创建的本地演示账号登录。录制完成、API 停止后从终端环境移除连接与管理员变量，关闭隔离实例。

视频操作路径、预期结果、人工录制评估见 [DEMO_SCENARIOS.md](DEMO_SCENARIOS.md)，截图见 [截图清单](docs/demo-screenshots/SCREENSHOT_MANIFEST.md)。该初始化生成展示快照，不伪造操作审计。人工操作后需要恢复原始快照时，新建另一个空隔离库。
