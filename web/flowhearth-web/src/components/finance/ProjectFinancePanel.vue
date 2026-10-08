<script setup lang="ts">
import { Plus, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { computed, onMounted, reactive, ref, watch } from 'vue'

import { createReceivablePlan, getProjectFinance } from '../../api/finance'
import { ApiError } from '../../api/problem-details'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import type { PayableType, PaymentStatus, ProjectFinanceOverview, PurchaseOrderStatus, ReceivablePlanItemInput, ReceivableType, ShipmentStatus } from '../../types/finance'
import { payableStatusLabel, payableTypeLabels } from '../../utils/payable-format'
import { calculatePercentagePlan, formatFinanceAmount, formatFinanceDate, formatFinancePercentage, getReceivablePlanValidationError } from '../../utils/finance-format'
import { purchaseStatusLabels } from '../../utils/purchase-format'
import { shipmentStatusLabels, shipmentStatusType } from '../../utils/shipment'

const props = defineProps<{ projectId: number }>()
const auth = useAuthStore()
const canManage = computed(() => auth.canAny([permissions.receivablesManage]))
const loading = ref(false)
const saving = ref(false)
const dialogVisible = ref(false)
const percentageMode = ref(true)
const data = ref<ProjectFinanceOverview>()
type PlanRow = ReceivablePlanItemInput & { percentage: number }
const rows = reactive<PlanRow[]>([])
const typeLabels: Record<ReceivableType, string> = { AdvancePayment: '预付款', ShipmentPayment: '发货款', AcceptancePayment: '验收款', WarrantyRetention: '质保金', ProgressPayment: '进度款', Other: '其它' }
const money = formatFinanceAmount
const planTotal = computed(() => rows.reduce((sum, row) => sum + (row.amount || 0), 0))
const remaining = computed(() => (data.value?.contractAmount ?? 0) - (data.value?.receivableAmount ?? 0) - planTotal.value)

onMounted(load)
watch(() => props.projectId, load)

async function load() { loading.value = true; try { data.value = await getProjectFinance(props.projectId) } catch { ElMessage.error('项目经营财务加载失败。') } finally { loading.value = false } }
function openPlan() { resetPlan(); dialogVisible.value = true }
function resetPlan() { rows.splice(0, rows.length, { receivableType: 'AdvancePayment', title: '预付款', percentage: 30, amount: 0, dueDate: '' }, { receivableType: 'ShipmentPayment', title: '发货款', percentage: 40, amount: 0, dueDate: '' }, { receivableType: 'AcceptancePayment', title: '验收款', percentage: 25, amount: 0, dueDate: '' }, { receivableType: 'WarrantyRetention', title: '质保金', percentage: 5, amount: 0, dueDate: '' }); calculatePercentages() }
function calculatePercentages() {
  if (!data.value || data.value.contractAmount <= 0) return
  const available = data.value.contractAmount - data.value.receivableAmount
  try {
    const amounts = calculatePercentagePlan(available, rows.map(row => row.percentage))
    rows.forEach((row, index) => { row.amount = amounts[index]! })
  } catch {
    ElMessage.warning('各阶段比例合计必须为 100%。')
  }
}
function addRow() { rows.push({ receivableType: 'Other', title: '', percentage: 0, amount: 0, dueDate: '' }) }
async function savePlan() {
  const validationError = getReceivablePlanValidationError(
    (data.value?.contractAmount ?? 0) - (data.value?.receivableAmount ?? 0),
    rows,
  )
  if (validationError) { ElMessage.warning(validationError); return }
  saving.value = true
  try { data.value = await createReceivablePlan(props.projectId, rows.map((row) => ({ receivableType: row.receivableType, title: row.title.trim(), amount: row.amount, dueDate: row.dueDate, remark: row.remark }))); dialogVisible.value = false; ElMessage.success(remaining.value > 0 ? '应收计划已创建，尚有合同金额未安排应收。' : '应收计划已创建。') } catch (error) { showError(error, '应收计划创建失败。') } finally { saving.value = false }
}
function showError(error: unknown, fallback: string) { if (error instanceof ApiError) { if (error.status === 409) { ElMessage.error('项目财务数据已变化，请刷新后重新操作。'); return } const validation = Object.values(error.problem?.errors ?? {}).flat()[0]; ElMessage.error(validation ?? error.problem?.detail ?? fallback); return } ElMessage.error(fallback) }
</script>

<template>
  <div v-loading="loading">
    <div class="detail-tab-toolbar">
      <el-button
        :icon="Refresh"
        @click="load"
      >
        刷新
      </el-button><el-button
        v-if="canManage"
        type="primary"
        :icon="Plus"
        :disabled="!data || data.contractAmount <= data.receivableAmount"
        @click="openPlan"
      >
        新增应收计划
      </el-button>
    </div>
    <template v-if="data">
      <h3 class="finance-section-title finance-section-title-first">
        合同与收入
      </h3>
      <div class="finance-card-grid finance-card-grid-six">
        <div><span>合同金额</span><strong>{{ money(data.contractAmount) }}</strong></div><div><span>累计应收</span><strong>{{ money(data.receivableAmount) }}</strong></div><div><span>已核销实收</span><strong>{{ money(data.receivedAllocatedAmount) }}</strong></div><div><span>未收</span><strong>{{ money(data.receivableOutstandingAmount) }}</strong></div><div><span>逾期应收</span><strong :class="{ 'finance-negative': data.receivableOverdueAmount > 0 }">{{ money(data.receivableOverdueAmount) }}</strong></div><div><span>未到期应收</span><strong>{{ money(data.receivableNotDueAmount) }}</strong></div>
      </div>
      <h3 class="finance-section-title">
        采购与供应商
      </h3>
      <div class="finance-card-grid finance-card-grid-six">
        <div><span>采购金额</span><strong>{{ money(data.purchaseAmount) }}</strong></div><div><span>累计应付</span><strong>{{ money(data.payableAmount) }}</strong></div><div><span>已核销实付</span><strong>{{ money(data.paidAllocatedAmount) }}</strong></div><div><span>未付</span><strong>{{ money(data.payableOutstandingAmount) }}</strong></div><div><span>逾期应付</span><strong :class="{ 'finance-negative': data.payableOverdueAmount > 0 }">{{ money(data.payableOverdueAmount) }}</strong></div><div><span>未到期应付</span><strong>{{ money(data.payableNotDueAmount) }}</strong></div>
      </div>
      <h3 class="finance-section-title">
        经营结果
      </h3>
      <div class="finance-card-grid finance-card-grid-six">
        <div><span>预计项目毛利</span><strong :class="{ 'finance-negative': data.grossProfit < 0 }">{{ money(data.grossProfit) }}</strong></div><div><span>预计毛利率</span><strong :class="{ 'finance-negative': (data.grossMargin ?? 0) < 0 }">{{ formatFinancePercentage(data.grossMargin) }}</strong></div><div><span>项目现金净流入</span><strong :class="{ 'finance-negative': data.cashNetInflow < 0 }">{{ money(data.cashNetInflow) }}</strong></div>
      </div>
      <h3 class="finance-section-title">
        交付
      </h3>
      <div class="finance-card-grid finance-card-grid-six">
        <div><span>采购订单</span><strong>{{ data.purchaseOrderCount }}</strong></div><div><span>采购到货记录</span><strong>{{ data.purchaseReceiptCount }}</strong></div><div><span>正式出货</span><strong>{{ data.shipmentCount }}</strong></div><div><span>已签收出货</span><strong>{{ data.receivedShipmentCount }}</strong></div><div><span>已发设备</span><strong>{{ data.deliveredEquipmentCount }}</strong></div><div><span>最近出货</span><strong>{{ formatFinanceDate(data.lastShipmentDate) }}</strong></div>
      </div>
      <el-alert
        title="当前毛利按合同金额减已登记有效采购订单计算，不含人工、差旅、工资、税费及其他间接成本。项目实收、实付只统计核销到本项目的金额。"
        type="info"
        :closable="false"
      />
      <el-alert
        v-if="data.receivableOverdueAmount > 0"
        :title="`存在逾期应收 ${money(data.receivableOverdueAmount)}`"
        type="warning"
        :closable="false"
        class="finance-alert"
      /><el-alert
        v-if="data.payableOverdueAmount > 0"
        :title="`存在逾期应付 ${money(data.payableOverdueAmount)}`"
        type="warning"
        :closable="false"
        class="finance-alert"
      /><el-alert
        v-if="data.receivableAmount < data.contractAmount"
        :title="`仍有 ${money(data.contractAmount - data.receivableAmount)} 合同金额尚未安排应收。`"
        type="info"
        :closable="false"
        class="finance-alert"
      /><el-alert
        v-for="warning in data.warnings"
        :key="warning.code"
        :title="`${warning.message}（${warning.count} 项）`"
        type="error"
        :closable="false"
        class="finance-alert"
      />
      <h3 class="finance-section-title">
        项目应收计划
      </h3><el-table
        :data="data.receivables"
        stripe
      >
        <el-table-column
          prop="code"
          label="应收编号"
          width="140"
        /><el-table-column
          prop="title"
          label="阶段"
          min-width="150"
        /><el-table-column
          label="类型"
          width="100"
        >
          <template #default="scope">
            {{ typeLabels[scope.row.receivableType as ReceivableType] }}
          </template>
        </el-table-column><el-table-column
          prop="dueDate"
          label="到期日"
          width="110"
        /><el-table-column
          label="应收"
          width="140"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.amount) }}
          </template>
        </el-table-column><el-table-column
          label="已收"
          width="140"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.allocatedAmount) }}
          </template>
        </el-table-column><el-table-column
          label="未收"
          width="140"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.outstandingAmount) }}
          </template>
        </el-table-column>
      </el-table><el-empty
        v-if="data.receivables.length === 0"
        description="尚未创建应收计划"
      />
      <h3 class="finance-section-title">
        项目采购成本
      </h3>
      <el-table
        :data="data.purchases"
        stripe
      >
        <el-table-column
          prop="code"
          label="采购单号"
          width="155"
        /><el-table-column
          prop="supplierName"
          label="供应商"
          min-width="200"
        /><el-table-column
          prop="orderDate"
          label="下单日"
          width="110"
        /><el-table-column
          label="状态"
          width="110"
        >
          <template #default="scope">
            {{ purchaseStatusLabels[scope.row.status as PurchaseOrderStatus] }}
          </template>
        </el-table-column><el-table-column
          label="采购金额"
          width="150"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.totalAmount) }}
          </template>
        </el-table-column><el-table-column
          label="收货进度"
          width="170"
        >
          <template #default="scope">
            <el-progress :percentage="scope.row.receiptProgress" />
          </template>
        </el-table-column>
      </el-table><el-empty
        v-if="data.purchases.length === 0"
        description="尚无已下单采购成本；草稿和取消单不计入毛利"
      />
      <h3 class="finance-section-title">
        项目应付计划
      </h3>
      <el-table
        :data="data.payables"
        stripe
        table-layout="fixed"
      >
        <el-table-column
          prop="code"
          label="应付编号"
          width="145"
        /><el-table-column
          prop="supplierName"
          label="供应商"
          min-width="200"
        /><el-table-column
          prop="purchaseOrderCode"
          label="采购单"
          width="135"
        /><el-table-column
          prop="payableType"
          label="类型"
          width="95"
        >
          <template #default="scope">
            {{ payableTypeLabels[scope.row.payableType as PayableType] }}
          </template>
        </el-table-column><el-table-column
          prop="dueDate"
          label="到期日"
          width="110"
        /><el-table-column
          prop="amount"
          label="应付"
          width="125"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.amount) }}
          </template>
        </el-table-column><el-table-column
          prop="allocatedAmount"
          label="实付"
          width="125"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.allocatedAmount) }}
          </template>
        </el-table-column><el-table-column
          prop="remainingAmount"
          label="未付"
          width="125"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.remainingAmount) }}
          </template>
        </el-table-column><el-table-column
          prop="paymentStatus"
          label="状态"
          width="110"
        >
          <template #default="scope">
            <el-tag :type="scope.row.isOverdue ? 'danger' : scope.row.paymentStatus === 'Paid' ? 'success' : 'warning'">
              {{ payableStatusLabel(scope.row.paymentStatus as PaymentStatus, scope.row.isOverdue) }}
            </el-tag>
          </template>
        </el-table-column>
      </el-table><el-empty
        v-if="data.payables.length === 0"
        description="尚未创建采购应付计划"
      />
      <h3 class="finance-section-title">
        收款核销
      </h3>
      <el-table
        :data="data.receiptAllocations ?? []"
        stripe
      >
        <el-table-column
          prop="receiptCode"
          label="收款编号"
          width="150"
        /><el-table-column
          prop="receiptDate"
          label="收款日期"
          width="115"
        /><el-table-column
          prop="receivableCode"
          label="核销应收"
          width="150"
        /><el-table-column
          label="收款总额"
          width="145"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.receiptAmount) }}
          </template>
        </el-table-column><el-table-column
          label="本项目核销"
          width="145"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.allocatedAmount) }}
          </template>
        </el-table-column>
      </el-table><el-empty
        v-if="(data.receiptAllocations ?? []).length === 0"
        description="尚无收款核销记录"
      />
      <h3 class="finance-section-title">
        付款核销
      </h3>
      <el-table
        :data="data.paymentAllocations ?? []"
        stripe
      >
        <el-table-column
          prop="paymentCode"
          label="付款编号"
          width="150"
        /><el-table-column
          prop="paymentDate"
          label="付款日期"
          width="115"
        /><el-table-column
          prop="payableCode"
          label="核销应付"
          width="150"
        /><el-table-column
          label="付款总额"
          width="145"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.paymentAmount) }}
          </template>
        </el-table-column><el-table-column
          label="本项目核销"
          width="145"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.allocatedAmount) }}
          </template>
        </el-table-column>
      </el-table><el-empty
        v-if="(data.paymentAllocations ?? []).length === 0"
        description="尚无付款核销记录"
      />
      <h3 class="finance-section-title">
        出货记录
      </h3>
      <el-table
        :data="data.shipments ?? []"
        stripe
      >
        <el-table-column
          prop="shipmentCode"
          label="出货编号"
          width="150"
        /><el-table-column
          prop="shipmentDate"
          label="出货日期"
          width="115"
        /><el-table-column
          label="状态"
          width="110"
        >
          <template #default="scope">
            <el-tag :type="shipmentStatusType(scope.row.status as ShipmentStatus)">
              {{ shipmentStatusLabels[scope.row.status as ShipmentStatus] }}
            </el-tag>
          </template>
        </el-table-column><el-table-column
          prop="itemCount"
          label="明细数"
          width="100"
        /><el-table-column
          prop="equipmentCount"
          label="关联设备"
          width="110"
        />
      </el-table><el-empty
        v-if="(data.shipments ?? []).length === 0"
        description="尚无正式出货记录"
      />
    </template>

    <el-dialog
      v-model="dialogVisible"
      draggable
      title="新增项目应收计划"
      width="1000px"
      destroy-on-close
      append-to-body
    >
      <template v-if="data">
        <div class="finance-card-grid">
          <div><span>合同金额</span><strong>{{ money(data.contractAmount) }}</strong></div><div><span>已安排应收</span><strong>{{ money(data.receivableAmount) }}</strong></div><div><span>本次计划</span><strong>{{ money(planTotal) }}</strong></div><div><span>计划后未安排</span><strong :class="{ 'finance-negative': remaining < 0 }">{{ money(remaining) }}</strong></div>
        </div>
        <div class="detail-tab-toolbar">
          <el-switch
            v-model="percentageMode"
            active-text="百分比辅助"
            inactive-text="直接金额"
          /><el-button
            v-if="percentageMode"
            :disabled="data.contractAmount <= 0"
            @click="calculatePercentages"
          >
            按百分比计算
          </el-button><el-button @click="addRow">
            增加阶段
          </el-button>
        </div>
        <el-alert
          v-if="data.contractAmount <= 0"
          title="项目合同金额为 0，请先完善合同金额。"
          type="warning"
          :closable="false"
        />
        <el-table :data="rows">
          <el-table-column
            label="类型"
            width="140"
          >
            <template #default="scope">
              <el-select v-model="scope.row.receivableType">
                <el-option
                  v-for="(label, value) in typeLabels"
                  :key="value"
                  :label="label"
                  :value="value"
                />
              </el-select>
            </template>
          </el-table-column><el-table-column
            label="标题"
            min-width="150"
          >
            <template #default="scope">
              <el-input v-model="scope.row.title" />
            </template>
          </el-table-column><el-table-column
            v-if="percentageMode"
            label="比例"
            width="130"
          >
            <template #default="scope">
              <el-input-number
                v-model="scope.row.percentage"
                :min="0.01"
                :max="100"
                :precision="2"
                @change="calculatePercentages"
              />
            </template>
          </el-table-column><el-table-column
            label="确定金额"
            width="170"
          >
            <template #default="scope">
              <el-input-number
                v-model="scope.row.amount"
                :min="0.01"
                :precision="2"
                :step="1000"
              />
            </template>
          </el-table-column><el-table-column
            label="到期日"
            width="160"
          >
            <template #default="scope">
              <el-date-picker
                v-model="scope.row.dueDate"
                type="date"
                value-format="YYYY-MM-DD"
              />
            </template>
          </el-table-column><el-table-column
            label="备注"
            min-width="150"
          >
            <template #default="scope">
              <el-input v-model="scope.row.remark" />
            </template>
          </el-table-column><el-table-column
            label="操作"
            width="70"
          >
            <template #default="scope">
              <el-button
                link
                type="danger"
                :disabled="rows.length === 1"
                @click="rows.splice(scope.$index, 1)"
              >
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-alert
          v-if="remaining > 0"
          :title="`尚有 ${money(remaining)} 合同金额未安排应收。`"
          type="warning"
          :closable="false"
        /><el-alert
          v-if="remaining < 0"
          title="计划总额超过合同金额，不能提交。"
          type="error"
          :closable="false"
        />
      </template>
      <template #footer>
        <el-button @click="dialogVisible = false">
          取消
        </el-button><el-button
          type="primary"
          :loading="saving"
          :disabled="remaining < 0"
          @click="savePlan"
        >
          创建计划
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>
