import { describe, expect, it } from 'vitest'
import AuditLogsViewSource from './audit/AuditLogsView.vue?raw'
import CustomersViewSource from './customers/CustomersView.vue?raw'
import EquipmentViewSource from './equipment/EquipmentView.vue?raw'
import FinanceDashboardViewSource from './finance/FinanceDashboardView.vue?raw'
import PayablesViewSource from './finance/PayablesView.vue?raw'
import PaymentsViewSource from './finance/PaymentsView.vue?raw'
import PurchasesViewSource from './finance/PurchasesView.vue?raw'
import ReceiptsViewSource from './finance/ReceiptsView.vue?raw'
import ReceivablesViewSource from './finance/ReceivablesView.vue?raw'
import ShipmentsViewSource from './finance/ShipmentsView.vue?raw'
import SuppliersViewSource from './finance/SuppliersView.vue?raw'
import OpportunitiesViewSource from './opportunities/OpportunitiesView.vue?raw'
import ProjectsViewSource from './projects/ProjectsView.vue?raw'
import ServiceTicketsViewSource from './service/ServiceTicketsView.vue?raw'
import SecurityAdministrationViewSource from './system/SecurityAdministrationView.vue?raw'
import SettingsAdministrationViewSource from './system/SettingsAdministrationView.vue?raw'

const presentationTargets = [
  ['客户', CustomersViewSource, 1],
  ['商机', OpportunitiesViewSource, 1],
  ['项目', ProjectsViewSource, 1],
  ['设备', EquipmentViewSource, 1],
  ['服务', ServiceTicketsViewSource, 1],
  ['审计', AuditLogsViewSource, 1],
  ['供应商', SuppliersViewSource, 1],
  ['应收', ReceivablesViewSource, 1],
  ['收款', ReceiptsViewSource, 1],
  ['采购', PurchasesViewSource, 1],
  ['应付', PayablesViewSource, 1],
  ['付款', PaymentsViewSource, 1],
  ['出货', ShipmentsViewSource, 1],
  ['经营财务看板', FinanceDashboardViewSource, 5],
  ['字典设置', SettingsAdministrationViewSource, 1],
  ['用户与角色', SecurityAdministrationViewSource, 2],
] as const

describe('content-area data-table presentation', () => {
  it.each(presentationTargets)('%s uses the shared bordered and striped table treatment', (_name, source, count) => {
    const tableTags = source.match(/<el-table\b[^>]*class="[^"]*flowhearth-data-table[^"]*"[^>]*>/g) ?? []

    expect(tableTags).toHaveLength(count)
    for (const tableTag of tableTags) {
      expect(tableTag).toMatch(/\bborder\b/)
      expect(tableTag).toMatch(/\bstripe\b/)
    }
  })
})
