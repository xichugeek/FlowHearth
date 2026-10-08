<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import dayjs from 'dayjs'
import { computed, onMounted, reactive, ref } from 'vue'

import * as customerApi from '../../api/customers'
import * as financeApi from '../../api/finance'
import { ApiError } from '../../api/problem-details'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { CustomerSummary } from '../../types/customers'
import type { PaymentMethod, ReceiptDetails, ReceiptInput, ReceiptSummary, ReceiptUpdateInput, ReceivableSummary, ReceivableType } from '../../types/finance'

const auth = useAuthStore()
const settings = useSettingsStore()
const canManage = computed(() => auth.canAny([permissions.receiptsManage]))
const loading = ref(false)
const saving = ref(false)
const formVisible = ref(false)
const detailVisible = ref(false)
const allocationVisible = ref(false)
const editing = ref(false)
const items = ref<ReceiptSummary[]>([])
const selected = ref<ReceiptDetails>()
const customers = ref<CustomerSummary[]>([])
const candidates = ref<ReceivableSummary[]>([])
const allocationAmounts = reactive<Record<number, number>>({})
const search = ref('')
const customerId = ref<number>()
const dateRange = ref<[string, string]>()
const hasUnallocated = ref<boolean>()
const archive = ref('active')
const page = ref(1)
const pageSize = ref(settings.defaultPageSize)
const total = ref(0)
const form = reactive<ReceiptInput & { version?: number }>({ customerId: 0, receiptDate: dayjs().format('YYYY-MM-DD'), amount: 0, paymentMethod: 'BankTransfer' })
const methodLabels: Record<PaymentMethod, string> = { BankTransfer: '银行转账', Cash: '现金', Cheque: '支票', Other: '其它' }
const typeLabels: Record<ReceivableType, string> = { AdvancePayment: '预付款', ShipmentPayment: '发货款', AcceptancePayment: '验收款', WarrantyRetention: '质保金', ProgressPayment: '进度款', Other: '其它' }
const money = (value: number) => new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY' }).format(value)
const allocationTotal = computed(() => Object.values(allocationAmounts).reduce((sum, value) => sum + (value || 0), 0))
const remainingAfterAllocation = computed(() => (selected.value?.unallocatedAmount ?? 0) - allocationTotal.value)

onMounted(async () => {
  const customerPage = await customerApi.listCustomers({ page: 1, pageSize: 100, archive: 'active', sortBy: 'name', sortDescending: false })
  customers.value = customerPage.items
  await load()
})

async function load() {
  loading.value = true
  try {
    const result = await financeApi.listReceipts({ page: page.value, pageSize: pageSize.value, search: search.value.trim() || undefined, customerId: customerId.value, dateFrom: dateRange.value?.[0], dateTo: dateRange.value?.[1], hasUnallocated: hasUnallocated.value, archive: archive.value, sortBy: 'receiptDate', sortDescending: true })
    items.value = result.items
    total.value = result.total
  } catch (error) { showError(error, '收款列表加载失败。') } finally { loading.value = false }
}

async function openDetails(id: number) {
  loading.value = true
  try { selected.value = await financeApi.getReceipt(id); detailVisible.value = true } catch (error) { showError(error, '收款详情加载失败。') } finally { loading.value = false }
}

function openCreate() { editing.value = false; Object.assign(form, { customerId: customerId.value ?? 0, receiptDate: dayjs().format('YYYY-MM-DD'), amount: 0, paymentMethod: 'BankTransfer', bankReference: undefined, payerName: undefined, remark: undefined, version: undefined }); formVisible.value = true }
function openEdit() { if (!selected.value) return; editing.value = true; Object.assign(form, selected.value); formVisible.value = true }

async function save() {
  if (!form.customerId || form.amount <= 0 || !form.receiptDate) { ElMessage.warning('请完整填写客户、日期和金额。'); return }
  saving.value = true
  try {
    const saved = editing.value && selected.value
      ? await financeApi.updateReceipt(selected.value.id, { receiptDate: form.receiptDate, amount: form.amount, paymentMethod: form.paymentMethod, bankReference: form.bankReference, payerName: form.payerName, remark: form.remark, version: form.version! } satisfies ReceiptUpdateInput)
      : await financeApi.createReceipt(form)
    selected.value = saved
    formVisible.value = false
    detailVisible.value = true
    await load()
    ElMessage.success(editing.value ? '收款记录已更新。' : '收款记录已创建。')
  } catch (error) { showError(error, '收款记录保存失败。') } finally { saving.value = false }
}

async function toggleArchive() {
  if (!selected.value) return
  const archived = !selected.value.isArchived
  try {
    await ElMessageBox.confirm(archived ? '确认归档该收款？存在有效核销时系统会拒绝。' : '确认恢复该收款？', archived ? '归档收款' : '恢复收款', { type: archived ? 'warning' : 'info' })
    selected.value = await financeApi.setReceiptArchived(selected.value.id, archived, selected.value.version)
    await load()
    ElMessage.success(archived ? '收款已归档。' : '收款已恢复。')
  } catch (error) { if (error !== 'cancel') showError(error, archived ? '归档失败。' : '恢复失败。') }
}

async function openAllocation() {
  if (!selected.value || selected.value.unallocatedAmount <= 0) return
  loading.value = true
  try {
    const result = await financeApi.listReceivables({ page: 1, pageSize: 100, customerId: selected.value.customerId, archive: 'active', sortBy: 'dueDate', sortDescending: false })
    candidates.value = result.items.filter(item => item.outstandingAmount > 0)
    for (const key of Object.keys(allocationAmounts)) delete allocationAmounts[Number(key)]
    allocationVisible.value = true
  } catch (error) { showError(error, '待核销应收加载失败。') } finally { loading.value = false }
}

async function saveAllocation() {
  if (!selected.value) return
  const allocations = candidates.value.map(item => ({ receivableId: item.id, amount: allocationAmounts[item.id] || 0 })).filter(item => item.amount > 0)
  if (!allocations.length) { ElMessage.warning('请至少填写一笔核销金额。'); return }
  if (remainingAfterAllocation.value < 0) { ElMessage.warning('本次核销总额不能超过可用收款余额。'); return }
  saving.value = true
  try {
    selected.value = await financeApi.allocateReceipt(selected.value.id, selected.value.version, allocations)
    allocationVisible.value = false
    await load()
    ElMessage.success('核销已完成。')
  } catch (error) { showError(error, '核销失败。') } finally { saving.value = false }
}

async function cancelAllocation(id: number, version: number) {
  if (!selected.value) return
  try {
    await ElMessageBox.confirm('取消后相关应收余额和状态将重新计算，确认继续？', '取消核销', { type: 'warning' })
    selected.value = await financeApi.cancelReceiptAllocation(selected.value.id, id, version)
    await load()
    ElMessage.success('核销已取消。')
  } catch (error) { if (error !== 'cancel') showError(error, '取消核销失败。') }
}

function showError(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    if (error.status === 409) { ElMessage.error('收款或应收余额已被其他用户修改，请刷新后重新操作。'); return }
    const validation = Object.values(error.problem?.errors ?? {}).flat()[0]
    ElMessage.error(validation ?? error.problem?.detail ?? fallback)
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
        placeholder="编号、流水、付款方、客户"
        @keyup.enter="page = 1; load()"
      >
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>
      <el-select
        v-model="customerId"
        clearable
        filterable
        placeholder="客户"
        @change="page = 1; load()"
      >
        <el-option
          v-for="item in customers"
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
        start-placeholder="收款开始"
        end-placeholder="收款结束"
        @change="page = 1; load()"
      />
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
        新增收款
      </el-button>
    </div>
    <el-table
      v-loading="loading"
      class="flowhearth-data-table"
      :data="items"
      border
      stripe
      row-class-name="customer-table-row"
      @row-click="row => openDetails(row.id)"
    >
      <el-table-column
        prop="code"
        label="收款编号"
        width="145"
      /><el-table-column
        prop="customerName"
        label="客户"
        min-width="240"
      /><el-table-column
        prop="receiptDate"
        label="收款日期"
        width="115"
      />
      <el-table-column
        label="收款金额"
        width="145"
        align="right"
      >
        <template #default="scope">
          {{ money(scope.row.amount) }}
        </template>
      </el-table-column><el-table-column
        label="已核销"
        width="140"
        align="right"
      >
        <template #default="scope">
          {{ money(scope.row.allocatedAmount) }}
        </template>
      </el-table-column><el-table-column
        label="未核销"
        width="140"
        align="right"
      >
        <template #default="scope">
          <span :class="{ 'finance-negative': scope.row.unallocatedAmount > 0 }">{{ money(scope.row.unallocatedAmount) }}</span>
        </template>
      </el-table-column>
      <el-table-column
        label="方式"
        width="110"
      >
        <template #default="scope">
          {{ methodLabels[scope.row.paymentMethod as PaymentMethod] }}
        </template>
      </el-table-column><el-table-column
        prop="payerName"
        label="付款方"
        min-width="160"
      /><el-table-column
        prop="bankReference"
        label="银行流水/参考号"
        min-width="180"
      /><el-table-column
        label="操作"
        width="80"
        fixed="right"
      >
        <template #default="scope">
          <el-button
            link
            type="primary"
            @click.stop="openDetails(scope.row.id)"
          >
            查看
          </el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-pagination
      v-model:current-page="page"
      v-model:page-size="pageSize"
      layout="total, sizes, prev, pager, next"
      :total="total"
      :page-sizes="[10,20,50,100]"
      @change="load"
    />

    <el-drawer
      v-model="detailVisible"
      size="min(920px, 96vw)"
      destroy-on-close
    >
      <template #header>
        <strong v-if="selected">{{ selected.code }} · {{ selected.customerName }}</strong>
      </template>
      <template v-if="selected">
        <div
          v-if="canManage"
          class="detail-action-row"
        >
          <el-button
            :disabled="selected.isArchived"
            @click="openEdit"
          >
            编辑
          </el-button><el-button
            type="primary"
            :disabled="selected.isArchived || selected.unallocatedAmount <= 0"
            @click="openAllocation"
          >
            核销应收
          </el-button><el-button
            :type="selected.isArchived ? 'primary' : 'danger'"
            plain
            @click="toggleArchive"
          >
            {{ selected.isArchived ? '恢复' : '归档' }}
          </el-button>
        </div>
        <div class="finance-card-grid">
          <div><span>收款金额</span><strong>{{ money(selected.amount) }}</strong></div><div><span>已核销</span><strong>{{ money(selected.allocatedAmount) }}</strong></div><div><span>未核销</span><strong>{{ money(selected.unallocatedAmount) }}</strong></div>
        </div>
        <el-descriptions
          :column="2"
          border
        >
          <el-descriptions-item label="客户">
            {{ selected.customerCode }} · {{ selected.customerName }}
          </el-descriptions-item><el-descriptions-item label="收款日期">
            {{ selected.receiptDate }}
          </el-descriptions-item><el-descriptions-item label="付款方式">
            {{ methodLabels[selected.paymentMethod] }}
          </el-descriptions-item><el-descriptions-item label="付款方">
            {{ selected.payerName || '—' }}
          </el-descriptions-item><el-descriptions-item label="银行流水/参考号">
            {{ selected.bankReference || '—' }}
          </el-descriptions-item><el-descriptions-item label="备注">
            {{ selected.remark || '—' }}
          </el-descriptions-item>
        </el-descriptions>
        <h3 class="finance-section-title">
          核销明细
        </h3>
        <el-table
          :data="selected.allocations"
          stripe
        >
          <el-table-column
            prop="receivableCode"
            label="应收编号"
            width="145"
          /><el-table-column
            label="项目"
            min-width="190"
          >
            <template #default="scope">
              {{ scope.row.projectCode }} · {{ scope.row.projectName }}
            </template>
          </el-table-column><el-table-column
            label="类型"
            width="100"
          >
            <template #default="scope">
              {{ typeLabels[scope.row.receivableType as ReceivableType] }}
            </template>
          </el-table-column><el-table-column
            label="应收总额"
            width="140"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.receivableAmount) }}
            </template>
          </el-table-column><el-table-column
            label="本次核销"
            width="140"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.allocatedAmount) }}
            </template>
          </el-table-column><el-table-column
            label="状态"
            width="90"
          >
            <template #default="scope">
              <el-tag :type="scope.row.isCancelled ? 'info' : 'success'">
                {{ scope.row.isCancelled ? '已取消' : '有效' }}
              </el-tag>
            </template>
          </el-table-column><el-table-column
            v-if="canManage"
            label="操作"
            width="90"
          >
            <template #default="scope">
              <el-button
                v-if="!scope.row.isCancelled"
                link
                type="danger"
                @click="cancelAllocation(scope.row.id, scope.row.version)"
              >
                取消核销
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-empty
          v-if="selected.allocations.length === 0"
          description="尚未核销应收"
        />
      </template>
    </el-drawer>

    <el-dialog
      v-model="allocationVisible"
      draggable
      title="核销应收"
      width="980px"
      destroy-on-close
    >
      <template v-if="selected">
        <div class="finance-card-grid">
          <div><span>客户</span><strong>{{ selected.customerName }}</strong></div><div><span>本笔收款</span><strong>{{ money(selected.amount) }}</strong></div><div><span>已核销</span><strong>{{ money(selected.allocatedAmount) }}</strong></div><div><span>可用余额</span><strong>{{ money(selected.unallocatedAmount) }}</strong></div>
        </div>
        <el-table
          :data="candidates"
          max-height="430"
        >
          <el-table-column
            prop="code"
            label="应收编号"
            width="140"
          /><el-table-column
            label="项目"
            min-width="180"
          >
            <template #default="scope">
              {{ scope.row.projectCode }} · {{ scope.row.projectName }}
            </template>
          </el-table-column><el-table-column
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
            label="应收金额"
            width="130"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.amount) }}
            </template>
          </el-table-column><el-table-column
            label="已收"
            width="125"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.allocatedAmount) }}
            </template>
          </el-table-column><el-table-column
            label="未收"
            width="125"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.outstandingAmount) }}
            </template>
          </el-table-column><el-table-column
            label="本次核销"
            width="160"
          >
            <template #default="scope">
              <el-input-number
                v-model="allocationAmounts[scope.row.id]"
                :min="0"
                :max="Math.min(scope.row.outstandingAmount, selected!.unallocatedAmount)"
                :precision="2"
                :step="1000"
                controls-position="right"
              />
            </template>
          </el-table-column>
        </el-table>
        <el-empty
          v-if="candidates.length === 0"
          description="该客户没有未收清的应收"
        />
        <div class="finance-allocation-summary">
          <span>本次核销：<strong>{{ money(allocationTotal) }}</strong></span><span>核销后剩余：<strong :class="{ 'finance-negative': remainingAfterAllocation < 0 }">{{ money(remainingAfterAllocation) }}</strong></span>
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

    <el-dialog
      v-model="formVisible"
      draggable
      :title="editing ? '编辑收款' : '新增收款'"
      width="640px"
      destroy-on-close
    >
      <el-form label-position="top">
        <el-form-item
          label="客户"
          required
        >
          <el-select
            v-model="form.customerId"
            filterable
            :disabled="editing"
          >
            <el-option
              v-for="item in customers"
              :key="item.id"
              :label="`${item.code} · ${item.name}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item><el-row :gutter="16">
          <el-col :span="12">
            <el-form-item
              label="收款日期"
              required
            >
              <el-date-picker
                v-model="form.receiptDate"
                type="date"
                value-format="YYYY-MM-DD"
              />
            </el-form-item>
          </el-col><el-col :span="12">
            <el-form-item
              label="收款金额"
              required
            >
              <el-input-number
                v-model="form.amount"
                :min="0.01"
                :precision="2"
                :step="1000"
              />
            </el-form-item>
          </el-col>
        </el-row><el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="付款方式">
              <el-select v-model="form.paymentMethod">
                <el-option
                  v-for="(label, value) in methodLabels"
                  :key="value"
                  :label="label"
                  :value="value"
                />
              </el-select>
            </el-form-item>
          </el-col><el-col :span="12">
            <el-form-item label="付款方">
              <el-input v-model="form.payerName" />
            </el-form-item>
          </el-col>
        </el-row><el-form-item label="银行流水/参考号">
          <el-input
            v-model="form.bankReference"
            maxlength="100"
          />
        </el-form-item><el-alert
          v-if="editing && selected?.allocatedAmount"
          :title="`已核销 ${money(selected.allocatedAmount)}；新金额不得低于已核销金额。`"
          type="warning"
          :closable="false"
        /><el-form-item label="备注">
          <el-input
            v-model="form.remark"
            type="textarea"
            :rows="3"
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
  </section>
</template>
