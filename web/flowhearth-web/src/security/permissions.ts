export const permissions = {
  dashboardView: 'dashboard.view',
  customersView: 'customers.view',
  customersManage: 'customers.manage',
  opportunitiesView: 'opportunities.view',
  opportunitiesManage: 'opportunities.manage',
  projectsView: 'projects.view',
  projectsManage: 'projects.manage',
  equipmentView: 'equipment.view',
  equipmentManage: 'equipment.manage',
  serviceView: 'service.view',
  serviceManage: 'service.manage',
  attachmentsView: 'attachments.view',
  attachmentsManage: 'attachments.manage',
  auditView: 'audit.view',
  searchUse: 'search.use',
  settingsView: 'settings.view',
  settingsManage: 'settings.manage',
  securityUsersView: 'security.users.view',
  securityUsersManage: 'security.users.manage',
  securityRolesView: 'security.roles.view',
  securityRolesManage: 'security.roles.manage',
  financeDashboardView: 'finance.dashboard.view',
  suppliersView: 'suppliers.view',
  suppliersManage: 'suppliers.manage',
  receivablesView: 'receivables.view',
  receivablesManage: 'receivables.manage',
  receiptsView: 'receipts.view',
  receiptsManage: 'receipts.manage',
  purchasesView: 'purchases.view',
  purchasesManage: 'purchases.manage',
  payablesView: 'payables.view',
  payablesManage: 'payables.manage',
  paymentsView: 'payments.view',
  paymentsManage: 'payments.manage',
  shipmentsView: 'shipments.view',
  shipmentsManage: 'shipments.manage',
} as const

export function hasAnyPermission(
  grantedPermissions: readonly string[],
  requiredPermissions?: readonly string[],
) {
  if (!requiredPermissions || requiredPermissions.length === 0) {
    return true
  }

  const granted = new Set(grantedPermissions)
  return requiredPermissions.some((permission) => granted.has(permission))
}
