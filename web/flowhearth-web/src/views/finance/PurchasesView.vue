<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { listAuditLogs } from '../../api/audit'
import * as financeApi from '../../api/finance'
import { ApiError } from '../../api/problem-details'
import { listProjects } from '../../api/projects'
import EntityAttachmentsPanel from '../../components/attachments/EntityAttachmentsPanel.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { AuditLogSummary } from '../../types/audit'
import type { PayablePlanItemInput, PayableSummary, PurchaseOrderDetails, PurchaseOrderInput, PurchaseOrderStatus, PurchaseOrderSummary, SupplierSummary } from '../../types/finance'
import type { ProjectSummary } from '../../types/projects'
import { calculatePercentagePlan } from '../../utils/finance-format'
import { payableTypeLabels } from '../../utils/payable-format'
import { calculatePurchaseLineAmount, createPurchaseItem, getReceiptValidationError, purchaseStatusLabels, removePurchaseItem } from '../../utils/purchase-format'

const auth = useAuthStore()
const settings = useSettingsStore()
const route = useRoute()
const router = useRouter()
const canManage = computed(() => auth.canAny([permissions.purchasesManage]))
const canAudit = computed(() => auth.canAny([permissions.auditView]))
const loading = ref(false)
const saving = ref(false)
const items = ref<PurchaseOrderSummary[]>([])
const suppliers = ref<SupplierSummary[]>([])
const projects = ref<ProjectSummary[]>([])
const selected = ref<PurchaseOrderDetails>()
const audits = ref<AuditLogSummary[]>([])
const detailVisible = ref(false)
const formVisible = ref(false)
const receiptVisible = ref(false)
const payablePlanVisible = ref(false)
const editing = ref(false)
const page = ref(1)
const pageSize = ref(settings.defaultPageSize)
const total = ref(0)
const filters = reactive({ search: '', supplierId: undefined as number | undefined, projectId: undefined as number | undefined, status: '' as '' | PurchaseOrderStatus, archive: 'active', orderDates: [] as string[] })
const sortBy = ref('updatedAt')
const sortDescending = ref(true)
const form = reactive<PurchaseOrderInput & { version?: number }>({ supplierId: 0, projectId: 0, orderDate: today(), items: [createPurchaseItem()] })
const receipt = reactive({ receivedDate: today(), remark: '', rows: [] as Array<{ purchaseOrderItemId: number; itemName: string; unit: string; remainingQuantity: number; quantityReceived: number }> })
type PayablePlanRow = PayablePlanItemInput & { percentage: number }
const payablePlanRows = reactive<PayablePlanRow[]>([])
const existingPayables = ref<PayableSummary[]>([])
const payablePercentageMode = ref(true)
const statusTypes: Record<PurchaseOrderStatus, 'info' | 'primary' | 'warning' | 'success' | 'danger'> = { Draft: 'info', Ordered: 'primary', PartiallyReceived: 'warning', Received: 'success', Cancelled: 'danger' }
const receiptLabels = { NotReceived: '未收', PartiallyReceived: '部分收货', Received: '已收' }
const money = (value: number) => new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY' }).format(value)
const formTotal = computed(() => form.items.reduce((sum, row) => sum + calculatePurchaseLineAmount(row.quantity || 0, row.unitPrice || 0), 0))
const canEdit = computed(() => !!selected.value && !selected.value.isArchived && !['Received', 'Cancelled'].includes(selected.value.status))
const canReceive = computed(() => !!selected.value && !selected.value.isArchived && ['Ordered', 'PartiallyReceived'].includes(selected.value.status) && selected.value.items.some(row => row.remainingQuantity > 0))
const existingPayableTotal = computed(() => existingPayables.value.reduce((sum, row) => sum + row.amount, 0))
const payablePlanTotal = computed(() => payablePlanRows.reduce((sum, row) => sum + (row.amount || 0), 0))
const payablePlanRemaining = computed(() => (selected.value?.totalAmount ?? 0) - existingPayableTotal.value - payablePlanTotal.value)

onMounted(async () => { await loadOptions(); await load(); const id = Number(route.query.entityId); if (id) await openDetails(id) })
watch(() => route.query.entityId, async value => { const id = Number(value); if (id && selected.value?.id !== id) await openDetails(id) })

function today() { const date = new Date(); const offset = date.getTimezoneOffset() * 60000; return new Date(date.getTime() - offset).toISOString().slice(0, 10) }
async function loadOptions() {
  try {
    const [supplierPage, projectPage] = await Promise.all([
      financeApi.listSuppliers({ page: 1, pageSize: 100, archive: 'active', status: 'Active', sortBy: 'name', sortDescending: false }),
      listProjects({ page: 1, pageSize: 100, archive: 'active', sortBy: 'updatedAt', sortDescending: true }),
    ])
    suppliers.value = supplierPage.items
    projects.value = projectPage.items
  } catch { ElMessage.error('供应商或项目选项加载失败。') }
}
async function load() {
  loading.value = true
  try {
    const result = await financeApi.listPurchaseOrders({ page: page.value, pageSize: pageSize.value, search: filters.search.trim() || undefined, supplierId: filters.supplierId, projectId: filters.projectId, status: filters.status || undefined, archive: filters.archive, orderFrom: filters.orderDates[0], orderTo: filters.orderDates[1], sortBy: sortBy.value, sortDescending: sortDescending.value })
    items.value = result.items
    total.value = result.total
  } catch (error) { showError(error, '采购单列表加载失败。') } finally { loading.value = false }
}
async function openDetails(id: number) {
  loading.value = true
  try {
    selected.value = await financeApi.getPurchaseOrder(id)
    audits.value = []
    if (canAudit.value) {
      const result = await listAuditLogs({ page: 1, pageSize: 100, entityType: 'purchase_order', search: selected.value.code, sortBy: 'occurredAt', sortDescending: true })
      audits.value = result.items.filter(item => item.entityId === id)
    }
    detailVisible.value = true
    if (String(route.query.entityId ?? '') !== String(id)) await router.replace({ query: { ...route.query, entityId: String(id) } })
  } catch (error) { showError(error, '采购单详情加载失败。') } finally { loading.value = false }
}
function closeDetails() { void router.replace({ query: { ...route.query, entityId: undefined } }) }
function resetForm() { Object.assign(form, { supplierId: 0, projectId: 0, orderDate: today(), contactName: undefined, deliveryAddress: undefined, expectedDeliveryDate: undefined, remark: undefined, items: [createPurchaseItem()], version: undefined }) }
function openCreate() { editing.value = false; resetForm(); formVisible.value = true }
function openEdit() {
  if (!selected.value) return
  editing.value = true
  Object.assign(form, { supplierId: selected.value.supplierId, projectId: selected.value.projectId, orderDate: selected.value.orderDate, contactName: selected.value.contactName, deliveryAddress: selected.value.deliveryAddress, expectedDeliveryDate: selected.value.expectedDeliveryDate, remark: selected.value.remark, version: selected.value.version, items: selected.value.items.map(row => ({ id: row.id, itemName: row.itemName, manufacturer: row.manufacturer, model: row.model, specification: row.specification, quantity: row.quantity, unit: row.unit, unitPrice: row.unitPrice, remark: row.remark })) })
  formVisible.value = true
}
async function save() {
  if (!form.supplierId || !form.projectId || !form.orderDate) { ElMessage.warning('请选择供应商、项目和下单日期。'); return }
  if (!form.items.length || form.items.some(row => !row.itemName.trim() || !row.unit.trim() || row.quantity <= 0 || row.unitPrice < 0)) { ElMessage.warning('请完整填写采购明细，数量须大于 0、单价不能为负。'); return }
  saving.value = true
  try {
    const input = { ...form, items: form.items.map(row => ({ ...row, itemName: row.itemName.trim(), unit: row.unit.trim() })) }
    const saved = editing.value && selected.value ? await financeApi.updatePurchaseOrder(selected.value.id, input as PurchaseOrderInput & { version: number }) : await financeApi.createPurchaseOrder(input)
    selected.value = saved
    formVisible.value = false
    detailVisible.value = true
    await load()
    ElMessage.success(editing.value ? '采购单已更新。' : '采购单草稿已创建。')
  } catch (error) { showError(error, '采购单保存失败。') } finally { saving.value = false }
}
async function transition(action: 'order' | 'cancel') {
  if (!selected.value) return
  try {
    await ElMessageBox.confirm(action === 'order' ? '正式下单后即可登记收货，确认继续？' : '取消后不能继续收货，确认取消？', action === 'order' ? '正式下单' : '取消采购单', { type: 'warning' })
    selected.value = await financeApi.transitionPurchaseOrder(selected.value.id, action, selected.value.version)
    await load()
    ElMessage.success(action === 'order' ? '采购单已正式下单。' : '采购单已取消。')
  } catch (error) { if (error !== 'cancel') showError(error, '状态操作失败。') }
}
async function toggleArchive() {
  if (!selected.value) return
  const archived = !selected.value.isArchived
  try {
    await ElMessageBox.confirm(archived ? '确认归档该采购单？' : '确认恢复该采购单？', archived ? '归档采购单' : '恢复采购单')
    selected.value = await financeApi.setPurchaseOrderArchived(selected.value.id, archived, selected.value.version)
    await load()
  } catch (error) { if (error !== 'cancel') showError(error, '归档操作失败。') }
}
function openReceipt() {
  if (!selected.value) return
  receipt.receivedDate = today(); receipt.remark = ''
  receipt.rows = selected.value.items.filter(row => row.remainingQuantity > 0).map(row => ({ purchaseOrderItemId: row.id, itemName: row.itemName, unit: row.unit, remainingQuantity: row.remainingQuantity, quantityReceived: 0 }))
  receiptVisible.value = true
}
async function saveReceipt() {
  if (!selected.value) return
  const error = getReceiptValidationError(receipt.rows)
  if (error) { ElMessage.warning(error); return }
  saving.value = true
  try {
    selected.value = await financeApi.receivePurchaseOrder(selected.value.id, { receivedDate: receipt.receivedDate, remark: receipt.remark.trim() || undefined, purchaseOrderVersion: selected.value.version, items: receipt.rows.filter(row => row.quantityReceived > 0).map(row => ({ purchaseOrderItemId: row.purchaseOrderItemId, quantityReceived: row.quantityReceived })) })
    receiptVisible.value = false
    await load()
    ElMessage.success('收货已登记，采购状态和进度已更新。')
  } catch (error) { showError(error, '收货登记失败。') } finally { saving.value = false }
}
async function openPayablePlan() {
  if (!selected.value) return
  loading.value = true
  try {
    existingPayables.value = (await financeApi.listPayables({ page: 1, pageSize: 100, purchaseOrderId: selected.value.id, archive: 'all', sortBy: 'dueDate', sortDescending: false })).items
    payablePlanRows.splice(0, payablePlanRows.length,
      { payableType: 'AdvancePayment', title: '预付款', percentage: 30, amount: 0, dueDate: '' },
      { payableType: 'DeliveryPayment', title: '到货款', percentage: 60, amount: 0, dueDate: '' },
      { payableType: 'RetentionPayment', title: '质保款', percentage: 10, amount: 0, dueDate: '' },
    )
    calculatePayablePercentages()
    payablePlanVisible.value = true
  } catch (error) { showError(error, '采购应付数据加载失败。') } finally { loading.value = false }
}
function calculatePayablePercentages() {
  const available = (selected.value?.totalAmount ?? 0) - existingPayableTotal.value
  try {
    const amounts = calculatePercentagePlan(available, payablePlanRows.map(row => row.percentage))
    payablePlanRows.forEach((row, index) => { row.amount = amounts[index]! })
  } catch { ElMessage.warning('各阶段比例合计必须为 100%，且可安排金额必须大于 0。') }
}
function addPayablePlanRow() { payablePlanRows.push({ payableType: 'Other', title: '', percentage: 0, amount: 0, dueDate: '' }) }
async function savePayablePlan() {
  if (!selected.value || payablePlanRows.some(row => !row.title.trim() || !row.dueDate || row.amount <= 0)) { ElMessage.warning('请完整填写每个应付阶段的类型、标题、金额和到期日。'); return }
  if (payablePlanRemaining.value < -0.001) { ElMessage.warning('应付计划总额不能超过采购单金额。'); return }
  saving.value = true
  try {
    await financeApi.createPayablePlan(selected.value.id, payablePlanRows.map(row => ({ payableType: row.payableType, title: row.title.trim(), amount: row.amount, dueDate: row.dueDate, remark: row.remark })))
    payablePlanVisible.value = false
    ElMessage.success(payablePlanRemaining.value > 0 ? '应付计划已创建，采购金额尚未全部安排。' : '应付计划已创建。')
  } catch (error) { showError(error, '应付计划创建失败。') } finally { saving.value = false }
}
function changeSort({ prop, order }: { prop: string | null; order: string | null }) { sortBy.value = prop || 'updatedAt'; sortDescending.value = order !== 'ascending'; void load() }
function showError(error: unknown, fallback: string) { if (error instanceof ApiError) { if (error.status === 409) { ElMessage.error('采购单已被其他操作修改，请刷新后重试。'); return } const validation = Object.values(error.problem?.errors ?? {}).flat()[0]; ElMessage.error(validation ?? error.problem?.detail ?? fallback); return } ElMessage.error(fallback) }
</script>

<template>
  <section class="module-page">
    <div class="finance-filter-grid purchase-toolbar">
      <el-input
        v-model="filters.search"
        clearable
        placeholder="采购单号、供应商或项目"
        @keyup.enter="page = 1; load()"
      >
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>
      <el-select
        v-model="filters.supplierId"
        filterable
        clearable
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
        v-model="filters.projectId"
        filterable
        clearable
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
        v-model="filters.status"
        clearable
        placeholder="状态"
        @change="page = 1; load()"
      >
        <el-option
          v-for="(label, value) in purchaseStatusLabels"
          :key="value"
          :label="label"
          :value="value"
        />
      </el-select>
      <el-date-picker
        v-model="filters.orderDates"
        type="daterange"
        value-format="YYYY-MM-DD"
        start-placeholder="下单开始"
        end-placeholder="下单结束"
        @change="page = 1; load()"
      />
      <el-select
        v-model="filters.archive"
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
        新建采购单
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
      @sort-change="changeSort"
    >
      <el-table-column
        prop="code"
        label="采购单号"
        width="155"
        sortable="custom"
      /><el-table-column
        prop="supplierName"
        label="供应商"
        min-width="220"
      /><el-table-column
        prop="projectName"
        label="关联项目"
        min-width="200"
      >
        <template #default="scope">
          <strong>{{ scope.row.projectCode }}</strong><br><small>{{ scope.row.projectName }}</small>
        </template>
      </el-table-column><el-table-column
        prop="orderDate"
        label="下单日"
        width="112"
        sortable="custom"
      /><el-table-column
        label="金额"
        prop="totalAmount"
        width="145"
        align="right"
        sortable="custom"
      >
        <template #default="scope">
          {{ money(scope.row.totalAmount) }}
        </template>
      </el-table-column><el-table-column
        label="状态"
        prop="status"
        width="112"
        sortable="custom"
      >
        <template #default="scope">
          <el-tag :type="statusTypes[scope.row.status as PurchaseOrderStatus]">
            {{ purchaseStatusLabels[scope.row.status as PurchaseOrderStatus] }}
          </el-tag>
        </template>
      </el-table-column><el-table-column
        label="收货进度"
        min-width="190"
      >
        <template #default="scope">
          <el-progress
            :percentage="scope.row.receiptProgress"
            :stroke-width="10"
          /><small>已收 {{ scope.row.receivedItemCount }} / 部分 {{ scope.row.partiallyReceivedItemCount }} / 未收 {{ scope.row.notReceivedItemCount }}</small>
        </template>
      </el-table-column><el-table-column
        prop="expectedDeliveryDate"
        label="预计到货"
        width="112"
        sortable="custom"
      /><el-table-column
        prop="createdByDisplayName"
        label="创建人"
        width="110"
      /><el-table-column
        label="操作"
        width="75"
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
      size="min(1100px, 98vw)"
      destroy-on-close
      @closed="closeDetails"
    >
      <template #header>
        <div v-if="selected">
          <strong>{{ selected.code }} · {{ selected.supplierName }}</strong> <el-tag :type="statusTypes[selected.status]">
            {{ purchaseStatusLabels[selected.status] }}
          </el-tag> <el-tag
            v-if="selected.isArchived"
            type="info"
          >
            已归档
          </el-tag>
        </div>
      </template>
      <template v-if="selected">
        <div
          v-if="canManage"
          class="detail-action-row"
        >
          <el-button
            :disabled="!canEdit"
            @click="openEdit"
          >
            编辑
          </el-button><el-button
            v-if="selected.status === 'Draft'"
            type="primary"
            @click="transition('order')"
          >
            正式下单
          </el-button><el-button
            v-if="canReceive"
            type="success"
            @click="openReceipt"
          >
            登记收货
          </el-button><el-button
            v-if="!selected.isArchived && !['Draft','Cancelled'].includes(selected.status)"
            type="primary"
            plain
            @click="openPayablePlan"
          >
            创建应付计划
          </el-button><el-button
            v-if="['Draft','Ordered'].includes(selected.status) && !selected.isArchived"
            type="danger"
            plain
            @click="transition('cancel')"
          >
            取消
          </el-button><el-button
            plain
            @click="toggleArchive"
          >
            {{ selected.isArchived ? '恢复' : '归档' }}
          </el-button>
        </div>
        <div class="finance-card-grid">
          <div><span>采购金额</span><strong>{{ money(selected.totalAmount) }}</strong></div><div><span>采购数量</span><strong>{{ selected.orderedQuantity }}</strong></div><div><span>已收数量</span><strong>{{ selected.receivedQuantity }}</strong></div><div><span>收货进度</span><strong>{{ selected.receiptProgress }}%</strong></div>
        </div>
        <el-tabs>
          <el-tab-pane label="概览">
            <el-descriptions
              :column="2"
              border
            >
              <el-descriptions-item label="供应商">
                {{ selected.supplierCode }} · {{ selected.supplierName }}
              </el-descriptions-item><el-descriptions-item label="项目">
                {{ selected.projectCode }} · {{ selected.projectName }}
              </el-descriptions-item><el-descriptions-item label="下单日期">
                {{ selected.orderDate }}
              </el-descriptions-item><el-descriptions-item label="预计到货">
                {{ selected.expectedDeliveryDate || '—' }}
              </el-descriptions-item><el-descriptions-item label="联系人">
                {{ selected.contactName || '—' }}
              </el-descriptions-item><el-descriptions-item label="创建人">
                {{ selected.createdByDisplayName || '—' }}
              </el-descriptions-item><el-descriptions-item
                label="收货地址"
                :span="2"
              >
                {{ selected.deliveryAddress || '—' }}
              </el-descriptions-item><el-descriptions-item
                label="备注"
                :span="2"
              >
                {{ selected.remark || '—' }}
              </el-descriptions-item>
            </el-descriptions>
          </el-tab-pane>
          <el-tab-pane :label="`明细 (${selected.items.length})`">
            <div class="purchase-table-scroll">
              <el-table
                :data="selected.items"
                stripe
              >
                <el-table-column
                  prop="itemName"
                  label="品名"
                  min-width="180"
                /><el-table-column
                  prop="manufacturer"
                  label="制造商"
                  min-width="120"
                /><el-table-column
                  prop="model"
                  label="型号"
                  min-width="120"
                /><el-table-column
                  prop="specification"
                  label="规格"
                  min-width="150"
                /><el-table-column
                  prop="quantity"
                  label="数量"
                  width="100"
                  align="right"
                /><el-table-column
                  prop="unit"
                  label="单位"
                  width="70"
                /><el-table-column
                  label="单价"
                  width="120"
                  align="right"
                >
                  <template #default="scope">
                    {{ money(scope.row.unitPrice) }}
                  </template>
                </el-table-column><el-table-column
                  label="金额"
                  width="130"
                  align="right"
                >
                  <template #default="scope">
                    {{ money(scope.row.amount) }}
                  </template>
                </el-table-column><el-table-column
                  prop="receivedQuantity"
                  label="已收"
                  width="90"
                  align="right"
                /><el-table-column
                  prop="remainingQuantity"
                  label="未收"
                  width="90"
                  align="right"
                /><el-table-column
                  label="行状态"
                  width="105"
                >
                  <template #default="scope">
                    {{ receiptLabels[scope.row.receiptStatus as keyof typeof receiptLabels] }}
                  </template>
                </el-table-column>
              </el-table>
            </div>
          </el-tab-pane>
          <el-tab-pane :label="`收货记录 (${selected.receipts.length})`">
            <el-collapse>
              <el-collapse-item
                v-for="record in selected.receipts"
                :key="record.id"
                :title="`${record.code} · ${record.receivedDate} · ${record.receivedByDisplayName}`"
              >
                <p>{{ record.remark || '无备注' }}</p><el-table :data="record.items">
                  <el-table-column
                    prop="itemName"
                    label="品名"
                  /><el-table-column
                    prop="quantityReceived"
                    label="本次收货"
                    width="130"
                    align="right"
                  /><el-table-column
                    prop="unit"
                    label="单位"
                    width="80"
                  />
                </el-table>
              </el-collapse-item>
            </el-collapse><el-empty
              v-if="selected.receipts.length === 0"
              description="尚无收货记录"
            />
          </el-tab-pane>
          <el-tab-pane label="附件">
            <EntityAttachmentsPanel
              entity-type="PurchaseOrder"
              :entity-id="selected.id"
            />
          </el-tab-pane>
          <el-tab-pane
            v-if="canAudit"
            :label="`操作记录 (${audits.length})`"
          >
            <el-timeline>
              <el-timeline-item
                v-for="item in audits"
                :key="item.id"
                :timestamp="new Date(item.occurredAtUtc).toLocaleString('zh-CN')"
              >
                <strong>{{ item.summary }}</strong><p>{{ item.actorDisplayName || item.actorUsername || '系统' }}</p>
              </el-timeline-item>
            </el-timeline><el-empty
              v-if="!audits.length"
              description="暂无操作记录"
            />
          </el-tab-pane>
        </el-tabs>
      </template>
    </el-drawer>

    <el-dialog
      v-model="formVisible"
      draggable
      :title="editing ? '编辑采购单' : '新建采购单草稿'"
      width="min(1200px, 96vw)"
      destroy-on-close
    >
      <el-form label-position="top">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item
              label="供应商"
              required
            >
              <el-select
                v-model="form.supplierId"
                filterable
              >
                <el-option
                  v-for="item in suppliers"
                  :key="item.id"
                  :label="`${item.code} · ${item.name}`"
                  :value="item.id"
                />
              </el-select>
            </el-form-item>
          </el-col><el-col :span="8">
            <el-form-item
              label="关联项目"
              required
            >
              <el-select
                v-model="form.projectId"
                filterable
              >
                <el-option
                  v-for="item in projects"
                  :key="item.id"
                  :label="`${item.code} · ${item.name}`"
                  :value="item.id"
                />
              </el-select>
            </el-form-item>
          </el-col><el-col :span="4">
            <el-form-item
              label="下单日期"
              required
            >
              <el-date-picker
                v-model="form.orderDate"
                type="date"
                value-format="YYYY-MM-DD"
              />
            </el-form-item>
          </el-col><el-col :span="4">
            <el-form-item label="预计到货">
              <el-date-picker
                v-model="form.expectedDeliveryDate"
                type="date"
                value-format="YYYY-MM-DD"
              />
            </el-form-item>
          </el-col>
        </el-row><el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="联系人">
              <el-input v-model="form.contactName" />
            </el-form-item>
          </el-col><el-col :span="16">
            <el-form-item label="收货地址">
              <el-input v-model="form.deliveryAddress" />
            </el-form-item>
          </el-col>
        </el-row>
        <div class="detail-tab-toolbar">
          <strong>采购明细</strong><el-button @click="form.items.push(createPurchaseItem())">
            增加一行
          </el-button><span>合计：{{ money(formTotal) }}</span>
        </div><div class="purchase-table-scroll">
          <el-table :data="form.items">
            <el-table-column
              label="品名 *"
              min-width="160"
            >
              <template #default="scope">
                <el-input v-model="scope.row.itemName" />
              </template>
            </el-table-column><el-table-column
              label="制造商"
              min-width="120"
            >
              <template #default="scope">
                <el-input v-model="scope.row.manufacturer" />
              </template>
            </el-table-column><el-table-column
              label="型号"
              min-width="110"
            >
              <template #default="scope">
                <el-input v-model="scope.row.model" />
              </template>
            </el-table-column><el-table-column
              label="规格"
              min-width="140"
            >
              <template #default="scope">
                <el-input v-model="scope.row.specification" />
              </template>
            </el-table-column><el-table-column
              label="数量 *"
              width="145"
            >
              <template #default="scope">
                <el-input-number
                  v-model="scope.row.quantity"
                  :min="0.0001"
                  :precision="4"
                />
              </template>
            </el-table-column><el-table-column
              label="单位 *"
              width="90"
            >
              <template #default="scope">
                <el-input v-model="scope.row.unit" />
              </template>
            </el-table-column><el-table-column
              label="单价"
              width="150"
            >
              <template #default="scope">
                <el-input-number
                  v-model="scope.row.unitPrice"
                  :min="0"
                  :precision="2"
                />
              </template>
            </el-table-column><el-table-column
              label="金额"
              width="125"
              align="right"
            >
              <template #default="scope">
                {{ money(calculatePurchaseLineAmount(scope.row.quantity, scope.row.unitPrice)) }}
              </template>
            </el-table-column><el-table-column
              label="操作"
              width="70"
            >
              <template #default="scope">
                <el-button
                  link
                  type="danger"
                  :disabled="form.items.length === 1"
                  @click="removePurchaseItem(form.items, scope.$index)"
                >
                  删除
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </div><el-form-item label="备注">
          <el-input
            v-model="form.remark"
            type="textarea"
            :rows="2"
          />
        </el-form-item>
      </el-form><template #footer>
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
      v-model="receiptVisible"
      draggable
      title="登记收货"
      width="850px"
      destroy-on-close
    >
      <el-form label-position="top">
        <el-form-item
          label="收货日期"
          required
        >
          <el-date-picker
            v-model="receipt.receivedDate"
            type="date"
            value-format="YYYY-MM-DD"
          />
        </el-form-item><el-table :data="receipt.rows">
          <el-table-column
            prop="itemName"
            label="品名"
            min-width="180"
          /><el-table-column
            prop="remainingQuantity"
            label="未收数量"
            width="120"
            align="right"
          /><el-table-column
            label="本次收货"
            width="190"
          >
            <template #default="scope">
              <el-input-number
                v-model="scope.row.quantityReceived"
                :min="0"
                :max="scope.row.remainingQuantity"
                :precision="4"
              />
            </template>
          </el-table-column><el-table-column
            prop="unit"
            label="单位"
            width="80"
          />
        </el-table><el-form-item label="备注">
          <el-input
            v-model="receipt.remark"
            type="textarea"
          />
        </el-form-item>
      </el-form><template #footer>
        <el-button @click="receiptVisible = false">
          取消
        </el-button><el-button
          type="primary"
          :loading="saving"
          @click="saveReceipt"
        >
          确认收货
        </el-button>
      </template>
    </el-dialog>
    <el-dialog
      v-model="payablePlanVisible"
      draggable
      title="创建采购应付计划"
      width="min(1040px, 96vw)"
      destroy-on-close
    >
      <template v-if="selected">
        <div class="finance-card-grid">
          <div><span>采购金额</span><strong>{{ money(selected.totalAmount) }}</strong></div><div><span>已安排应付</span><strong>{{ money(existingPayableTotal) }}</strong></div><div><span>本次计划</span><strong>{{ money(payablePlanTotal) }}</strong></div><div><span>计划后未安排</span><strong :class="{ 'finance-negative': payablePlanRemaining < 0 }">{{ money(payablePlanRemaining) }}</strong></div>
        </div>
        <div class="detail-tab-toolbar">
          <el-switch
            v-model="payablePercentageMode"
            active-text="百分比辅助"
            inactive-text="直接金额"
          /><el-button
            v-if="payablePercentageMode"
            @click="calculatePayablePercentages"
          >
            按百分比计算
          </el-button><el-button @click="addPayablePlanRow">
            增加阶段
          </el-button>
        </div>
        <el-table :data="payablePlanRows">
          <el-table-column
            label="类型"
            width="140"
          >
            <template #default="scope">
              <el-select v-model="scope.row.payableType">
                <el-option
                  v-for="(label, value) in payableTypeLabels"
                  :key="value"
                  :label="label"
                  :value="value"
                />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column
            label="标题"
            min-width="150"
          >
            <template #default="scope">
              <el-input v-model="scope.row.title" />
            </template>
          </el-table-column>
          <el-table-column
            v-if="payablePercentageMode"
            label="比例"
            width="135"
          >
            <template #default="scope">
              <el-input-number
                v-model="scope.row.percentage"
                :min="0.01"
                :max="100"
                :precision="2"
                @change="calculatePayablePercentages"
              />
            </template>
          </el-table-column>
          <el-table-column
            label="确定金额"
            width="175"
          >
            <template #default="scope">
              <el-input-number
                v-model="scope.row.amount"
                :min="0.01"
                :precision="2"
                :step="1000"
              />
            </template>
          </el-table-column>
          <el-table-column
            label="到期日"
            width="165"
          >
            <template #default="scope">
              <el-date-picker
                v-model="scope.row.dueDate"
                type="date"
                value-format="YYYY-MM-DD"
              />
            </template>
          </el-table-column>
          <el-table-column
            label="备注"
            min-width="140"
          >
            <template #default="scope">
              <el-input v-model="scope.row.remark" />
            </template>
          </el-table-column>
          <el-table-column
            label="操作"
            width="70"
          >
            <template #default="scope">
              <el-button
                link
                type="danger"
                :disabled="payablePlanRows.length === 1"
                @click="payablePlanRows.splice(scope.$index, 1)"
              >
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
        <el-alert
          v-if="payablePlanRemaining > 0"
          :title="`尚有 ${money(payablePlanRemaining)} 采购金额未安排应付。`"
          type="warning"
          :closable="false"
        /><el-alert
          v-if="payablePlanRemaining < 0"
          title="计划总额超过采购金额，不能提交。"
          type="error"
          :closable="false"
        />
      </template>
      <template #footer>
        <el-button @click="payablePlanVisible = false">
          取消
        </el-button><el-button
          type="primary"
          :loading="saving"
          :disabled="payablePlanRemaining < 0"
          @click="savePayablePlan"
        >
          创建计划
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.purchase-table-scroll { width: 100%; overflow-x: auto; }
.purchase-table-scroll :deep(.el-table) { min-width: 1040px; }
</style>
