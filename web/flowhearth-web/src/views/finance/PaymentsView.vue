<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import dayjs from 'dayjs'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

import * as financeApi from '../../api/finance'
import { ApiError } from '../../api/problem-details'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { PayableSummary, PayableType, PaymentDetails, PaymentInput, PaymentMethod, PaymentSummary, PaymentUpdateInput, SupplierSummary } from '../../types/finance'
import { getPaymentAllocationValidationError, getPaymentValidationError, payableTypeLabels, paymentMethodLabels } from '../../utils/payable-format'

const auth = useAuthStore()
const route = useRoute()
const settings = useSettingsStore()
const canManage = computed(() => auth.canAny([permissions.paymentsManage]))
const loading = ref(false)
const saving = ref(false)
const formVisible = ref(false)
const detailVisible = ref(false)
const allocationVisible = ref(false)
const editing = ref(false)
const items = ref<PaymentSummary[]>([])
const selected = ref<PaymentDetails>()
const suppliers = ref<SupplierSummary[]>([])
const candidates = ref<PayableSummary[]>([])
const allocationAmounts = reactive<Record<number, number>>({})
const search = ref('')
const supplierId = ref<number>()
const dateRange = ref<[string, string]>()
const paymentMethod = ref<PaymentMethod>()
const hasUnallocated = ref<boolean>()
const archive = ref('active')
const page = ref(1)
const pageSize = ref(settings.defaultPageSize)
const total = ref(0)
const form = reactive<PaymentInput & { version?: number }>({ supplierId: 0, paymentDate: dayjs().format('YYYY-MM-DD'), amount: 0, paymentMethod: 'BankTransfer' })
const money = (value: number) => new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY' }).format(value)
const allocationTotal = computed(() => Object.values(allocationAmounts).reduce((sum, value) => sum + (value || 0), 0))
const remainingAfterAllocation = computed(() => (selected.value?.unallocatedAmount ?? 0) - allocationTotal.value)

onMounted(async () => {
  const supplierPage = await financeApi.listSuppliers({ page: 1, pageSize: 100, archive: 'active', sortBy: 'name', sortDescending: false })
  suppliers.value = supplierPage.items
  const requestedSupplierId = Number(route.query.supplierId)
  if (requestedSupplierId) supplierId.value = requestedSupplierId
  await load()
  const id = Number(route.query.entityId)
  if (id) await openDetails(id)
})
watch(() => route.query.entityId, async (value) => { const id = Number(value); if (id && selected.value?.id !== id) await openDetails(id) })

async function load() {
  loading.value = true
  try {
    const result = await financeApi.listPayments({ page: page.value, pageSize: pageSize.value, search: search.value.trim() || undefined, supplierId: supplierId.value, dateFrom: dateRange.value?.[0], dateTo: dateRange.value?.[1], paymentMethod: paymentMethod.value, hasUnallocated: hasUnallocated.value, archive: archive.value, sortBy: 'paymentDate', sortDescending: true })
    items.value = result.items
    total.value = result.total
  } catch (error) { showError(error, '付款列表加载失败。') } finally { loading.value = false }
}

async function openDetails(id: number) {
  loading.value = true
  try { selected.value = await financeApi.getPayment(id); detailVisible.value = true } catch (error) { showError(error, '付款详情加载失败。') } finally { loading.value = false }
}

function openCreate() { editing.value = false; Object.assign(form, { supplierId: supplierId.value ?? 0, paymentDate: dayjs().format('YYYY-MM-DD'), amount: 0, paymentMethod: 'BankTransfer', payeeName: undefined, bankReference: undefined, remark: undefined, version: undefined }); formVisible.value = true }
function openEdit() { if (!selected.value) return; editing.value = true; Object.assign(form, selected.value); formVisible.value = true }

async function save() {
  const validationError = getPaymentValidationError(form)
  if (validationError) { ElMessage.warning(validationError); return }
  saving.value = true
  try {
    const input = { supplierId: form.supplierId, paymentDate: form.paymentDate, amount: form.amount, paymentMethod: form.paymentMethod, payeeName: form.payeeName?.trim() || undefined, bankReference: form.bankReference?.trim() || undefined, remark: form.remark }
    const saved = editing.value && selected.value ? await financeApi.updatePayment(selected.value.id, { ...input, version: form.version! } satisfies PaymentUpdateInput) : await financeApi.createPayment(input)
    selected.value = saved
    formVisible.value = false
    detailVisible.value = true
    await load()
    ElMessage.success(editing.value ? '付款记录已更新。' : '付款记录已创建，可立即核销应付。')
  } catch (error) { showError(error, '付款记录保存失败。') } finally { saving.value = false }
}

async function toggleArchive() {
  if (!selected.value) return
  const archived = !selected.value.isArchived
  try {
    await ElMessageBox.confirm(archived ? '确认归档该付款？存在有效核销时系统会拒绝，请先取消核销。' : '确认恢复该付款？', archived ? '归档付款' : '恢复付款', { type: archived ? 'warning' : 'info' })
    selected.value = await financeApi.setPaymentArchived(selected.value.id, archived, selected.value.version)
    await load()
    ElMessage.success(archived ? '付款已归档。' : '付款已恢复。')
  } catch (error) { if (error !== 'cancel') showError(error, archived ? '归档失败。' : '恢复失败。') }
}

async function openAllocation() {
  if (!selected.value || selected.value.unallocatedAmount <= 0) return
  loading.value = true
  try {
    const result = await financeApi.listPayables({ page: 1, pageSize: 100, supplierId: selected.value.supplierId, archive: 'active', sortBy: 'dueDate', sortDescending: false })
    candidates.value = result.items.filter(item => item.remainingAmount > 0)
    for (const key of Object.keys(allocationAmounts)) delete allocationAmounts[Number(key)]
    allocationVisible.value = true
  } catch (error) { showError(error, '待核销应付加载失败。') } finally { loading.value = false }
}

async function saveAllocation() {
  if (!selected.value) return
  const allocations = candidates.value.map(item => ({ payableId: item.id, amount: allocationAmounts[item.id] || 0 })).filter(item => item.amount > 0)
  const validationError = getPaymentAllocationValidationError(
    selected.value.unallocatedAmount,
    candidates.value.map(item => ({ amount: allocationAmounts[item.id] || 0, payableRemaining: item.remainingAmount })),
  )
  if (validationError) { ElMessage.warning(validationError); return }
  saving.value = true
  try {
    selected.value = await financeApi.allocatePayment(selected.value.id, selected.value.version, allocations)
    allocationVisible.value = false
    await load()
    ElMessage.success('付款核销已完成。')
  } catch (error) { showError(error, '付款核销失败。') } finally { saving.value = false }
}

async function cancelAllocation(id: number, version: number) {
  if (!selected.value) return
  try {
    await ElMessageBox.confirm('取消后付款和应付余额会重新计算，确认继续？', '取消核销', { type: 'warning' })
    selected.value = await financeApi.cancelPaymentAllocation(selected.value.id, id, version)
    await load()
    ElMessage.success('核销已取消。')
  } catch (error) { if (error !== 'cancel') showError(error, '取消核销失败。') }
}

function showError(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    if (error.status === 409) { ElMessage.error('付款或应付余额已被其他用户修改，请刷新后重新操作。'); return }
    ElMessage.error(Object.values(error.problem?.errors ?? {}).flat()[0] ?? error.problem?.detail ?? fallback)
    return
  }
  ElMessage.error(fallback)
}
</script>

<template>
  <section class="module-page">
    <div class="finance-filter-grid">
      <el-input
        v-model="search"
        clearable
        placeholder="付款编号、供应商、银行参考号"
        @keyup.enter="page = 1; load()"
      >
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>
      <el-select
        v-model="supplierId"
        clearable
        filterable
        placeholder="供应商"
        @change="page = 1; load()"
      >
        <el-option
          v-for="item in suppliers"
          :key="item.id"
          :label="`${item.code} · ${item.name}`"
          :value="item.id"
        />
      </el-select>
      <el-date-picker
        v-model="dateRange"
        type="daterange"
        value-format="YYYY-MM-DD"
        range-separator="至"
        start-placeholder="付款开始"
        end-placeholder="付款结束"
        @change="page = 1; load()"
      />
      <el-select
        v-model="paymentMethod"
        clearable
        placeholder="付款方式"
        @change="page = 1; load()"
      >
        <el-option
          v-for="(label, value) in paymentMethodLabels"
          :key="value"
          :label="label"
          :value="value"
        />
      </el-select>
      <el-select
        v-model="hasUnallocated"
        clearable
        placeholder="核销余额"
        @change="page = 1; load()"
      >
        <el-option
          label="有未核销余额"
          :value="true"
        /><el-option
          label="已全部核销"
          :value="false"
        />
      </el-select>
      <el-select
        v-model="archive"
        @change="page = 1; load()"
      >
        <el-option
          label="在用"
          value="active"
        /><el-option
          label="已归档"
          value="archived"
        /><el-option
          label="全部"
          value="all"
        />
      </el-select>
      <el-button
        :icon="Refresh"
        @click="load"
      >
        刷新
      </el-button><el-button
        v-if="canManage"
        type="primary"
        :icon="Plus"
        @click="openCreate"
      >
        新增付款
      </el-button>
    </div>

    <div class="finance-table-scroll payment-table-scroll">
      <el-table
        v-loading="loading"
        class="flowhearth-data-table"
        :data="items"
        border
        stripe
        table-layout="fixed"
        @row-dblclick="row => openDetails(row.id)"
      >
        <el-table-column
          prop="code"
          label="付款编号"
          width="150"
          fixed="left"
        >
          <template #default="scope">
            <el-link
              type="primary"
              @click="openDetails(scope.row.id)"
            >
              {{ scope.row.code }}
            </el-link>
          </template>
        </el-table-column>
        <el-table-column
          prop="supplierName"
          label="供应商"
          min-width="240"
          show-overflow-tooltip
        />
        <el-table-column
          prop="paymentDate"
          label="付款日期"
          width="120"
        />
        <el-table-column
          prop="amount"
          label="付款金额"
          width="135"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.amount) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="allocatedAmount"
          label="已核销"
          width="125"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.allocatedAmount) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="unallocatedAmount"
          label="未核销"
          width="125"
          align="right"
        >
          <template #default="scope">
            <span :class="{ 'danger-text': scope.row.unallocatedAmount > 0 }">{{ money(scope.row.unallocatedAmount) }}</span>
          </template>
        </el-table-column>
        <el-table-column
          prop="paymentMethod"
          label="付款方式"
          width="110"
        >
          <template #default="scope">
            {{ paymentMethodLabels[scope.row.paymentMethod as PaymentMethod] }}
          </template>
        </el-table-column>
        <el-table-column
          prop="payeeName"
          label="收款方"
          min-width="180"
          show-overflow-tooltip
        />
        <el-table-column
          prop="bankReference"
          label="银行流水/参考号"
          min-width="180"
          show-overflow-tooltip
        />
      </el-table>
    </div>
    <el-pagination
      v-model:current-page="page"
      v-model:page-size="pageSize"
      :total="total"
      layout="total, sizes, prev, pager, next"
      @current-change="load"
      @size-change="page = 1; load()"
    />

    <el-drawer
      v-model="detailVisible"
      title="付款详情"
      size="min(940px, 92vw)"
    >
      <template v-if="selected">
        <div class="drawer-actions">
          <el-button
            v-if="canManage"
            @click="openEdit"
          >
            编辑
          </el-button><el-button
            v-if="canManage"
            :type="selected.isArchived ? 'primary' : 'danger'"
            plain
            @click="toggleArchive"
          >
            {{ selected.isArchived ? '恢复' : '归档' }}
          </el-button><el-button
            v-if="canManage && !selected.isArchived && selected.unallocatedAmount > 0"
            type="primary"
            @click="openAllocation"
          >
            核销应付
          </el-button>
        </div>
        <el-descriptions
          :column="2"
          border
        >
          <el-descriptions-item label="付款编号">
            {{ selected.code }}
          </el-descriptions-item><el-descriptions-item label="供应商">
            {{ selected.supplierCode }} · {{ selected.supplierName }}
          </el-descriptions-item>
          <el-descriptions-item label="付款日期">
            {{ selected.paymentDate }}
          </el-descriptions-item><el-descriptions-item label="付款方式">
            {{ paymentMethodLabels[selected.paymentMethod] }}
          </el-descriptions-item>
          <el-descriptions-item label="付款金额">
            {{ money(selected.amount) }}
          </el-descriptions-item><el-descriptions-item label="已核销金额">
            {{ money(selected.allocatedAmount) }}
          </el-descriptions-item>
          <el-descriptions-item label="未核销金额">
            {{ money(selected.unallocatedAmount) }}
          </el-descriptions-item><el-descriptions-item label="收款方">
            {{ selected.payeeName || '—' }}
          </el-descriptions-item>
          <el-descriptions-item label="银行参考号">
            {{ selected.bankReference || '—' }}
          </el-descriptions-item><el-descriptions-item label="备注">
            {{ selected.remark || '—' }}
          </el-descriptions-item>
        </el-descriptions>
        <h3>核销到应付</h3>
        <el-table
          :data="selected.allocations"
          table-layout="fixed"
        >
          <el-table-column
            prop="payableCode"
            label="应付编号"
            width="150"
          /><el-table-column
            prop="projectName"
            label="项目"
            min-width="170"
          >
            <template #default="scope">
              {{ scope.row.projectCode }} · {{ scope.row.projectName }}
            </template>
          </el-table-column><el-table-column
            prop="purchaseOrderCode"
            label="采购单"
            width="140"
          /><el-table-column
            prop="payableType"
            label="类型"
            width="100"
          >
            <template #default="scope">
              {{ payableTypeLabels[scope.row.payableType as PayableType] }}
            </template>
          </el-table-column><el-table-column
            prop="payableAmount"
            label="应付总额"
            width="125"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.payableAmount) }}
            </template>
          </el-table-column><el-table-column
            prop="allocatedAmount"
            label="本次核销"
            width="125"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.allocatedAmount) }}
            </template>
          </el-table-column><el-table-column
            label="状态/操作"
            width="120"
            fixed="right"
          >
            <template #default="scope">
              <el-tag
                v-if="scope.row.isCancelled"
                type="info"
              >
                已取消
              </el-tag><el-button
                v-else-if="canManage"
                link
                type="danger"
                @click="cancelAllocation(scope.row.id, scope.row.version)"
              >
                取消核销
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </template>
    </el-drawer>

    <el-dialog
      v-model="formVisible"
      draggable
      :title="editing ? '编辑付款' : '新增付款'"
      width="min(660px, 92vw)"
      destroy-on-close
    >
      <el-form label-width="110px">
        <el-form-item
          label="供应商"
          required
        >
          <el-select
            v-model="form.supplierId"
            filterable
            style="width: 100%"
          >
            <el-option
              v-for="item in suppliers"
              :key="item.id"
              :label="`${item.code} · ${item.name}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          label="付款日期"
          required
        >
          <el-date-picker
            v-model="form.paymentDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item
          label="付款金额"
          required
        >
          <el-input-number
            v-model="form.amount"
            :min="0.01"
            :precision="2"
            :step="1000"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item
          label="付款方式"
          required
        >
          <el-select
            v-model="form.paymentMethod"
            style="width: 100%"
          >
            <el-option
              v-for="(label, value) in paymentMethodLabels"
              :key="value"
              :label="label"
              :value="value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="收款方">
          <el-input
            v-model="form.payeeName"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="银行参考号">
          <el-input
            v-model="form.bankReference"
            maxlength="100"
          />
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="form.remark"
            type="textarea"
            :rows="3"
            maxlength="1000"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="formVisible = false">
          取消
        </el-button><el-button
          type="primary"
          :loading="saving"
          @click="save"
        >
          保存
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="allocationVisible"
      draggable
      title="核销应付"
      width="min(1120px, 96vw)"
      destroy-on-close
    >
      <template v-if="selected">
        <el-alert
          type="info"
          :closable="false"
        >
          <template #title>
            {{ selected.supplierName }} · 本笔付款 {{ money(selected.amount) }} · 已核销 {{ money(selected.allocatedAmount) }} · 可核销 {{ money(selected.unallocatedAmount) }}
          </template>
        </el-alert>
        <el-table
          :data="candidates"
          max-height="480"
          table-layout="fixed"
        >
          <el-table-column
            prop="code"
            label="应付编号"
            width="145"
          /><el-table-column
            prop="projectName"
            label="项目"
            min-width="160"
          >
            <template #default="scope">
              {{ scope.row.projectCode }} · {{ scope.row.projectName }}
            </template>
          </el-table-column><el-table-column
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
            width="115"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.amount) }}
            </template>
          </el-table-column><el-table-column
            prop="allocatedAmount"
            label="已付"
            width="115"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.allocatedAmount) }}
            </template>
          </el-table-column><el-table-column
            prop="remainingAmount"
            label="未付"
            width="115"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.remainingAmount) }}
            </template>
          </el-table-column><el-table-column
            label="本次核销"
            width="170"
            fixed="right"
          >
            <template #default="scope">
              <el-input-number
                v-model="allocationAmounts[scope.row.id]"
                :min="0"
                :max="scope.row.remainingAmount"
                :precision="2"
                controls-position="right"
              />
            </template>
          </el-table-column>
        </el-table>
        <div class="allocation-summary">
          <span>本次核销合计：<strong>{{ money(allocationTotal) }}</strong></span><span>付款剩余可用余额：<strong :class="{ 'danger-text': remainingAfterAllocation < 0 }">{{ money(remainingAfterAllocation) }}</strong></span>
        </div>
      </template>
      <template #footer>
        <el-button @click="allocationVisible = false">
          取消
        </el-button><el-button
          type="primary"
          :loading="saving"
          :disabled="allocationTotal <= 0 || remainingAfterAllocation < 0"
          @click="saveAllocation"
        >
          确认核销
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.drawer-actions { display: flex; justify-content: flex-end; gap: 8px; margin-bottom: 16px; }
.finance-table-scroll { width: 100%; overflow-x: auto; }
.payment-table-scroll :deep(.el-table) { min-width: 1320px; }
.allocation-summary { display: flex; justify-content: flex-end; gap: 32px; margin-top: 16px; font-size: 15px; }
.danger-text { color: var(--el-color-danger); }
h3 { margin-top: 24px; }
@media (max-width: 720px) { .allocation-summary { flex-direction: column; gap: 4px; } }
</style>
