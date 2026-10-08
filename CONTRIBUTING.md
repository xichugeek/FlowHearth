# Contributing to FlowHearth

欢迎提交 Issue 和 Pull Request。

## 开发流程

1. 从最新 `main` 创建分支。
2. 保持改动聚焦，新增行为同时补测试和文档。
3. 不修改已应用的 `database/migrations/*.sql`；新增更高编号迁移。
4. 不提交真实业务数据、生产配置或密钥。
5. 提交前运行 README 中的后端与前端验证命令。

提交信息建议使用简短、可执行的说明，例如：

```text
客户：修复地区筛选
界面：统一表格分页文案
docs: clarify deployment rollback
```

## Pull Request

PR 应说明：问题、方案、风险、测试结果、迁移影响与兼容性。如果修改权限、状态机、财务口径或部署边界，请同步更新架构文档。
