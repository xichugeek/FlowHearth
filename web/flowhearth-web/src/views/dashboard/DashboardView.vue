<script setup lang="ts">
import {
  Calendar,
  Connection,
  Money,
  Refresh,
  Service,
  TrendCharts,
  User,
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { computed, onMounted, ref } from 'vue'

import { getDashboard } from '../../api/dashboard'
import type {
  DashboardDistributionItem,
  DashboardSnapshot,
} from '../../types/dashboard'

const loading = ref(false)
const loadFailed = ref(false)
const snapshot = ref<DashboardSnapshot | null>(null)

const opportunityLabels: Record<string, string> = {
  Lead: '线索',
  Qualified: '已确认',
  Proposal: '方案/报价',
  Negotiation: '商务谈判',
  Won: '赢单',
  Lost: '丢单',
}

const projectLabels: Record<string, string> = {
  Planning: '规划中',
  Active: '进行中',
  OnHold: '暂停',
  Completed: '已完成',
  Cancelled: '已取消',
}

const metricCards = computed(() => {
  const metrics = snapshot.value?.metrics
  if (!metrics) return []
  return [
    { label: '客户总数', value: metrics.totalCustomers, icon: User, route: '/customers' },
    { label: '本月新增客户', value: metrics.newCustomersThisMonth, icon: User, route: '/customers' },
    { label: '活跃商机', value: metrics.activeOpportunities, icon: Connection, route: '/opportunities' },
    { label: '活跃商机金额', value: metrics.expectedOpportunityAmount, icon: Money, route: '/opportunities', currency: true },
    { label: '进行中项目', value: metrics.activeProjects, icon: TrendCharts, route: '/projects' },
    { label: '30 天内交付', value: metrics.projectsNearingDelivery, icon: Calendar, route: '/projects' },
    { label: '未关闭工单', value: metrics.openTickets, icon: Service, route: '/service' },
    { label: '未关闭 P1/P2', value: metrics.openPriorityTickets, icon: Service, route: '/service' },
    { label: '今日待跟进', value: metrics.dueFollowUpsToday, icon: Calendar, route: '/customers' },
    { label: '未来 7 天跟进', value: metrics.nextSevenDaysFollowUps, icon: Calendar, route: '/customers' },
  ].filter((item) => item.value !== null && item.value !== undefined)
})

const trendMaximum = computed(() =>
  Math.max(1, ...(snapshot.value?.monthlyNewCustomers.map((item) => item.count) ?? [])),
)

function distributionMaximum(items: DashboardDistributionItem[]) {
  return Math.max(1, ...items.map((item) => item.count))
}

function metricValue(value: number | null | undefined, currency = false) {
  if (value === null || value === undefined) return '—'
  return currency
    ? new Intl.NumberFormat('zh-CN', {
        style: 'currency',
        currency: 'CNY',
        maximumFractionDigits: 0,
      }).format(value)
    : new Intl.NumberFormat('zh-CN').format(value)
}

async function loadDashboard() {
  loading.value = true
  loadFailed.value = false
  try {
    snapshot.value = await getDashboard()
  } catch {
    loadFailed.value = true
    ElMessage.error('工作台数据加载失败，请稍后重试。')
  } finally {
    loading.value = false
  }
}

onMounted(loadDashboard)
</script>

<template>
  <section
    v-loading="loading"
    class="dashboard-page dashboard-insights"
  >
    <div class="compact-page-actions">
      <el-button
        :icon="Refresh"
        :loading="loading"
        @click="loadDashboard"
      >
        刷新
      </el-button>
    </div>

    <el-alert
      v-if="loadFailed"
      title="工作台数据更新失败"
      description="当前内容可能不是最新结果，请点击刷新重试。"
      type="error"
      :closable="false"
      show-icon
    />

    <el-skeleton
      v-if="loading && !snapshot"
      :rows="8"
      animated
    />

    <div
      v-if="snapshot"
      class="dashboard-metrics"
    >
      <RouterLink
        v-for="item in metricCards"
        :key="item.label"
        class="dashboard-metric-card"
        :class="{ 'is-currency': item.currency }"
        :to="item.route"
      >
        <span class="dashboard-metric-icon">
          <el-icon><component :is="item.icon" /></el-icon>
        </span>
        <span>
          <small>{{ item.label }}</small>
          <strong>{{ metricValue(item.value, item.currency) }}</strong>
        </span>
      </RouterLink>
    </div>

    <div
      v-if="snapshot"
      class="dashboard-chart-grid"
    >
      <el-card
        v-if="snapshot?.opportunityStages.length"
        shadow="never"
      >
        <template #header>
          <div class="dashboard-card-title">
            <span>商机阶段分布</span>
            <RouterLink to="/opportunities">
              查看商机
            </RouterLink>
          </div>
        </template>
        <div class="distribution-chart">
          <div
            v-for="item in snapshot.opportunityStages"
            :key="item.key"
            class="distribution-row"
          >
            <span>{{ opportunityLabels[item.key] ?? item.key }}</span>
            <div><i :style="{ width: `${(item.count / distributionMaximum(snapshot.opportunityStages)) * 100}%` }" /></div>
            <strong>{{ item.count }}</strong>
          </div>
        </div>
      </el-card>

      <el-card
        v-if="snapshot?.projectStatuses.length"
        shadow="never"
      >
        <template #header>
          <div class="dashboard-card-title">
            <span>项目状态分布</span>
            <RouterLink to="/projects">
              查看项目
            </RouterLink>
          </div>
        </template>
        <div class="distribution-chart project-distribution">
          <div
            v-for="item in snapshot.projectStatuses"
            :key="item.key"
            class="distribution-row"
          >
            <span>{{ projectLabels[item.key] ?? item.key }}</span>
            <div><i :style="{ width: `${(item.count / distributionMaximum(snapshot.projectStatuses)) * 100}%` }" /></div>
            <strong>{{ item.count }}</strong>
          </div>
        </div>
      </el-card>

      <el-card
        v-if="snapshot?.monthlyNewCustomers.length"
        class="customer-trend-card"
        shadow="never"
      >
        <template #header>
          <div class="dashboard-card-title">
            <span>近 6 个月新增客户</span>
            <small>按 Asia/Shanghai 月份统计</small>
          </div>
        </template>
        <div
          class="trend-chart"
          aria-label="近 6 个月新增客户趋势"
        >
          <div
            v-for="item in snapshot.monthlyNewCustomers"
            :key="item.month"
            class="trend-column"
          >
            <strong>{{ item.count }}</strong>
            <div><i :style="{ height: `${Math.max(4, (item.count / trendMaximum) * 100)}%` }" /></div>
            <span>{{ item.month.slice(5) }}月</span>
          </div>
        </div>
      </el-card>
    </div>

    <el-empty
      v-if="!loading && snapshot && metricCards.length === 0"
      description="当前账号没有可展示的业务模块数据"
    />
  </section>
</template>
