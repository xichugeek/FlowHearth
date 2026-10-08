# Episode 006 截图清单

采集日期：2026-10-08。共 61 张 PNG（约 5.5 MB）。真实 Chromium + Playwright CLI 截图，使用独立隔离 Demo Seed；未使用设计稿、生成图或业务数据导出。

桌面视口 1600×900，手机视口 390×844。列表/看板全页截图的图片高度可能超过视口；详情截图为当前视口，内容可在抽屉内滚动。文件尺寸以 PNG 原始像素为准。

## 推荐视频取材

- 工作台：desktop-dashboard.png；商机看板：desktop-opportunity-board.png；项目：desktop-projects.png；经营财务：desktop-finance-dashboard.png。
- 场景 A/B/C/D：scene-a-*、scene-b-*、scene-c-*、scene-d-*，编号对应 DEMO_SCENARIOS.md。
- 手机展示：mobile-dashboard.png、mobile-finance-dashboard.png、mobile-customer-form.png、mobile-receipt-detail.png。
- qa-* 仅为错误/空状态验收材料，不作正常业务画面取材。

## 敏感信息与真实性

客户、供应商、联系人、地址、设备序列号与银行参考号均为虚构；邮箱仅使用 example.test。画面账号为明确命名的本地演示管理员及禁用的虚构人员标识，没有密码、Cookie、令牌、私有域名或真实服务器地址。截图不包含浏览器地址栏和开发者工具。审计页仅显示真实本地管理员引导记录，采购/出货的操作记录为空；没有伪造历史用户审计。

场景详情已人工目视复核；全部页面在两个视口完成加载和宽度检查。其他浏览器、实体手机和正式视频剪辑效果未声称验收。图片校验和见 SCREENSHOT_SHA256.txt。

## 文件列表

| 文件 | 实际页面 | 视口 | PNG 尺寸 | 用途 |
| --- | --- | --- | --- | --- |
| [01-dashboard-desktop.png](01-dashboard-desktop.png) | /dashboard | 1600×900 | 1600×900 | 桌面展示 |
| [desktop-audit.png](desktop-audit.png) | /audit | 1600×900 | 1600×900 | 桌面展示 |
| [desktop-customers.png](desktop-customers.png) | /customers | 1600×900 | 1600×1066 | 桌面展示 |
| [desktop-dashboard.png](desktop-dashboard.png) | /dashboard | 1600×900 | 1600×1086 | 桌面展示 |
| [desktop-equipment.png](desktop-equipment.png) | /equipment | 1600×900 | 1600×1022 | 桌面展示 |
| [desktop-finance-dashboard.png](desktop-finance-dashboard.png) | /finance/dashboard | 1600×900 | 1600×2521 | 桌面展示 |
| [desktop-finance-payables.png](desktop-finance-payables.png) | /finance/payables | 1600×900 | 1600×942 | 桌面展示 |
| [desktop-finance-payments.png](desktop-finance-payments.png) | /finance/payments | 1600×900 | 1600×900 | 桌面展示 |
| [desktop-finance-purchases.png](desktop-finance-purchases.png) | /finance/purchases | 1600×900 | 1600×952 | 桌面展示 |
| [desktop-finance-receipts.png](desktop-finance-receipts.png) | /finance/receipts | 1600×900 | 1600×900 | 桌面展示 |
| [desktop-finance-receivables.png](desktop-finance-receivables.png) | /finance/receivables | 1600×900 | 1600×900 | 桌面展示 |
| [desktop-finance-shipments.png](desktop-finance-shipments.png) | /finance/shipments | 1600×900 | 1600×1132 | 桌面展示 |
| [desktop-finance-suppliers.png](desktop-finance-suppliers.png) | /finance/suppliers | 1600×900 | 1600×900 | 桌面展示 |
| [desktop-opportunities.png](desktop-opportunities.png) | /opportunities | 1600×900 | 1600×1158 | 桌面展示 |
| [desktop-opportunity-board.png](desktop-opportunity-board.png) | /opportunities（看板） | 1600×900 | 1600×900 | 桌面展示 |
| [desktop-projects.png](desktop-projects.png) | /projects | 1600×900 | 1600×1252 | 桌面展示 |
| [desktop-service.png](desktop-service.png) | /service | 1600×900 | 1600×1022 | 桌面展示 |
| [desktop-settings.png](desktop-settings.png) | /settings | 1600×900 | 1600×900 | 桌面展示 |
| [desktop-system.png](desktop-system.png) | /system | 1600×900 | 1600×900 | 桌面展示 |
| [mobile-audit.png](mobile-audit.png) | /audit | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-customer-form.png](mobile-customer-form.png) | /customers（创建表单） | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-customers.png](mobile-customers.png) | /customers | 390×844 | 390×1312 | 手机验收/展示 |
| [mobile-dashboard.png](mobile-dashboard.png) | /dashboard | 390×844 | 390×2426 | 手机验收/展示 |
| [mobile-detail-scene-a-customer.png](mobile-detail-scene-a-customer.png) | /customers?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-detail-scene-a-project.png](mobile-detail-scene-a-project.png) | /projects?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-detail-scene-b-payable.png](mobile-detail-scene-b-payable.png) | /finance/payables?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-detail-scene-b-payment.png](mobile-detail-scene-b-payment.png) | /finance/payments?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-detail-scene-b-purchase.png](mobile-detail-scene-b-purchase.png) | /finance/purchases?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-detail-scene-c-equipment.png](mobile-detail-scene-c-equipment.png) | /equipment?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-detail-scene-c-service.png](mobile-detail-scene-c-service.png) | /service?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-detail-scene-c-shipment.png](mobile-detail-scene-c-shipment.png) | /finance/shipments?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-detail-scene-d-receivable.png](mobile-detail-scene-d-receivable.png) | /finance/receivables?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-equipment-detail.png](mobile-equipment-detail.png) | /equipment?entityId=1 | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-equipment.png](mobile-equipment.png) | /equipment | 390×844 | 390×1172 | 手机验收/展示 |
| [mobile-finance-dashboard.png](mobile-finance-dashboard.png) | /finance/dashboard | 390×844 | 390×5055 | 手机验收/展示 |
| [mobile-finance-payables.png](mobile-finance-payables.png) | /finance/payables | 390×844 | 390×1320 | 手机验收/展示 |
| [mobile-finance-payments.png](mobile-finance-payments.png) | /finance/payments | 390×844 | 390×981 | 手机验收/展示 |
| [mobile-finance-purchases.png](mobile-finance-purchases.png) | /finance/purchases | 390×844 | 390×1204 | 手机验收/展示 |
| [mobile-finance-receipts.png](mobile-finance-receipts.png) | /finance/receipts | 390×844 | 390×882 | 手机验收/展示 |
| [mobile-finance-receivables.png](mobile-finance-receivables.png) | /finance/receivables | 390×844 | 390×1236 | 手机验收/展示 |
| [mobile-finance-shipments.png](mobile-finance-shipments.png) | /finance/shipments | 390×844 | 390×1132 | 手机验收/展示 |
| [mobile-finance-suppliers.png](mobile-finance-suppliers.png) | /finance/suppliers | 390×844 | 390×898 | 手机验收/展示 |
| [mobile-opportunities.png](mobile-opportunities.png) | /opportunities | 390×844 | 390×1714 | 手机验收/展示 |
| [mobile-projects.png](mobile-projects.png) | /projects | 390×844 | 390×1402 | 手机验收/展示 |
| [mobile-receipt-detail.png](mobile-receipt-detail.png) | /finance/receipts（DEMO-RC-001 详情） | 390×844 | 390×844 | 手机验收/展示 |
| [mobile-service.png](mobile-service.png) | /service | 390×844 | 390×1444 | 手机验收/展示 |
| [mobile-settings.png](mobile-settings.png) | /settings | 390×844 | 390×1096 | 手机验收/展示 |
| [mobile-system.png](mobile-system.png) | /system | 390×844 | 390×844 | 手机验收/展示 |
| [qa-customer-empty.png](qa-customer-empty.png) | /customers（无搜索结果） | 1600×900 | 1600×900 | 验收 |
| [qa-dashboard-error.png](qa-dashboard-error.png) | /dashboard（模拟 503） | 1600×900 | 1600×900 | 验收 |
| [scene-a-customer.png](scene-a-customer.png) | /customers?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
| [scene-a-opportunity.png](scene-a-opportunity.png) | /opportunities（DEMO-O-001 详情） | 1600×900 | 1600×900 | 业务场景 |
| [scene-a-project.png](scene-a-project.png) | /projects?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
| [scene-b-payable.png](scene-b-payable.png) | /finance/payables?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
| [scene-b-payment.png](scene-b-payment.png) | /finance/payments?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
| [scene-b-purchase.png](scene-b-purchase.png) | /finance/purchases?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
| [scene-c-equipment.png](scene-c-equipment.png) | /equipment?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
| [scene-c-service.png](scene-c-service.png) | /service?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
| [scene-c-shipment.png](scene-c-shipment.png) | /finance/shipments?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
| [scene-d-receipt.png](scene-d-receipt.png) | /finance/receipts（DEMO-RC-001 详情） | 1600×900 | 1600×900 | 业务场景 |
| [scene-d-receivable.png](scene-d-receivable.png) | /finance/receivables?entityId=1 | 1600×900 | 1600×900 | 业务场景 |
