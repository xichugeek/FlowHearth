<script setup lang="ts">
import { Coin, Money, Refresh, TrendCharts, Wallet } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { getFinanceDashboard, getFinanceProjectRanking } from '../../api/finance'
import FinanceChart from '../../components/finance/FinanceChart.vue'
import type {
  FinanceDashboardSnapshot,
  FinanceProjectRankingMetric,
  FinanceProjectRankingRow,
} from '../../types/finance'
import {
  buildAgingOption,
  buildCashFlowOption,
  projectRankingMetricLabels,
} from '../../utils/finance-dashboard'
import {
  formatFinanceAmount,
  formatFinanceDate,
  formatFinancePercentage,
} from '../../utils/finance-format'
import { projectStatusLabel } from '../../utils/project-format'

const router = useRouter()
const loading = ref(false)
const rankingLoading = ref(false)
const loadFailed = ref(false)
const snapshot = ref<FinanceDashboardSnapshot>()
const projectRanking = ref<FinanceProjectRankingRow[]>([])
const rankingMetric = ref<FinanceProjectRankingMetric>('contractAmount')
const overdueTab = ref<'receivable' | 'payable'>('receivable')
const partnerTab = ref<'customer' | 'supplier'>('customer')

const metricCards = computed(() => {
  const value = snapshot.value?.summary
  if (!value) return []
  return [
    { label: '本月收款', value: value.cashReceivedThisMonth, note: `本年 ${formatFinanceAmount(value.cashReceivedYearToDate)}`, icon: Money, route: '/finance/receipts' },
    { label: '本月付款', value: value.cashPaidThisMonth, note: `本年 ${formatFinanceAmount(value.cashPaidYearToDate)}`, icon: Wallet, route: '/finance/payments' },
    { label: '本月净现金流', value: value.cashNetFlowThisMonth, note: '实际收款减实际付款', icon: TrendCharts, route: '/finance/dashboard', negative: value.cashNetFlowThisMonth < 0 },
    { label: '应收余额', value: value.receivableOutstanding, note: `累计应收 ${formatFinanceAmount(value.receivableAmount)}`, icon: Coin, route: '/finance/receivables' },
    { label: '逾期应收', value: value.receivableOverdue, note: '到期日早于业务日期', icon: Coin, route: '/finance/receivables', danger: value.receivableOverdue > 0 },
    { label: '应付余额', value: value.payableOutstanding, note: '有效应付尚未核销', icon: Wallet, route: '/finance/payables' },
    { label: '逾期应付', value: value.payableOverdue, note: '到期日早于业务日期', icon: Wallet, route: '/finance/payables', danger: value.payableOverdue > 0 },
    { label: '本月采购', value: value.purchaseThisMonth, note: `本年 ${formatFinanceAmount(value.purchaseYearToDate)}`, icon: Coin, route: '/finance/purchases' },
    { label: '活跃项目合同', value: value.activeProjectContractAmount, note: '规划中、进行中及暂停项目', icon: TrendCharts, route: '/projects' },
    { label: '预计项目毛利', value: value.estimatedGrossProfit, note: `预计毛利率 ${formatFinancePercentage(value.estimatedGrossMargin)}`, icon: TrendCharts, route: '/projects', negative: value.estimatedGrossProfit < 0 },
  ]
})

const cashFlowOption = computed(() => buildCashFlowOption(snapshot.value?.cashFlowTrend ?? []))
const receivableAgingOption = computed(() => snapshot.value ? buildAgingOption(snapshot.value.receivableAging) : {})
const payableAgingOption = computed(() => snapshot.value ? buildAgingOption(snapshot.value.payableAging) : {})

async function loadDashboard() {
  loading.value = true
  loadFailed.value = false
  try {
    const result = await getFinanceDashboard()
    snapshot.value = result
    projectRanking.value = result.projectRanking
    rankingMetric.value = result.projectRankingMetric
  } catch {
    loadFailed.value = true
    ElMessage.error('财务总览加载失败，请稍后重试。')
  } finally {
    loading.value = false
  }
}

async function changeRankingMetric(value: string | number | boolean | undefined) {
  if (typeof value !== 'string') return
  const metric = value as FinanceProjectRankingMetric
  rankingLoading.value = true
  try {
    projectRanking.value = await getFinanceProjectRanking(metric)
  } catch {
    ElMessage.error('项目排行加载失败。')
  } finally {
    rankingLoading.value = false
  }
}

function openCustomer(id: number) {
  void router.push({ path: '/customers', query: { entityId: String(id), tab: 'finance' } })
}
function openSupplier(id: number) {
  void router.push({ path: '/finance/suppliers', query: { entityId: String(id) } })
}
function openProject(id: number) {
  void router.push({ path: '/projects', query: { entityId: String(id), tab: 'finance' } })
}
function openReceivable(id: number) {
  void router.push({ path: '/finance/receivables', query: { entityId: String(id) } })
}
function openPayable(id: number) {
  void router.push({ path: '/finance/payables', query: { entityId: String(id) } })
}

onMounted(loadDashboard)
</script>

<template>
  <section
    v-loading="loading"
    class="module-page finance-dashboard-page"
  >
    <div class="finance-dashboard-toolbar">
      <div>
        <strong>经营财务概览</strong>
        <span v-if="snapshot">数据截至 {{ formatFinanceDate(snapshot.asOfDate) }}（Asia/Shanghai）</span>
      </div>
      <el-button
        :icon="Refresh"
        :loading="loading"
        @click="loadDashboard"
      >
        刷新
      </el-button>
    </div>

    <el-result
      v-if="loadFailed && !snapshot"
      icon="error"
      title="财务总览暂时无法加载"
      sub-title="请检查本地服务后重试。"
    >
      <template #extra>
        <el-button
          type="primary"
          @click="loadDashboard"
        >
          重新加载
        </el-button>
      </template>
    </el-result>

    <template v-else-if="snapshot">
      <div class="finance-dashboard-metrics">
        <RouterLink
          v-for="item in metricCards"
          :key="item.label"
          :to="item.route"
          class="finance-kpi-card"
          :class="{ 'is-danger': item.danger, 'is-negative': item.negative }"
        >
          <span class="finance-kpi-icon"><el-icon><component :is="item.icon" /></el-icon></span>
          <span class="finance-kpi-copy">
            <small>{{ item.label }}</small>
            <strong>{{ formatFinanceAmount(item.value) }}</strong>
            <em>{{ item.note }}</em>
          </span>
        </RouterLink>
      </div>

      <el-alert
        title="预计毛利按有效采购订单计算，不含人工、差旅、税费及其他间接费用。"
        type="info"
        :closable="false"
        show-icon
        class="finance-dashboard-definition"
      />

      <div class="finance-dashboard-grid finance-dashboard-grid-main">
        <el-card
          shadow="never"
          class="finance-dashboard-card cash-flow-card"
        >
          <template #header>
            <div class="finance-dashboard-card-title">
              <div><strong>近 12 个月现金流</strong><small>实际收款 / 实际付款 / 净现金流</small></div>
            </div>
          </template>
          <FinanceChart
            :option="cashFlowOption"
            description="近十二个月收付款与净现金流趋势"
            height="330px"
          />
        </el-card>

        <el-card
          shadow="never"
          class="finance-dashboard-card risk-card"
        >
          <template #header>
            <div class="finance-dashboard-card-title">
              <div><strong>经营风险</strong><small>按当前业务日期实时派生</small></div>
            </div>
          </template>
          <div
            v-if="snapshot.risks.length"
            class="finance-risk-list"
          >
            <RouterLink
              v-for="risk in snapshot.risks"
              :key="risk.code"
              :to="risk.targetPath"
              class="finance-risk-item"
              :class="`risk-${risk.severity.toLowerCase()}`"
            >
              <span><strong>{{ risk.title }}</strong><small>{{ risk.itemCount }} 项</small></span>
              <b>{{ formatFinanceAmount(risk.amount) }}</b>
            </RouterLink>
          </div>
          <el-empty
            v-else
            description="当前没有逾期或负毛利风险"
            :image-size="76"
          />
          <el-alert
            v-for="warning in snapshot.summary.warnings"
            :key="warning.code"
            :title="`${warning.message}（${warning.count} 项）`"
            type="warning"
            :closable="false"
            class="finance-warning"
          />
        </el-card>
      </div>

      <div class="finance-dashboard-grid finance-aging-grid">
        <el-card
          shadow="never"
          class="finance-dashboard-card"
        >
          <template #header>
            <div class="finance-dashboard-card-title">
              <div><strong>应收账龄</strong><small>余额 {{ formatFinanceAmount(snapshot.receivableAging.totalOutstanding) }}</small></div>
              <RouterLink to="/finance/receivables">
                查看应收
              </RouterLink>
            </div>
          </template>
          <FinanceChart
            :option="receivableAgingOption"
            description="应收账龄分布"
            height="280px"
          />
        </el-card>
        <el-card
          shadow="never"
          class="finance-dashboard-card"
        >
          <template #header>
            <div class="finance-dashboard-card-title">
              <div><strong>应付账龄</strong><small>余额 {{ formatFinanceAmount(snapshot.payableAging.totalOutstanding) }}</small></div>
              <RouterLink to="/finance/payables">
                查看应付
              </RouterLink>
            </div>
          </template>
          <FinanceChart
            :option="payableAgingOption"
            description="应付账龄分布"
            height="280px"
          />
        </el-card>
      </div>

      <el-card
        shadow="never"
        class="finance-dashboard-card finance-table-card"
      >
        <template #header>
          <div class="finance-dashboard-card-title">
            <div><strong>逾期明细 Top 10</strong><small>按未结余额排序</small></div>
          </div>
        </template>
        <el-tabs v-model="overdueTab">
          <el-tab-pane
            label="逾期应收"
            name="receivable"
          >
            <div class="finance-dashboard-table-scroll">
              <el-table
                class="flowhearth-data-table"
                :data="snapshot.overdueReceivables"
                border
                stripe
                empty-text="暂无逾期应收"
              >
                <el-table-column
                  label="应收编号"
                  min-width="150"
                >
                  <template #default="scope">
                    <el-button
                      link
                      type="primary"
                      @click="openReceivable(scope.row.receivableId)"
                    >
                      {{ scope.row.receivableCode }}
                    </el-button>
                  </template>
                </el-table-column>
                <el-table-column
                  label="客户"
                  min-width="210"
                >
                  <template #default="scope">
                    <el-button
                      link
                      @click="openCustomer(scope.row.customerId)"
                    >
                      {{ scope.row.customerName }}
                    </el-button>
                  </template>
                </el-table-column>
                <el-table-column
                  label="项目"
                  min-width="210"
                >
                  <template #default="scope">
                    <el-button
                      link
                      @click="openProject(scope.row.projectId)"
                    >
                      {{ scope.row.projectCode }} · {{ scope.row.projectName }}
                    </el-button>
                  </template>
                </el-table-column>
                <el-table-column
                  label="到期日"
                  width="120"
                >
                  <template #default="scope">
                    {{ formatFinanceDate(scope.row.dueDate) }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="overdueDays"
                  label="逾期天数"
                  width="105"
                  align="right"
                />
                <el-table-column
                  label="未收金额"
                  width="150"
                  align="right"
                >
                  <template #default="scope">
                    <strong class="finance-danger-text">{{ formatFinanceAmount(scope.row.remainingAmount) }}</strong>
                  </template>
                </el-table-column>
              </el-table>
            </div>
          </el-tab-pane>
          <el-tab-pane
            label="逾期应付"
            name="payable"
          >
            <div class="finance-dashboard-table-scroll">
              <el-table
                class="flowhearth-data-table"
                :data="snapshot.overduePayables"
                border
                stripe
                empty-text="暂无逾期应付"
              >
                <el-table-column
                  label="应付编号"
                  min-width="150"
                >
                  <template #default="scope">
                    <el-button
                      link
                      type="primary"
                      @click="openPayable(scope.row.payableId)"
                    >
                      {{ scope.row.payableCode }}
                    </el-button>
                  </template>
                </el-table-column>
                <el-table-column
                  label="供应商"
                  min-width="210"
                >
                  <template #default="scope">
                    <el-button
                      link
                      @click="openSupplier(scope.row.supplierId)"
                    >
                      {{ scope.row.supplierName }}
                    </el-button>
                  </template>
                </el-table-column>
                <el-table-column
                  label="项目"
                  min-width="210"
                >
                  <template #default="scope">
                    <el-button
                      link
                      @click="openProject(scope.row.projectId)"
                    >
                      {{ scope.row.projectCode }} · {{ scope.row.projectName }}
                    </el-button>
                  </template>
                </el-table-column>
                <el-table-column
                  label="到期日"
                  width="120"
                >
                  <template #default="scope">
                    {{ formatFinanceDate(scope.row.dueDate) }}
                  </template>
                </el-table-column>
                <el-table-column
                  prop="overdueDays"
                  label="逾期天数"
                  width="105"
                  align="right"
                />
                <el-table-column
                  label="未付金额"
                  width="150"
                  align="right"
                >
                  <template #default="scope">
                    <strong class="finance-danger-text">{{ formatFinanceAmount(scope.row.remainingAmount) }}</strong>
                  </template>
                </el-table-column>
              </el-table>
            </div>
          </el-tab-pane>
        </el-tabs>
      </el-card>

      <div class="finance-dashboard-grid finance-ranking-grid">
        <el-card
          shadow="never"
          class="finance-dashboard-card finance-table-card"
        >
          <template #header>
            <div class="finance-dashboard-card-title ranking-title">
              <div><strong>项目经营排行</strong><small>有效且未归档项目 Top 10</small></div>
              <el-select
                v-model="rankingMetric"
                aria-label="项目排行指标"
                class="ranking-select"
                @change="changeRankingMetric"
              >
                <el-option
                  v-for="(label, key) in projectRankingMetricLabels"
                  :key="key"
                  :label="label"
                  :value="key"
                />
              </el-select>
            </div>
          </template>
          <div class="finance-dashboard-table-scroll project-ranking-table">
            <el-table
              v-loading="rankingLoading"
              class="flowhearth-data-table"
              :data="projectRanking"
              border
              stripe
              empty-text="暂无项目数据"
            >
              <el-table-column
                type="index"
                label="#"
                width="52"
              />
              <el-table-column
                label="项目"
                min-width="230"
              >
                <template #default="scope">
                  <el-button
                    link
                    type="primary"
                    @click="openProject(scope.row.projectId)"
                  >
                    {{ scope.row.projectCode }} · {{ scope.row.projectName }}
                  </el-button><small class="table-secondary">{{ scope.row.customerName }} · {{ projectStatusLabel(scope.row.projectStatus) }}</small>
                </template>
              </el-table-column>
              <el-table-column
                label="合同金额"
                width="145"
                align="right"
              >
                <template #default="scope">
                  {{ formatFinanceAmount(scope.row.contractAmount) }}
                </template>
              </el-table-column>
              <el-table-column
                label="应收余额"
                width="145"
                align="right"
              >
                <template #default="scope">
                  {{ formatFinanceAmount(scope.row.receivableOutstandingAmount) }}
                </template>
              </el-table-column>
              <el-table-column
                label="逾期应收"
                width="145"
                align="right"
              >
                <template #default="scope">
                  <span :class="{ 'finance-danger-text': scope.row.receivableOverdueAmount > 0 }">{{ formatFinanceAmount(scope.row.receivableOverdueAmount) }}</span>
                </template>
              </el-table-column>
              <el-table-column
                label="采购金额"
                width="145"
                align="right"
              >
                <template #default="scope">
                  {{ formatFinanceAmount(scope.row.purchaseAmount) }}
                </template>
              </el-table-column>
              <el-table-column
                label="预计毛利"
                width="145"
                align="right"
              >
                <template #default="scope">
                  <strong :class="{ 'finance-negative': scope.row.grossProfit < 0 }">{{ formatFinanceAmount(scope.row.grossProfit) }}</strong>
                </template>
              </el-table-column>
              <el-table-column
                label="预计毛利率"
                width="125"
                align="right"
              >
                <template #default="scope">
                  <span :class="{ 'finance-negative': scope.row.grossProfit < 0 }">{{ formatFinancePercentage(scope.row.grossMargin) }}</span>
                </template>
              </el-table-column>
            </el-table>
          </div>
        </el-card>

        <el-card
          shadow="never"
          class="finance-dashboard-card finance-table-card"
        >
          <template #header>
            <div class="finance-dashboard-card-title">
              <div><strong>往来余额排行</strong><small>未收 / 未付 Top 10</small></div>
            </div>
          </template>
          <el-tabs v-model="partnerTab">
            <el-tab-pane
              label="客户应收"
              name="customer"
            >
              <div class="finance-dashboard-table-scroll partner-ranking-table">
                <el-table
                  class="flowhearth-data-table"
                  :data="snapshot.customerReceivableRanking"
                  border
                  stripe
                  empty-text="暂无客户应收"
                >
                  <el-table-column
                    type="index"
                    label="#"
                    width="52"
                  />
                  <el-table-column
                    label="客户"
                    min-width="210"
                  >
                    <template #default="scope">
                      <el-button
                        link
                        type="primary"
                        @click="openCustomer(scope.row.customerId)"
                      >
                        {{ scope.row.customerName }}
                      </el-button><small class="table-secondary">{{ scope.row.customerCode }}</small>
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="合同金额"
                    width="140"
                    align="right"
                  >
                    <template #default="scope">
                      {{ formatFinanceAmount(scope.row.contractAmount) }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="应收余额"
                    width="140"
                    align="right"
                  >
                    <template #default="scope">
                      {{ formatFinanceAmount(scope.row.outstandingAmount) }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="逾期应收"
                    width="140"
                    align="right"
                  >
                    <template #default="scope">
                      <span :class="{ 'finance-danger-text': scope.row.overdueAmount > 0 }">{{ formatFinanceAmount(scope.row.overdueAmount) }}</span>
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="累计到账"
                    width="140"
                    align="right"
                  >
                    <template #default="scope">
                      {{ formatFinanceAmount(scope.row.cashReceivedAmount) }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="未分配收款"
                    width="150"
                    align="right"
                  >
                    <template #default="scope">
                      {{ formatFinanceAmount(scope.row.unallocatedReceiptAmount) }}
                    </template>
                  </el-table-column>
                </el-table>
              </div>
            </el-tab-pane>
            <el-tab-pane
              label="供应商应付"
              name="supplier"
            >
              <div class="finance-dashboard-table-scroll partner-ranking-table">
                <el-table
                  class="flowhearth-data-table"
                  :data="snapshot.supplierPayableRanking"
                  border
                  stripe
                  empty-text="暂无供应商应付"
                >
                  <el-table-column
                    type="index"
                    label="#"
                    width="52"
                  />
                  <el-table-column
                    label="供应商"
                    min-width="210"
                  >
                    <template #default="scope">
                      <el-button
                        link
                        type="primary"
                        @click="openSupplier(scope.row.supplierId)"
                      >
                        {{ scope.row.supplierName }}
                      </el-button><small class="table-secondary">{{ scope.row.supplierCode }}</small>
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="采购金额"
                    width="140"
                    align="right"
                  >
                    <template #default="scope">
                      {{ formatFinanceAmount(scope.row.purchaseAmount) }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="应付余额"
                    width="140"
                    align="right"
                  >
                    <template #default="scope">
                      {{ formatFinanceAmount(scope.row.outstandingAmount) }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="逾期应付"
                    width="140"
                    align="right"
                  >
                    <template #default="scope">
                      <span :class="{ 'finance-danger-text': scope.row.overdueAmount > 0 }">{{ formatFinanceAmount(scope.row.overdueAmount) }}</span>
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="累计付款"
                    width="140"
                    align="right"
                  >
                    <template #default="scope">
                      {{ formatFinanceAmount(scope.row.cashPaidAmount) }}
                    </template>
                  </el-table-column>
                  <el-table-column
                    label="未核销付款"
                    width="150"
                    align="right"
                  >
                    <template #default="scope">
                      {{ formatFinanceAmount(scope.row.unallocatedPaymentAmount) }}
                    </template>
                  </el-table-column>
                </el-table>
              </div>
            </el-tab-pane>
          </el-tabs>
        </el-card>
      </div>

      <p class="finance-dashboard-updated">
        本页展示实时聚合结果，生成时间 {{ new Date(snapshot.generatedAtUtc).toLocaleString('zh-CN', { hour12: false }) }}。
      </p>
    </template>
  </section>
</template>

<style scoped>
.finance-dashboard-page { max-width: 1540px; margin: 0 auto; }
.finance-dashboard-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-bottom: 16px; }
.finance-dashboard-toolbar div { display: flex; flex-direction: column; gap: 4px; }
.finance-dashboard-toolbar strong { color: var(--flowhearth-ink); font-size: 18px; }
.finance-dashboard-toolbar span { color: #718592; font-size: 12px; }
.finance-dashboard-metrics { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 12px; }
.finance-kpi-card { display: flex; min-height: 126px; align-items: flex-start; gap: 12px; padding: 16px; border: 1px solid var(--flowhearth-border); border-radius: 12px; background: #fff; transition: border-color 150ms ease, transform 150ms ease, box-shadow 150ms ease; }
.finance-kpi-card:hover { transform: translateY(-2px); border-color: #aebfca; box-shadow: 0 8px 20px rgba(23, 58, 94, .08); }
.finance-kpi-icon { display: grid; width: 36px; height: 36px; flex: 0 0 36px; place-items: center; border-radius: 10px; color: #b87816; background: #fff4df; }
.finance-kpi-copy { min-width: 0; }
.finance-kpi-copy small, .finance-kpi-copy strong, .finance-kpi-copy em { display: block; }
.finance-kpi-copy small { color: #6e818e; font-size: 12px; }
.finance-kpi-copy strong { margin: 7px 0 6px; overflow: hidden; color: var(--flowhearth-ink); font-size: clamp(17px, 1.55vw, 23px); text-overflow: ellipsis; white-space: nowrap; }
.finance-kpi-copy em { color: #91a0aa; font-size: 11px; font-style: normal; line-height: 1.4; }
.finance-kpi-card.is-danger .finance-kpi-icon, .finance-kpi-card.is-negative .finance-kpi-icon { color: #c45656; background: #fdf0f0; }
.finance-kpi-card.is-danger strong, .finance-kpi-card.is-negative strong { color: #b83d3d; }
.finance-dashboard-definition { margin-top: 12px; }
.finance-dashboard-grid { display: grid; gap: 14px; margin-top: 14px; }
.finance-dashboard-grid-main { grid-template-columns: minmax(0, 2fr) minmax(290px, .8fr); }
.finance-aging-grid, .finance-ranking-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
.finance-dashboard-card { min-width: 0; border-radius: 12px; }
.finance-dashboard-card-title { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.finance-dashboard-card-title div { display: flex; flex-direction: column; gap: 3px; }
.finance-dashboard-card-title strong { color: var(--flowhearth-ink); font-size: 15px; }
.finance-dashboard-card-title small, .finance-dashboard-card-title a { color: #778b99; font-size: 12px; font-weight: 400; }
.finance-dashboard-card-title a:hover { color: #b87816; }
.finance-risk-list { display: grid; gap: 10px; }
.finance-risk-item { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 15px; border: 1px solid #f0d6d6; border-left: 4px solid #c45656; border-radius: 9px; background: #fffafa; }
.finance-risk-item.risk-warning { border-color: #f1dfbd; border-left-color: #d99a2b; background: #fffaf0; }
.finance-risk-item span { display: flex; flex-direction: column; gap: 4px; }
.finance-risk-item small { color: #8a99a3; }
.finance-risk-item b { color: #b83d3d; white-space: nowrap; }
.finance-warning { margin-top: 10px; }
.finance-table-card { margin-top: 14px; }
.finance-dashboard-table-scroll { width: 100%; overflow-x: auto; }
.finance-dashboard-table-scroll :deep(.el-table) { min-width: 920px; }
.project-ranking-table :deep(.el-table) { min-width: 1120px; }
.partner-ranking-table :deep(.el-table) { min-width: 970px; }
.table-secondary { display: block; padding: 0 15px; color: #8b99a3; font-size: 11px; }
.finance-danger-text { color: #c45656; font-weight: 600; }
.ranking-select { width: 140px; }
.finance-dashboard-updated { margin: 14px 2px 0; color: #8796a0; font-size: 11px; text-align: right; }
@media (max-width: 1500px) {
  .finance-kpi-card { gap: 8px; padding: 14px 12px; }
  .finance-kpi-icon { width: 32px; height: 32px; flex-basis: 32px; }
  .finance-kpi-copy strong { font-size: 18px; }
}
@media (max-width: 1220px) {
  .finance-dashboard-metrics { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .finance-dashboard-grid-main { grid-template-columns: minmax(0, 1.55fr) minmax(280px, .75fr); }
}
@media (max-width: 980px) {
  .finance-dashboard-metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .finance-dashboard-grid-main, .finance-aging-grid, .finance-ranking-grid { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 620px) {
  .finance-dashboard-metrics { grid-template-columns: minmax(0, 1fr); }
  .finance-dashboard-toolbar, .ranking-title { align-items: stretch; flex-direction: column; }
  .ranking-select { width: 100%; }
}
</style>
