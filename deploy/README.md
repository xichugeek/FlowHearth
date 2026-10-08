# 部署模板

此目录提供不含密钥的 Linux 部署模板：

- `nginx/flowhearth.example.com.bootstrap.conf`：申请 TLS 证书前的 HTTP/ACME 配置；
- `nginx/flowhearth.example.com.conf`：同源 SPA + `/api` HTTPS 反向代理；
- `systemd/flowhearth.service`：受限的 Kestrel 服务单元；
- `flowhearth.env.example`：不含真实密钥的运行时配置占位模板；
- `scripts/backup-database.sh`：一致性数据库备份与 SHA-256 旁车；
- `scripts/switch-release.sh`：校验内部 `SHA256SUMS` 后原子切换；
- `scripts/health-check.sh`：回环 live/readiness 检查；
- `scripts/reload-nginx-after-certificate-renewal.sh`：证书续期后检查并重载 Nginx。

模板假定部署根目录是 `/opt/flowhearth`，服务账户是 `flowhearth`，应用仅监听 `127.0.0.1:5100`。使用前必须替换示例域名，并按 [部署指南](../docs/deployment.md) 配置独立数据库账户和 root-only 环境文件。
