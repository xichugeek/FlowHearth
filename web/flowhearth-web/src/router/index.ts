import { createRouter, createWebHistory } from 'vue-router'
import type { RouteLocationNormalized } from 'vue-router'

import { permissions } from '../security/permissions'
import { useAuthStore } from '../stores/auth'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/auth/LoginView.vue'),
      meta: { title: '登录' },
    },
    {
      path: '/forbidden',
      name: 'forbidden',
      component: () => import('../views/auth/ForbiddenView.vue'),
      meta: { title: '无权访问', requiresAuth: true },
    },
    {
      path: '/',
      redirect: '/dashboard',
    },
    {
      path: '/',
      component: () => import('../layouts/AppShell.vue'),
      children: [
        {
          path: 'dashboard',
          name: 'dashboard',
          component: () => import('../views/dashboard/DashboardView.vue'),
          meta: {
            title: '工作台',
            requiresAuth: true,
            permissionsAny: [permissions.dashboardView],
          },
        },
        {
          path: 'customers',
          name: 'customers',
          component: () => import('../views/customers/CustomersView.vue'),
          meta: {
            title: '客户管理',
            requiresAuth: true,
            permissionsAny: [permissions.customersView],
          },
        },
        {
          path: 'opportunities',
          name: 'opportunities',
          component: () => import('../views/opportunities/OpportunitiesView.vue'),
          meta: {
            title: '商机管理',
            requiresAuth: true,
            permissionsAny: [permissions.opportunitiesView],
          },
        },
        {
          path: 'projects',
          name: 'projects',
          component: () => import('../views/projects/ProjectsView.vue'),
          meta: {
            title: '项目管理',
            requiresAuth: true,
            permissionsAny: [permissions.projectsView],
          },
        },
        {
          path: 'equipment',
          name: 'equipment',
          component: () => import('../views/equipment/EquipmentView.vue'),
          meta: {
            title: '设备档案',
            requiresAuth: true,
            permissionsAny: [permissions.equipmentView],
          },
        },
        {
          path: 'service',
          name: 'service',
          component: () => import('../views/service/ServiceTicketsView.vue'),
          meta: {
            title: '服务管理',
            requiresAuth: true,
            permissionsAny: [permissions.serviceView],
          },
        },
        {
          path: 'finance/dashboard',
          name: 'finance-dashboard',
          component: () => import('../views/finance/FinanceDashboardView.vue'),
          meta: { title: '财务总览', requiresAuth: true, permissionsAny: [permissions.financeDashboardView] },
        },
        {
          path: 'finance/receivables',
          name: 'receivables',
          component: () => import('../views/finance/ReceivablesView.vue'),
          meta: { title: '应收账款', requiresAuth: true, permissionsAny: [permissions.receivablesView] },
        },
        {
          path: 'finance/receipts',
          name: 'receipts',
          component: () => import('../views/finance/ReceiptsView.vue'),
          meta: { title: '收款记录', requiresAuth: true, permissionsAny: [permissions.receiptsView] },
        },
        {
          path: 'finance/purchases',
          name: 'purchases',
          component: () => import('../views/finance/PurchasesView.vue'),
          meta: { title: '采购管理', requiresAuth: true, permissionsAny: [permissions.purchasesView] },
        },
        {
          path: 'finance/payables',
          name: 'payables',
          component: () => import('../views/finance/PayablesView.vue'),
          meta: { title: '应付账款', requiresAuth: true, permissionsAny: [permissions.payablesView] },
        },
        {
          path: 'finance/payments',
          name: 'payments',
          component: () => import('../views/finance/PaymentsView.vue'),
          meta: { title: '付款记录', requiresAuth: true, permissionsAny: [permissions.paymentsView] },
        },
        {
          path: 'finance/shipments',
          name: 'shipments',
          component: () => import('../views/finance/ShipmentsView.vue'),
          meta: { title: '出货管理', requiresAuth: true, permissionsAny: [permissions.shipmentsView] },
        },
        {
          path: 'finance/suppliers',
          name: 'suppliers',
          component: () => import('../views/finance/SuppliersView.vue'),
          meta: { title: '供应商', requiresAuth: true, permissionsAny: [permissions.suppliersView] },
        },
        {
          path: 'audit',
          name: 'audit',
          component: () => import('../views/audit/AuditLogsView.vue'),
          meta: {
            title: '审计日志',
            requiresAuth: true,
            permissionsAny: [permissions.auditView],
          },
        },
        {
          path: 'settings',
          name: 'settings',
          component: () =>
            import('../views/system/SettingsAdministrationView.vue'),
          meta: {
            title: '字典与设置',
            requiresAuth: true,
            permissionsAny: [permissions.settingsView],
          },
        },
        {
          path: 'system',
          name: 'system',
          component: () =>
            import('../views/system/SecurityAdministrationView.vue'),
          meta: {
            title: '用户与角色',
            requiresAuth: true,
            permissionsAny: [
              permissions.securityUsersView,
              permissions.securityRolesView,
            ],
          },
        },
      ],
    },
    {
      path: '/:pathMatch(.*)*',
      redirect: '/dashboard',
    },
  ],
})

export async function authNavigationGuard(to: RouteLocationNormalized) {
  const authStore = useAuthStore()
  await authStore.initialize()

  if (to.name === 'login' && authStore.isAuthenticated) {
    return { name: 'dashboard' }
  }

  if (to.meta.requiresAuth && !authStore.isAuthenticated) {
    return {
      name: 'login',
      query: { redirect: to.fullPath },
    }
  }

  if (
    to.meta.requiresAuth &&
    !authStore.canAny(to.meta.permissionsAny)
  ) {
    return { name: 'forbidden' }
  }

  return true
}

router.beforeEach(authNavigationGuard)

router.afterEach((route) => {
  document.title = route.meta.title
    ? `${String(route.meta.title)} · FlowHearth`
    : 'FlowHearth'
})

export default router
