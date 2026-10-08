<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import * as financeApi from '../../api/finance'
import { ApiError } from '../../api/problem-details'
import * as projectApi from '../../api/projects'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { PayableDetails, PayableInput, PayableSummary, PayableType, PayableUpdateInput, PaymentMethod, PaymentStatus, PurchaseOrderSummary, SupplierSummary } from '../../types/finance'
import type { ProjectSummary } from '../../types/projects'
import { getPayableValidationError, payableStatusLabel, payableTypeLabels, paymentMethodLabels, paymentStatusLabels } from '../../utils/payable-format'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const settings = useSettingsStore()
const canManage = computed(() => auth.canAny([permissions.payablesManage]))
const canManagePayments = computed(() => auth.canAny([permissions.paymentsManage]))
const loading = ref(false)
const saving = ref(false)
const formVisible = ref(false)
const detailVisible = ref(false)
const editing = ref(false)
const items = ref<PayableSummary[]>([])
const selected = ref<PayableDetails>()
const suppliers = ref<SupplierSummary[]>([])
const projects = ref<ProjectSummary[]>([])
const purchaseOrders = ref<PurchaseOrderSummary[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(settings.defaultPageSize)
const search = ref('')
const supplierId = ref<number>()
const projectId = ref<number>()
const purchaseOrderId = ref<number>()
const payableType = ref<PayableType>()
const paymentStatus = ref<PaymentStatus>()
const overdueOnly = ref<boolean>()
const dueRange = ref<[string, string]>()
const archive = ref('active')
const sortBy = ref('updatedAt')
const sortDescending = ref(true)
const form = reactive<PayableInput & { version?: number }>({ supplierId: 0, projectId: 0, payableType: 'PurchasePayment', title: '', amount: 0, dueDate: '' })

const money = (value: number) => new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY' }).format(value)
const availablePurchaseOrders = computed(() => purchaseOrders.value.filter(item => (!form.supplierId || item.supplierId === form.supplierId) && (!form.projectId || item.projectId === form.projectId)))

onMounted(async () => { await Promise.all([loadLookups(), load()]); const id = Number(route.query.entityId); if (id) await openDetails(id) })
watch(() => route.query.entityId, async (value) => { const id = Number(value); if (id && selected.value?.id !== id) await openDetails(id) })

async function loadLookups() {
  const [supplierPage, projectPage, purchasePage] = await Promise.all([
    financeApi.listSuppliers({ page: 1, pageSize: 100, archive: 'active', sortBy: 'name', sortDescending: false }),
    projectApi.listProjects({ page: 1, pageSize: 100, archive: 'active', sortBy: 'updatedAt', sortDescending: true }),
    financeApi.listPurchaseOrders({ page: 1, pageSize: 100, archive: 'active', sortBy: 'orderDate', sortDescending: true }),
  ])
  suppliers.value = supplierPage.items
  projects.value = projectPage.items
  purchaseOrders.value = purchasePage.items
}

async function load() {
  loading.value = true
  try {
    const result = await financeApi.listPayables({ page: page.value, pageSize: pageSize.value, search: search.value.trim() || undefined, supplierId: supplierId.value, projectId: projectId.value, purchaseOrderId: purchaseOrderId.value, payableType: payableType.value, paymentStatus: paymentStatus.value, overdueOnly: overdueOnly.value, dueFrom: dueRange.value?.[0], dueTo: dueRange.value?.[1], archive: archive.value, sortBy: sortBy.value, sortDescending: sortDescending.value })
    items.value = result.items
    total.value = result.total
  } catch (error) { showError(error, '应付列表加载失败。') } finally { loading.value = false }
}

async function openDetails(id: number) {
  loading.value = true
  try { selected.value = await financeApi.getPayable(id); detailVisible.value = true } catch (error) { showError(error, '应付详情加载失败。') } finally { loading.value = false }
}

function openCreate() {
  editing.value = false
  Object.assign(form, { supplierId: supplierId.value ?? 0, projectId: projectId.value ?? 0, purchaseOrderId: purchaseOrderId.value, payableType: 'PurchasePayment', title: '', amount: 0, dueDate: '', remark: undefined, version: undefined })
  formVisible.value = true
}

function openEdit() { if (!selected.value) return; editing.value = true; Object.assign(form, selected.value); formVisible.value = true }
function selectPurchaseOrder(value?: number) { const po = purchaseOrders.value.find(item => item.id === value); if (po) { form.supplierId = po.supplierId; form.projectId = po.projectId } }

async function save() {
  const validationError = getPayableValidationError(form)
  if (validationError) { ElMessage.warning(validationError); return }
  saving.value = true
  try {
    const input = { supplierId: form.supplierId, projectId: form.projectId, purchaseOrderId: form.purchaseOrderId || undefined, payableType: form.payableType, title: form.title.trim(), amount: form.amount, dueDate: form.dueDate, remark: form.remark }
    const saved = editing.value && selected.value ? await financeApi.updatePayable(selected.value.id, { ...input, version: form.version! } satisfies PayableUpdateInput) : await financeApi.createPayable(input)
    selected.value = saved
    formVisible.value = false
    detailVisible.value = true
    await load()
    ElMessage.success(editing.value ? '应付账款已更新。' : '应付账款已创建。')
  } catch (error) { showError(error, '应付账款保存失败。') } finally { saving.value = false }
}

async function toggleArchive() {
  if (!selected.value) return
  const archived = !selected.value.isArchived
  try {
    await ElMessageBox.confirm(archived ? '确认归档该应付？存在有效核销时系统会拒绝，请先取消核销。' : '确认恢复该应付？', archived ? '归档应付' : '恢复应付', { type: archived ? 'warning' : 'info' })
    selected.value = await financeApi.setPayableArchived(selected.value.id, archived, selected.value.version)
    await load()
    ElMessage.success(archived ? '应付已归档。' : '应付已恢复。')
  } catch (error) { if (error !== 'cancel') showError(error, archived ? '归档失败。' : '恢复失败。') }
}

async function cancelAllocation(allocationId: number, paymentId: number, version: number) {
  if (!selected.value) return
  try {
    await ElMessageBox.confirm('取消后付款和应付余额会重新计算，确认继续？', '取消核销', { type: 'warning' })
    await financeApi.cancelPaymentAllocation(paymentId, allocationId, version)
    selected.value = await financeApi.getPayable(selected.value.id)
    await load()
    ElMessage.success('核销已取消。')
  } catch (error) { if (error !== 'cancel') showError(error, '取消核销失败。') }
}

function changeSort({ prop, order }: { prop: string | null; order: string | null }) { sortBy.value = prop || 'updatedAt'; sortDescending.value = order !== 'ascending'; load() }
function statusTag(paymentStatus: PaymentStatus, isOverdue: boolean) { return paymentStatus === 'Paid' ? 'success' : isOverdue ? 'danger' : paymentStatus === 'PartiallyPaid' ? 'warning' : 'info' }
function showError(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    if (error.status === 409) { ElMessage.error('应付或付款数据已被其他用户修改，请刷新后重新操作。'); return }
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
        placeholder="应付编号、供应商、项目、采购单"
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
      <el-select
        v-model="projectId"
        clearable
        filterable
        placeholder="项目"
        @change="page = 1; load()"
      >
        <el-option
          v-for="item in projects"
          :key="item.id"
          :label="`${item.code} · ${item.name}`"
          :value="item.id"
        />
      </el-select>
      <el-select
        v-model="purchaseOrderId"
        clearable
        filterable
        placeholder="采购单"
        @change="page = 1; load()"
      >
        <el-option
          v-for="item in purchaseOrders"
          :key="item.id"
          :label="item.code"
          :value="item.id"
        />
      </el-select>
      <el-select
        v-model="payableType"
        clearable
        placeholder="应付类型"
        @change="page = 1; load()"
      >
        <el-option
          v-for="(label, value) in payableTypeLabels"
          :key="value"
          :label="label"
          :value="value"
        />
      </el-select>
      <el-select
        v-model="paymentStatus"
        clearable
        placeholder="支付状态"
        @change="page = 1; load()"
      >
        <el-option
          v-for="(label, value) in paymentStatusLabels"
          :key="value"
          :label="label"
          :value="value"
        />
      </el-select>
      <el-select
        v-model="overdueOnly"
        clearable
        placeholder="逾期情况"
        @change="page = 1; load()"
      >
        <el-option
          label="仅逾期"
          :value="true"
        /><el-option
          label="仅未逾期"
          :value="false"
        />
      </el-select>
      <el-date-picker
        v-model="dueRange"
        type="daterange"
        value-format="YYYY-MM-DD"
        range-separator="至"
        start-placeholder="到期开始"
        end-placeholder="到期结束"
        @change="page = 1; load()"
      />
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
        新增应付
      </el-button>
    </div>

    <div class="finance-table-scroll payable-table-scroll">
      <el-table
        v-loading="loading"
        class="flowhearth-data-table"
        :data="items"
        border
        stripe
        table-layout="fixed"
        @sort-change="changeSort"
        @row-dblclick="row => openDetails(row.id)"
      >
        <el-table-column
          prop="code"
          label="应付编号"
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
          min-width="220"
          show-overflow-tooltip
        />
        <el-table-column
          prop="projectName"
          label="项目"
          min-width="190"
        >
          <template #default="scope">
            {{ scope.row.projectCode }} · {{ scope.row.projectName }}
          </template>
        </el-table-column>
        <el-table-column
          prop="purchaseOrderCode"
          label="采购单"
          width="145"
        />
        <el-table-column
          prop="payableType"
          label="类型"
          width="100"
        >
          <template #default="scope">
            {{ payableTypeLabels[scope.row.payableType as PayableType] }}
          </template>
        </el-table-column>
        <el-table-column
          prop="title"
          label="标题"
          min-width="180"
          show-overflow-tooltip
        />
        <el-table-column
          prop="amount"
          label="应付金额"
          width="135"
          align="right"
          sortable="custom"
        >
          <template #default="scope">
            {{ money(scope.row.amount) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="allocatedAmount"
          label="已付"
          width="125"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.allocatedAmount) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="remainingAmount"
          label="未付"
          width="125"
          align="right"
        >
          <template #default="scope">
            {{ money(scope.row.remainingAmount) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="dueDate"
          label="到期日"
          width="120"
          sortable="custom"
        />
        <el-table-column
          prop="paymentStatus"
          label="状态"
          width="110"
          sortable="custom"
        >
          <template #default="scope">
            <el-tag :type="statusTag(scope.row.paymentStatus as PaymentStatus, scope.row.isOverdue)">
              {{ payableStatusLabel(scope.row.paymentStatus as PaymentStatus, scope.row.isOverdue) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column
          prop="overdueDays"
          label="逾期天数"
          width="100"
          align="right"
        >
          <template #default="scope">
            {{ scope.row.isOverdue ? `${scope.row.overdueDays} 天` : '—' }}
          </template>
        </el-table-column>
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
      title="应付详情"
      size="min(920px, 92vw)"
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
            v-if="canManagePayments && !selected.isArchived && selected.remainingAmount > 0"
            type="primary"
            @click="router.push({ name: 'payments', query: { supplierId: selected.supplierId } })"
          >
            前往付款核销
          </el-button>
        </div>
        <el-descriptions
          :column="2"
          border
        >
          <el-descriptions-item label="应付编号">
            {{ selected.code }}
          </el-descriptions-item><el-descriptions-item label="状态">
            <el-tag :type="statusTag(selected.paymentStatus, selected.isOverdue)">
              {{ selected.isOverdue ? `${payableStatusLabel(selected.paymentStatus, true)} ${selected.overdueDays} 天` : payableStatusLabel(selected.paymentStatus, false) }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="供应商">
            {{ selected.supplierCode }} · {{ selected.supplierName }}
          </el-descriptions-item><el-descriptions-item label="项目">
            {{ selected.projectCode }} · {{ selected.projectName }}
          </el-descriptions-item>
          <el-descriptions-item label="采购单">
            {{ selected.purchaseOrderCode || '未关联' }}
          </el-descriptions-item><el-descriptions-item label="类型">
            {{ payableTypeLabels[selected.payableType] }}
          </el-descriptions-item>
          <el-descriptions-item label="标题">
            {{ selected.title }}
          </el-descriptions-item><el-descriptions-item label="到期日">
            {{ selected.dueDate }}
          </el-descriptions-item>
          <el-descriptions-item label="应付金额">
            {{ money(selected.amount) }}
          </el-descriptions-item><el-descriptions-item label="已核销付款">
            {{ money(selected.allocatedAmount) }}
          </el-descriptions-item>
          <el-descriptions-item label="未付金额">
            {{ money(selected.remainingAmount) }}
          </el-descriptions-item><el-descriptions-item label="备注">
            {{ selected.remark || '—' }}
          </el-descriptions-item>
        </el-descriptions>
        <h3>付款核销记录</h3>
        <el-table
          :data="selected.allocations"
          table-layout="fixed"
        >
          <el-table-column
            prop="paymentCode"
            label="付款编号"
            width="150"
          /><el-table-column
            prop="paymentDate"
            label="付款日期"
            width="110"
          /><el-table-column
            prop="paymentAmount"
            label="付款总额"
            width="125"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.paymentAmount) }}
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
            prop="paymentMethod"
            label="付款方式"
            width="110"
          >
            <template #default="scope">
              {{ paymentMethodLabels[scope.row.paymentMethod as PaymentMethod] }}
            </template>
          </el-table-column><el-table-column
            prop="createdByDisplayName"
            label="操作人"
            width="110"
          /><el-table-column
            prop="createdAtUtc"
            label="核销时间"
            min-width="170"
          /><el-table-column
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
                v-else-if="canManagePayments"
                link
                type="danger"
                @click="cancelAllocation(scope.row.id, scope.row.paymentId, scope.row.version)"
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
      :title="editing ? '编辑应付' : '新增应付'"
      width="min(680px, 92vw)"
      destroy-on-close
    >
      <el-form label-width="100px">
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
          label="项目"
          required
        >
          <el-select
            v-model="form.projectId"
            filterable
            style="width: 100%"
          >
            <el-option
              v-for="item in projects"
              :key="item.id"
              :label="`${item.code} · ${item.name}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="采购单">
          <el-select
            v-model="form.purchaseOrderId"
            clearable
            filterable
            style="width: 100%"
            @change="selectPurchaseOrder"
          >
            <el-option
              v-for="item in availablePurchaseOrders"
              :key="item.id"
              :label="`${item.code} · ${money(item.totalAmount)}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          label="应付类型"
          required
        >
          <el-select
            v-model="form.payableType"
            style="width: 100%"
          >
            <el-option
              v-for="(label, value) in payableTypeLabels"
              :key="value"
              :label="label"
              :value="value"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          label="标题"
          required
        >
          <el-input
            v-model="form.title"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item
          label="金额"
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
          label="到期日"
          required
        >
          <el-date-picker
            v-model="form.dueDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
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
  </section>
</template>

<style scoped>
.drawer-actions { display: flex; justify-content: flex-end; gap: 8px; margin-bottom: 16px; }
.finance-table-scroll { width: 100%; overflow-x: auto; }
.payable-table-scroll :deep(.el-table) { min-width: 1660px; }
h3 { margin-top: 24px; }
</style>
