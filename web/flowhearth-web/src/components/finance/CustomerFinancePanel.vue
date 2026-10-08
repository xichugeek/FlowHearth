<script setup lang="ts">
import { Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import { getCustomerFinance, listCustomerProjectFinance } from '../../api/finance'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import type { CustomerFinanceOverview, CustomerProjectFinanceRow } from '../../types/finance'
import { formatFinanceAmount, formatFinanceDate, formatFinancePercentage } from '../../utils/finance-format'
import { projectStatusLabel } from '../../utils/project-format'

const props = defineProps<{ customerId: number }>()
const router = useRouter()
const auth = useAuthStore()
const canViewProjects = computed(() => auth.canAny([permissions.projectsView]))
const loading = ref(false)
const errorMessage = ref('')
const data = ref<CustomerFinanceOverview>()
const projects = ref<CustomerProjectFinanceRow[]>([])
const page = ref(1)
const pageSize = ref(10)
const total = ref(0)
const sortBy = ref('updatedAt')
const sortDescending = ref(true)

onMounted(load)
watch(() => props.customerId, () => { page.value = 1; void load() })

async function load() {
  loading.value = true
  errorMessage.value = ''
  try {
    data.value = await getCustomerFinance(props.customerId)
    if (canViewProjects.value) await loadProjects()
  } catch {
    errorMessage.value = '客户经营汇总加载失败，请稍后重试。'
    ElMessage.error(errorMessage.value)
  } finally {
    loading.value = false
  }
}

async function loadProjects() {
  const result = await listCustomerProjectFinance(props.customerId, {
    page: page.value,
    pageSize: pageSize.value,
    sortBy: sortBy.value,
    sortDescending: sortDescending.value,
  })
  projects.value = result.items
  total.value = result.total
}

async function changePage(value: number) {
  page.value = value
  loading.value = true
  try { await loadProjects() } catch { ElMessage.error('项目经营数据加载失败。') } finally { loading.value = false }
}

async function changeSort(value: { prop?: string | null; order?: string | null }) {
  const fieldMap: Record<string, string> = {
    contractAmount: 'contractAmount',
    receivableOutstandingAmount: 'outstandingAmount',
    receivableOverdueAmount: 'overdueAmount',
    purchaseAmount: 'purchaseAmount',
    grossProfit: 'grossProfit',
    grossMargin: 'grossMargin',
  }
  sortBy.value = value.prop ? fieldMap[value.prop] ?? 'updatedAt' : 'updatedAt'
  sortDescending.value = value.order !== 'ascending'
  await changePage(1)
}
</script>

<template>
  <div v-loading="loading">
    <div class="detail-tab-toolbar">
      <el-button
        :icon="Refresh"
        @click="load"
      >
        刷新经营数据
      </el-button>
    </div>
    <el-alert
      v-if="errorMessage"
      :title="errorMessage"
      type="error"
      :closable="false"
      show-icon
    />
    <template v-if="data">
      <h3 class="finance-section-title finance-section-title-first">
        合同与收入
      </h3>
      <div class="finance-card-grid finance-card-grid-six">
        <div><span>合同总额</span><strong>{{ formatFinanceAmount(data.contractAmount) }}</strong></div>
        <div><span>累计到账</span><strong>{{ formatFinanceAmount(data.receiptAmount) }}</strong></div>
        <div><span>已核销实收</span><strong>{{ formatFinanceAmount(data.receivedAllocatedAmount) }}</strong></div>
        <div><span>未分配收款</span><strong>{{ formatFinanceAmount(data.unallocatedReceiptAmount) }}</strong></div>
        <div><span>应收余额</span><strong>{{ formatFinanceAmount(data.receivableOutstandingAmount) }}</strong></div>
        <div><span>逾期应收</span><strong :class="{ 'finance-negative': data.receivableOverdueAmount > 0 }">{{ formatFinanceAmount(data.receivableOverdueAmount) }}</strong></div>
      </div>

      <h3 class="finance-section-title">
        采购与经营结果
      </h3>
      <div class="finance-card-grid finance-card-grid-six">
        <div><span>采购金额</span><strong>{{ formatFinanceAmount(data.purchaseAmount) }}</strong></div>
        <div><span>预计毛利</span><strong :class="{ 'finance-negative': data.estimatedGrossProfit < 0 }">{{ formatFinanceAmount(data.estimatedGrossProfit) }}</strong></div>
        <div><span>预计毛利率</span><strong :class="{ 'finance-negative': (data.estimatedGrossMargin ?? 0) < 0 }">{{ formatFinancePercentage(data.estimatedGrossMargin) }}</strong></div>
        <div><span>项目数量</span><strong>{{ data.projectCount }}</strong></div>
        <div><span>执行中项目</span><strong>{{ data.activeProjectCount }}</strong></div>
        <div><span>最近出货</span><strong>{{ formatFinanceDate(data.lastShipmentDate) }}</strong></div>
      </div>

      <h3 class="finance-section-title">
        供应商资金与交付
      </h3>
      <div class="finance-card-grid finance-card-grid-six">
        <div><span>累计应付</span><strong>{{ formatFinanceAmount(data.payableAmount) }}</strong></div>
        <div><span>已核销实付</span><strong>{{ formatFinanceAmount(data.paidAllocatedAmount) }}</strong></div>
        <div><span>应付余额</span><strong>{{ formatFinanceAmount(data.payableOutstandingAmount) }}</strong></div>
        <div><span>逾期应付</span><strong :class="{ 'finance-negative': data.payableOverdueAmount > 0 }">{{ formatFinanceAmount(data.payableOverdueAmount) }}</strong></div>
        <div><span>正式出货</span><strong>{{ data.shipmentCount }}</strong></div>
        <div><span>已签收出货</span><strong>{{ data.receivedShipmentCount }}</strong></div>
      </div>

      <el-alert
        title="预计毛利按有效项目合同金额减已登记正式采购订单计算，不含人工、差旅、工资、税费及其他间接成本。"
        type="info"
        :closable="false"
      />
      <el-alert
        v-if="data.receivableOverdueAmount > 0"
        :title="`存在逾期应收 ${formatFinanceAmount(data.receivableOverdueAmount)}`"
        type="warning"
        :closable="false"
        class="finance-alert"
      />
      <el-alert
        v-if="data.payableOverdueAmount > 0"
        :title="`存在逾期应付 ${formatFinanceAmount(data.payableOverdueAmount)}`"
        type="warning"
        :closable="false"
        class="finance-alert"
      />
      <el-alert
        v-for="warning in data.warnings"
        :key="warning.code"
        :title="`${warning.message}（${warning.count} 项）`"
        type="error"
        :closable="false"
        class="finance-alert"
      />

      <template v-if="canViewProjects">
        <h3 class="finance-section-title">
          项目经营表
        </h3>
        <div class="finance-table-scroll">
          <el-table
            :data="projects"
            stripe
            table-layout="fixed"
            @sort-change="changeSort"
          >
            <el-table-column
              prop="projectCode"
              label="项目编号"
              width="140"
              fixed="left"
            />
            <el-table-column
              prop="projectName"
              label="项目名称"
              min-width="220"
              fixed="left"
            />
            <el-table-column
              label="状态"
              width="100"
            >
              <template #default="scope">
                {{ projectStatusLabel(scope.row.projectStatus) }}
              </template>
            </el-table-column>
            <el-table-column
              prop="contractAmount"
              label="合同金额"
              width="145"
              align="right"
              sortable="custom"
            >
              <template #default="scope">
                {{ formatFinanceAmount(scope.row.contractAmount) }}
              </template>
            </el-table-column>
            <el-table-column
              label="应收"
              width="135"
              align="right"
            >
              <template #default="scope">
                {{ formatFinanceAmount(scope.row.receivableAmount) }}
              </template>
            </el-table-column>
            <el-table-column
              label="实收"
              width="135"
              align="right"
            >
              <template #default="scope">
                {{ formatFinanceAmount(scope.row.receivedAllocatedAmount) }}
              </template>
            </el-table-column>
            <el-table-column
              prop="receivableOutstandingAmount"
              label="未收"
              width="135"
              align="right"
              sortable="custom"
            >
              <template #default="scope">
                {{ formatFinanceAmount(scope.row.receivableOutstandingAmount) }}
              </template>
            </el-table-column>
            <el-table-column
              prop="receivableOverdueAmount"
              label="逾期"
              width="135"
              align="right"
              sortable="custom"
            >
              <template #default="scope">
                <span :class="{ 'finance-negative': scope.row.receivableOverdueAmount > 0 }">{{ formatFinanceAmount(scope.row.receivableOverdueAmount) }}</span>
              </template>
            </el-table-column>
            <el-table-column
              prop="purchaseAmount"
              label="采购"
              width="135"
              align="right"
              sortable="custom"
            >
              <template #default="scope">
                {{ formatFinanceAmount(scope.row.purchaseAmount) }}
              </template>
            </el-table-column>
            <el-table-column
              label="应付"
              width="135"
              align="right"
            >
              <template #default="scope">
                {{ formatFinanceAmount(scope.row.payableAmount) }}
              </template>
            </el-table-column>
            <el-table-column
              label="实付"
              width="135"
              align="right"
            >
              <template #default="scope">
                {{ formatFinanceAmount(scope.row.paidAllocatedAmount) }}
              </template>
            </el-table-column>
            <el-table-column
              prop="grossProfit"
              label="预计毛利"
              width="145"
              align="right"
              sortable="custom"
            >
              <template #default="scope">
                <span :class="{ 'finance-negative': scope.row.grossProfit < 0 }">{{ formatFinanceAmount(scope.row.grossProfit) }}</span>
              </template>
            </el-table-column>
            <el-table-column
              prop="grossMargin"
              label="毛利率"
              width="115"
              align="right"
              sortable="custom"
            >
              <template #default="scope">
                {{ formatFinancePercentage(scope.row.grossMargin) }}
              </template>
            </el-table-column>
            <el-table-column
              label="操作"
              width="80"
              fixed="right"
            >
              <template #default="scope">
                <el-button
                  link
                  type="primary"
                  @click="router.push({ path: '/projects', query: { entityId: scope.row.projectId } })"
                >
                  项目
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>
        <el-empty
          v-if="projects.length === 0"
          description="暂无有效项目"
        />
        <el-pagination
          v-if="total > pageSize"
          v-model:current-page="page"
          :page-size="pageSize"
          :total="total"
          layout="total, prev, pager, next"
          @current-change="changePage"
        />
      </template>
      <el-alert
        v-else
        title="当前账号无项目查看权限，因此不显示项目经营明细。"
        type="info"
        :closable="false"
      />
    </template>
  </div>
</template>
