<script setup lang="ts">
import {
  ArrowDown,
  Briefcase,
  Coin,
  Connection,
  DataAnalysis,
  Expand,
  Fold,
  Lock,
  Monitor,
  Operation,
  Service,
  Setting,
  User,
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { Component } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import ChangePasswordDialog from '../components/security/ChangePasswordDialog.vue'
import GlobalSearchBox from '../components/search/GlobalSearchBox.vue'
import { permissions } from '../security/permissions'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const router = useRouter()
const appStore = useAppStore()
const authStore = useAuthStore()
const changePasswordVisible = ref(false)
const narrowViewportQuery = window.matchMedia('(max-width: 760px)')
const isNarrowViewport = ref(narrowViewportQuery.matches)

function syncNarrowViewport(event: MediaQueryListEvent) {
  isNarrowViewport.value = event.matches
}

onMounted(() => narrowViewportQuery.addEventListener('change', syncNarrowViewport))
onBeforeUnmount(() =>
  narrowViewportQuery.removeEventListener('change', syncNarrowViewport),
)

type MenuItem = {
  path: string
  label: string
  icon: Component
  permission: string | readonly string[]
}

const businessMenuItems: MenuItem[] = [
  { path: '/dashboard', label: '工作台', icon: DataAnalysis, permission: permissions.dashboardView },
  { path: '/customers', label: '客户', icon: User, permission: permissions.customersView },
  { path: '/opportunities', label: '商机', icon: Connection, permission: permissions.opportunitiesView },
  { path: '/projects', label: '项目', icon: Briefcase, permission: permissions.projectsView },
  { path: '/equipment', label: '设备', icon: Monitor, permission: permissions.equipmentView },
  { path: '/service', label: '服务', icon: Service, permission: permissions.serviceView },
]

const administrationMenuItems: MenuItem[] = [
  { path: '/audit', label: '审计', icon: Operation, permission: permissions.auditView },
  {
    path: '/settings',
    label: '字典与设置',
    icon: Setting,
    permission: permissions.settingsView,
  },
  {
    path: '/system',
    label: '用户与角色',
    icon: Lock,
    permission: [permissions.securityUsersView, permissions.securityRolesView],
  },
]

function filterVisibleMenuItems(items: MenuItem[]) {
  return items.filter((item) =>
    authStore.canAny(
      Array.isArray(item.permission) ? item.permission : [item.permission],
    ),
  )
}

const visibleBusinessMenuItems = computed(() =>
  filterVisibleMenuItems(businessMenuItems),
)
const visibleAdministrationMenuItems = computed(() =>
  filterVisibleMenuItems(administrationMenuItems),
)

const financeMenuItems = [
  { path: '/finance/dashboard', label: '财务总览', permission: permissions.financeDashboardView },
  { path: '/finance/receivables', label: '应收账款', permission: permissions.receivablesView },
  { path: '/finance/receipts', label: '收款记录', permission: permissions.receiptsView },
  { path: '/finance/purchases', label: '采购管理', permission: permissions.purchasesView },
  { path: '/finance/payables', label: '应付账款', permission: permissions.payablesView },
  { path: '/finance/payments', label: '付款记录', permission: permissions.paymentsView },
  { path: '/finance/shipments', label: '出货管理', permission: permissions.shipmentsView },
  { path: '/finance/suppliers', label: '供应商', permission: permissions.suppliersView },
]

const visibleFinanceMenuItems = computed(() =>
  financeMenuItems.filter((item) => authStore.canAny([item.permission])),
)

const pageTitle = computed(() => String(route.meta.title ?? 'FlowHearth'))
const canSearch = computed(() => authStore.canAny([permissions.searchUse]))
const sidebarCollapsed = computed(
  () => appStore.sidebarCollapsed || isNarrowViewport.value,
)

async function handleUserCommand(command: string) {
  if (command === 'password') {
    changePasswordVisible.value = true
    return
  }

  if (command === 'logout') {
    try {
      await authStore.logout()
      await router.replace('/login')
    } catch {
      ElMessage.error('退出登录失败，请重试。')
    }
  }
}
</script>

<template>
  <el-container class="app-shell">
    <el-aside
      class="app-sidebar"
      :width="sidebarCollapsed ? '72px' : '232px'"
    >
      <RouterLink
        class="brand"
        to="/dashboard"
        aria-label="FlowHearth 工作台"
      >
        <span class="brand-mark">F</span>
        <span
          v-if="!sidebarCollapsed"
          class="brand-copy"
        >
          <strong>FlowHearth</strong>
        </span>
      </RouterLink>

      <el-menu
        class="module-menu"
        router
        :collapse="sidebarCollapsed"
        :collapse-transition="false"
        :default-active="route.path"
      >
        <el-menu-item
          v-for="item in visibleBusinessMenuItems"
          :key="item.path"
          :index="item.path"
        >
          <el-icon><component :is="item.icon" /></el-icon>
          <template #title>
            {{ item.label }}
          </template>
        </el-menu-item>
        <el-sub-menu
          v-if="visibleFinanceMenuItems.length > 0"
          index="/finance"
        >
          <template #title>
            <el-icon><Coin /></el-icon>
            <span>经营财务</span>
          </template>
          <el-menu-item
            v-for="item in visibleFinanceMenuItems"
            :key="item.path"
            :index="item.path"
          >
            {{ item.label }}
          </el-menu-item>
        </el-sub-menu>
        <el-menu-item
          v-for="item in visibleAdministrationMenuItems"
          :key="item.path"
          :index="item.path"
        >
          <el-icon><component :is="item.icon" /></el-icon>
          <template #title>
            {{ item.label }}
          </template>
        </el-menu-item>
      </el-menu>
    </el-aside>

    <el-container class="app-workspace">
      <el-header class="app-header">
        <div class="header-leading">
          <el-button
            text
            circle
            :aria-label="sidebarCollapsed ? '展开导航' : '收起导航'"
            @click="appStore.toggleSidebar"
          >
            <el-icon :size="20">
              <Expand v-if="sidebarCollapsed" />
              <Fold v-else />
            </el-icon>
          </el-button>
          <div>
            <p class="page-eyebrow">
              FLOWHEARTH
            </p>
            <h1>{{ pageTitle }}</h1>
          </div>
        </div>

        <div class="header-actions">
          <GlobalSearchBox v-if="canSearch" />
          <div class="header-user">
            <span class="user-avatar">{{ authStore.currentUser?.displayName.slice(0, 1) }}</span>
            <el-dropdown
              trigger="click"
              @command="handleUserCommand"
            >
              <button
                class="user-trigger"
                type="button"
              >
                <span>
                  <strong>{{ authStore.currentUser?.displayName }}</strong>
                  <small>{{ authStore.currentUser?.username }}</small>
                </span>
                <el-icon><ArrowDown /></el-icon>
              </button>
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item command="password">
                    修改密码
                  </el-dropdown-item>
                  <el-dropdown-item
                    command="logout"
                    divided
                  >
                    退出登录
                  </el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </div>
        </div>
      </el-header>

      <el-main class="app-main">
        <RouterView />
      </el-main>
    </el-container>
    <ChangePasswordDialog v-model="changePasswordVisible" />
  </el-container>
</template>
