# Local development

## 工具

- .NET 10 SDK（`global.json` 固定所需 feature band）
- Node.js 22 与 npm
- MySQL 8.4 LTS 或经过验证的兼容版本

## 密钥与连接字符串

开发密钥通过环境变量、.NET user-secrets 或被忽略的本地配置提供。`appsettings.json` 只保留空占位符。

API 与迁移器需要分别设置 `ConnectionStrings:FlowHearth`。推荐为本地数据库创建：

- `flowhearth_app`：仅运行时 CRUD/EXECUTE 权限；
- `flowhearth_migrator`：仅 `flowhearth` 数据库的结构变更权限。

不要复用真实生产账号，不要把连接字符串贴入 Issue、日志或提交。

## Windows 本地 MySQL 脚本

`scripts/dev` 提供一个可选的 ZIP 版 MySQL 初始化方案，默认目录是 `%LOCALAPPDATA%\FlowHearth`，只监听 `127.0.0.1:3306`。

```powershell
.\scripts\dev\Initialize-LocalMySql.ps1
.\scripts\dev\Start-LocalMySql.ps1
.\scripts\dev\Stop-LocalMySql.ps1
```

初始化器只适用于全新的本地实例。它拒绝覆盖已有凭据或占用中的 3306，不应对共享或生产服务器运行。也可以完全不用这些脚本，自行提供 MySQL 和 user-secrets。

## 迁移与本地管理员

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'

dotnet run --project .\src\FlowHearth.DbMigrator -- status
dotnet run --project .\src\FlowHearth.DbMigrator -- migrate
dotnet run --project .\src\FlowHearth.DbMigrator -- validate

dotnet user-secrets set 'DevelopmentAdministrator:Username' '<local username>' --project .\src\FlowHearth.DbMigrator
dotnet user-secrets set 'DevelopmentAdministrator:DisplayName' '<display name>' --project .\src\FlowHearth.DbMigrator
dotnet user-secrets set 'DevelopmentAdministrator:Password' '<strong local password>' --project .\src\FlowHearth.DbMigrator
dotnet run --project .\src\FlowHearth.DbMigrator -- bootstrap-development-admin

Remove-Item Env:\DOTNET_ENVIRONMENT
```

`bootstrap-development-admin` 在 Production 中硬禁用。它创建或刷新指定的本地管理员，但不会打印密码。

## 运行

终端 1：

```powershell
dotnet run --project .\src\FlowHearth.Api --launch-profile http
```

终端 2：

```powershell
Set-Location .\web\flowhearth-web
npm ci
npm run dev
```

Vite 默认监听 `127.0.0.1:5173`，并把 `/api` 与 `/health` 代理到 `127.0.0.1:5100`。

平台端点：

- `GET /health/live`
- `GET /health/ready`
- `GET /openapi/v1.json`（仅 Development）

所有变更请求需要先从 `/api/v1/auth/csrf` 获取 antiforgery token。

## 验证

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

要启用真实数据库集成用例，先对专用测试库执行迁移和管理员引导，再将连接字符串放入 `FLOWHEARTH_TEST_MYSQL`。运行 `dotnet test` 时移除 `ConnectionStrings__FlowHearth`，避免改变无数据库配置健康检查的前提。测试会创建带唯一标记的临时数据并在 `finally` 中清理；不要把该变量指向生产数据库。

未设置 `FLOWHEARTH_TEST_MYSQL` 的数据库用例会提前返回，xUnit 仍将它们计入通过数量。只有启用该变量并验证测试库状态后，才能声称完成真实数据库验证。仓库的 GitHub CI 已包含迁移、测试管理员引导和全部数据库用例。
