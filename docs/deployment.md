# FlowHearth 部署指南

本指南描述一个参考的单机 Linux 部署：Nginx 提供 HTTPS 和静态文件，ASP.NET Core 由 systemd 管理，MySQL 使用独立数据库。示例域名是 `flowhearth.example.com`，部署根目录是 `/opt/flowhearth`。

## 1. 前置条件

- Linux x64/arm64 主机
- .NET 10 ASP.NET Core Runtime
- MySQL 8
- Nginx
- systemd
- TLS 证书工具（例如 Certbot）

不要在内存受限的生产主机上编译。发布包应在可信的开发机或 CI 中构建。

## 2. 账户与目录

创建不可登录的服务账户，并建立 release/current/shared 布局：

```bash
sudo useradd --system --home /opt/flowhearth --shell /usr/sbin/nologin flowhearth
sudo install -d -o root -g root -m 0755 /opt/flowhearth/releases
sudo install -d -o root -g root -m 0755 /opt/flowhearth/deploy
sudo install -d -o flowhearth -g flowhearth -m 0750 \
  /opt/flowhearth/shared/uploads \
  /opt/flowhearth/shared/logs \
  /opt/flowhearth/shared/keys
sudo install -d -o root -g flowhearth -m 0750 \
  /opt/flowhearth/shared/backups \
  /opt/flowhearth/shared/acme
```

## 3. 数据库

创建独立的 `flowhearth` 数据库，以及职责分离的运行账户和迁移账户：

```sql
CREATE DATABASE flowhearth CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE USER 'flowhearth_app'@'127.0.0.1' IDENTIFIED BY '<runtime-password>';
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE
ON flowhearth.* TO 'flowhearth_app'@'127.0.0.1';

CREATE USER 'flowhearth_migrator'@'127.0.0.1' IDENTIFIED BY '<migrator-password>';
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX,
REFERENCES, CREATE VIEW, SHOW VIEW, TRIGGER
ON flowhearth.* TO 'flowhearth_migrator'@'127.0.0.1';
```

生产密码必须在部署时生成并存入受控密钥系统或 root-only 文件，不能使用文档中的占位文本。不要把 MySQL 3306 暴露到公网。

## 4. 构建发布包

在干净 Git 工作树中运行：

```powershell
dotnet restore .\FlowHearth.sln
dotnet build .\FlowHearth.sln -c Release --no-restore
dotnet test .\FlowHearth.sln -c Release --no-build --no-restore

.\scripts\deploy\Build-Release.ps1 -Version V1.0.0
```

脚本在 `.artifacts/` 中生成时间戳目录、`FlowHearth-<timestamp>.tar.gz` 和外部 manifest。发布包是依赖主机 .NET Runtime 的 framework-dependent 产物，不包含生产配置或业务数据；数据库迁移位于包内的 `migrator/migrations/`。

上传 archive 与外部 manifest 到服务器的临时目录。从可信构建端独立取得 archive SHA-256，并与 manifest 中的 `package.sha256` 一致。以下示例需先用实际时间戳、文件位置和哈希替换占位符：

```bash
release_id='<yyyyMMddHHmmss>'
archive="/tmp/FlowHearth-${release_id}.tar.gz"
expected_sha256='<SHA-256 from the trusted build>'
printf '%s  %s\n' "$expected_sha256" "$archive" | sha256sum --check -
tar -tzf "$archive"  # 确认只包含此 release，且无绝对路径或 .. 路径
test ! -e "/opt/flowhearth/releases/${release_id}"
sudo tar -xzf "$archive" -C /opt/flowhearth/releases
cd "/opt/flowhearth/releases/${release_id}"
sha256sum --check SHA256SUMS
```

不要在已有 release 目录上覆盖解压。检查 `release.json` 中的版本、Git 提交、`dirty=false` 和迁移范围，并保存本次部署记录。

## 5. 运行时配置

创建 `/opt/flowhearth/shared/flowhearth.env`，权限设为 root 可写、服务组可读（例如 `0640`）：

```dotenv
ConnectionStrings__FlowHearth=Server=127.0.0.1;Port=3306;Database=flowhearth;User ID=flowhearth_app;Password=<runtime-password>;SslMode=Preferred
Security__DataProtectionKeysPath=/opt/flowhearth/shared/keys
FileStorage__RootPath=/opt/flowhearth/shared/uploads
```

可从 `deploy/flowhearth.env.example` 复制占位模板。该文件使用 systemd EnvironmentFile 语法；不要直接用 shell `source` 加载其中未加引号的连接字符串。

迁移账户连接字符串应放在单独的 root-only 配置中，只在运行迁移器时注入环境。数据库备份脚本另需 `/opt/flowhearth/shared/migrator.my.cnf`（`0600`、root 所有），内容是 MySQL 客户端格式：

```ini
[client]
host=127.0.0.1
port=3306
protocol=tcp
user=<account allowed to dump the application database>
password=<backup password>
default-character-set=utf8mb4
```

备份账户须具备当前 MySQL 版本的完整 dump 权限，包含结构、视图、触发器、事件和存储程序。提前演练 `backup-database.sh` 和恢复流程；运行账户不能用于结构迁移。

## 6. 首次迁移与管理员

首次上线前：

1. 确认目标数据库是 `flowhearth`。
2. 运行 `status` 并保存输出。
3. 执行 `migrate`。
4. 执行 `validate`。
5. 再执行一次 `migrate`，必须应用 0 个迁移。

```bash
export DOTNET_ENVIRONMENT=Production
# 先通过受控配置加载 ConnectionStrings__FlowHearth（迁移账户）。
# 不要把真实连接字符串写进终端历史或把调试追踪 set -x 打开。
dotnet /opt/flowhearth/releases/<release>/migrator/FlowHearth.DbMigrator.dll status
dotnet /opt/flowhearth/releases/<release>/migrator/FlowHearth.DbMigrator.dll migrate
dotnet /opt/flowhearth/releases/<release>/migrator/FlowHearth.DbMigrator.dll validate
dotnet /opt/flowhearth/releases/<release>/migrator/FlowHearth.DbMigrator.dll migrate
```

首个管理员由迁移器的 `bootstrap-admin` 创建。通过环境变量临时提供 `BootstrapAdministrator__Username`、`DisplayName`、可选 `Email` 和 `Password`；成功后立即从环境与临时文件中移除这些变量。已有任意用户时，该命令不会创建第二个引导账户。

```bash
# 先通过受控配置注入上述 BootstrapAdministrator__* 变量。
dotnet /opt/flowhearth/releases/<release>/migrator/FlowHearth.DbMigrator.dll bootstrap-admin
unset BootstrapAdministrator__Username BootstrapAdministrator__DisplayName
unset BootstrapAdministrator__Email BootstrapAdministrator__Password
unset ConnectionStrings__FlowHearth
```

## 7. systemd 与 Nginx

1. 复制 `deploy/systemd/flowhearth.service` 到 `/etc/systemd/system/`。
2. 复制 `deploy/scripts/*.sh` 到 `/opt/flowhearth/deploy/`，root 所有，权限 `0750`。
3. 将 Nginx 模板中的 `flowhearth.example.com` 替换为真实域名。
4. 先使用 bootstrap vhost 完成 ACME 验证，再启用 HTTPS vhost。
5. 检查配置后再 reload：`nginx -t && systemctl reload nginx`。

Kestrel 必须保持 `127.0.0.1:5100`；公网只开放 80/443（以及受限来源的管理端口）。

首次安装在完成迁移、配置、内部 checksum 校验后执行：

```bash
sudo systemctl daemon-reload
sudo /opt/flowhearth/deploy/switch-release.sh <yyyyMMddHHmmss>
sudo systemctl enable flowhearth.service
sudo systemctl status flowhearth.service --no-pager
curl --fail http://127.0.0.1:5100/health/live
curl --fail http://127.0.0.1:5100/health/ready
```

随后验证 HTTPS 登录、客户列表和退出。切换脚本遇到健康检查失败会报错；它不会自动恢复旧应用或数据库，须按第 9 节评估处理。

## 8. 每次升级

升级顺序不可颠倒：

1. 核对当前 release、Git 提交、数据库和迁移状态。
2. 运行 `backup-database.sh`，验证文件非空、`gzip -t` 和 SHA-256。
3. 上传到新的不可变 release 目录，验证外部和内部校验和。
4. 比较候选迁移；不得修改已应用迁移。
5. 用迁移账户执行 `status -> migrate -> validate -> migrate`。
6. 运行 `switch-release.sh <yyyyMMddHHmmss>` 原子切换。
7. 验证 live/readiness、登录、核心只读 API、静态资源和日志。
8. 延迟观察服务重启次数、应用错误、Nginx 错误和内存。

## 9. 回滚与恢复

- 应用回滚：只有在旧二进制与当前数据库结构兼容时，才把 `current` 切回已验证的旧 release。
- 数据库恢复：独立且具有破坏性。先停止应用并备份失败现场数据库，再恢复经过校验的整库备份。
- 不提供手工反向迁移；不要修改 `schema_migrations` 来绕过校验。

历史 release 和数据库备份应按组织策略保留，不要在故障处理中顺手删除。
