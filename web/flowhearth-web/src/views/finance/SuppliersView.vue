<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

import EntityAttachmentsPanel from '../../components/attachments/EntityAttachmentsPanel.vue'
import { listAuditLogs } from '../../api/audit'
import * as financeApi from '../../api/finance'
import { ApiError } from '../../api/problem-details'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { AuditLogSummary } from '../../types/audit'
import type { PayableSummary, PayableType, PaymentMethod, PaymentSummary, PaymentStatus, PurchaseOrderStatus, PurchaseOrderSummary, SupplierDetails, SupplierInput, SupplierSummary, SupplierUpdateInput } from '../../types/finance'
import { payableStatusLabel, payableTypeLabels, paymentMethodLabels } from '../../utils/payable-format'
import { purchaseStatusLabels } from '../../utils/purchase-format'

const auth = useAuthStore()
const route = useRoute()
const settings = useSettingsStore()
const canManage = computed(() => auth.canAny([permissions.suppliersManage]))
const canViewAudit = computed(() => auth.canAny([permissions.auditView]))
const loading = ref(false)
const saving = ref(false)
const formVisible = ref(false)
const detailVisible = ref(false)
const items = ref<SupplierSummary[]>([])
const selected = ref<SupplierDetails>()
const audits = ref<AuditLogSummary[]>([])
const supplierPurchases = ref<PurchaseOrderSummary[]>([])
const supplierPayables = ref<PayableSummary[]>([])
const supplierPayments = ref<PaymentSummary[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(settings.defaultPageSize)
const search = ref('')
const category = ref('')
const status = ref('')
const archive = ref('active')
const sortBy = ref('updatedAt')
const sortDescending = ref(true)
const editing = ref(false)
const form = reactive<SupplierInput & { version?: number }>({ name: '', status: 'Active', creditDays: 0 })
const money = (value: number) => new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY' }).format(value)

onMounted(async () => {
  await load()
  await openFromRoute(route.query.entityId)
})
watch(() => route.query.entityId, openFromRoute)

async function openFromRoute(value: unknown) {
  const raw = Array.isArray(value) ? value[0] : value
  const id = Number(raw)
  if (Number.isSafeInteger(id) && id > 0 && selected.value?.id !== id) await openDetails(id)
}

async function load() {
  loading.value = true
  try {
    const result = await financeApi.listSuppliers({ page: page.value, pageSize: pageSize.value, search: search.value.trim() || undefined, category: category.value.trim() || undefined, status: status.value || undefined, archive: archive.value, sortBy: sortBy.value, sortDescending: sortDescending.value })
    items.value = result.items
    total.value = result.total
  } catch (error) { showError(error, '供应商列表加载失败。') } finally { loading.value = false }
}

async function openDetails(id: number) {
  loading.value = true
  try {
    selected.value = await financeApi.getSupplier(id)
    const [purchases, payables, payments] = await Promise.all([
      financeApi.listPurchaseOrders({ page: 1, pageSize: 100, supplierId: id, archive: 'all', sortBy: 'orderDate', sortDescending: true }),
      financeApi.listPayables({ page: 1, pageSize: 100, supplierId: id, archive: 'all', sortBy: 'dueDate', sortDescending: false }),
      financeApi.listPayments({ page: 1, pageSize: 100, supplierId: id, archive: 'all', sortBy: 'paymentDate', sortDescending: true }),
    ])
    supplierPurchases.value = purchases.items
    supplierPayables.value = payables.items
    supplierPayments.value = payments.items
    audits.value = []
    if (canViewAudit.value) {
      const result = await listAuditLogs({ page: 1, pageSize: 50, entityType: 'supplier', search: selected.value.code, sortBy: 'occurredAt', sortDescending: true })
      audits.value = result.items.filter(item => item.entityId === id)
    }
    detailVisible.value = true
  } catch (error) { showError(error, '供应商详情加载失败。') } finally { loading.value = false }
}

function clearForm() {
  Object.assign(form, { name: '', shortName: undefined, status: 'Active', category: undefined, contactName: undefined, mobile: undefined, phone: undefined, email: undefined, weChat: undefined, province: undefined, city: undefined, address: undefined, paymentTerms: undefined, creditDays: 0, bankName: undefined, bankAccountName: undefined, remark: undefined, version: undefined })
}

function openCreate() { editing.value = false; clearForm(); formVisible.value = true }
function openEdit() {
  if (!selected.value) return
  editing.value = true
  Object.assign(form, selected.value)
  formVisible.value = true
}

async function save() {
  if (!form.name.trim()) { ElMessage.warning('请输入供应商名称。'); return }
  saving.value = true
  try {
    const input = { ...form, name: form.name.trim() }
    const saved = editing.value && selected.value
      ? await financeApi.updateSupplier(selected.value.id, input as SupplierUpdateInput)
      : await financeApi.createSupplier(input)
    selected.value = saved
    formVisible.value = false
    detailVisible.value = true
    await load()
    ElMessage.success(editing.value ? '供应商已更新。' : '供应商已创建。')
  } catch (error) { showError(error, '供应商保存失败。') } finally { saving.value = false }
}

async function toggleArchive() {
  if (!selected.value) return
  const archived = !selected.value.isArchived
  try {
    await ElMessageBox.confirm(archived ? '归档后历史采购和财务记录仍会保留，确认归档？' : '确认恢复该供应商？', archived ? '归档供应商' : '恢复供应商', { type: archived ? 'warning' : 'info' })
    selected.value = await financeApi.setSupplierArchived(selected.value.id, archived, selected.value.version)
    await load()
    ElMessage.success(archived ? '供应商已归档。' : '供应商已恢复。')
  } catch (error) { if (error !== 'cancel') showError(error, archived ? '归档失败。' : '恢复失败。') }
}

function changeSort({ prop, order }: { prop: string | null; order: string | null }) {
  sortBy.value = prop || 'updatedAt'
  sortDescending.value = order !== 'ascending'
  load()
}

function showError(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    if (error.status === 409) { ElMessage.error('该供应商资料已被其他用户修改，请刷新后重新操作。'); return }
    const validation = Object.values(error.problem?.errors ?? {}).flat()[0]
    ElMessage.error(validation ?? error.problem?.detail ?? fallback)
    return
  }
  ElMessage.error(fallback)
}
</script>

<template>
  <section class="module-page">
    <div class="compact-page-actions finance-toolbar">
      <el-input
        v-model="search"
        clearable
        placeholder="编号、名称、联系人"
        @keyup.enter="page = 1; load()"
      >
        <template #prefix>
          <el-icon><Search /></el-icon>
        </template>
      </el-input>
      <el-input
        v-model="category"
        clearable
        placeholder="主营类别"
        @keyup.enter="page = 1; load()"
      />
      <el-select
        v-model="status"
        clearable
        placeholder="状态"
        @change="page = 1; load()"
      >
        <el-option
          label="启用"
          value="Active"
        /><el-option
          label="停用"
          value="Inactive"
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
      </el-button>
      <el-button
        v-if="canManage"
        type="primary"
        :icon="Plus"
        @click="openCreate"
      >
        新增供应商
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
        label="供应商编号"
        width="150"
        sortable="custom"
      />
      <el-table-column
        prop="name"
        label="供应商名称"
        min-width="260"
        sortable="custom"
      />
      <el-table-column
        prop="category"
        label="主营类别"
        min-width="140"
      />
      <el-table-column
        prop="contactName"
        label="联系人"
        width="120"
      />
      <el-table-column
        prop="contactMethod"
        label="手机号/电话"
        min-width="160"
      />
      <el-table-column
        label="累计采购"
        width="150"
        align="right"
        prop="totalPurchased"
        sortable="custom"
      >
        <template #default="scope">
          {{ money(scope.row.totalPurchased) }}
        </template>
      </el-table-column>
      <el-table-column
        label="累计实付"
        width="150"
        align="right"
      >
        <template #default="scope">
          {{ money(scope.row.totalPaid) }}
        </template>
      </el-table-column>
      <el-table-column
        label="当前未付"
        width="150"
        align="right"
        prop="outstandingPayable"
        sortable="custom"
      >
        <template #default="scope">
          <span :class="{ 'finance-negative': scope.row.outstandingPayable > 0 }">{{ money(scope.row.outstandingPayable) }}</span>
        </template>
      </el-table-column>
      <el-table-column
        label="状态"
        width="110"
      >
        <template #default="scope">
          <el-tag :type="scope.row.isArchived ? 'info' : scope.row.status === 'Active' ? 'success' : 'warning'">
            {{ scope.row.isArchived ? '已归档' : scope.row.status === 'Active' ? '启用' : '停用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column
        label="操作"
        width="90"
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
      size="min(900px, 96vw)"
      destroy-on-close
    >
      <template #header>
        <div v-if="selected">
          <strong>{{ selected.code }} · {{ selected.name }}</strong><el-tag
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
            :disabled="selected.isArchived"
            @click="openEdit"
          >
            编辑
          </el-button><el-button
            :type="selected.isArchived ? 'primary' : 'danger'"
            plain
            @click="toggleArchive"
          >
            {{ selected.isArchived ? '恢复' : '归档' }}
          </el-button>
        </div>
        <div class="finance-card-grid finance-card-grid-six">
          <div><span>累计采购</span><strong>{{ money(selected.totalPurchased) }}</strong></div><div><span>累计应付</span><strong>{{ money(selected.totalPayable) }}</strong></div><div><span>累计付款</span><strong>{{ money(selected.totalPaid) }}</strong></div><div><span>已核销付款</span><strong>{{ money(selected.allocatedPaid) }}</strong></div><div><span>未核销付款</span><strong>{{ money(selected.unallocatedPaid) }}</strong></div><div><span>当前未付</span><strong>{{ money(selected.outstandingPayable) }}</strong></div><div><span>当前逾期</span><strong class="finance-negative">{{ money(selected.overduePayable) }}</strong></div><div><span>未到期应付</span><strong>{{ money(selected.notDuePayable) }}</strong></div>
        </div>
        <el-tabs>
          <el-tab-pane label="概览">
            <el-descriptions
              :column="2"
              border
            >
              <el-descriptions-item label="简称">
                {{ selected.shortName || '—' }}
              </el-descriptions-item><el-descriptions-item label="主营类别">
                {{ selected.category || '—' }}
              </el-descriptions-item><el-descriptions-item label="联系人">
                {{ selected.contactName || '—' }}
              </el-descriptions-item><el-descriptions-item label="手机">
                {{ selected.mobile || '—' }}
              </el-descriptions-item><el-descriptions-item label="电话">
                {{ selected.phone || '—' }}
              </el-descriptions-item><el-descriptions-item label="邮箱">
                {{ selected.email || '—' }}
              </el-descriptions-item><el-descriptions-item label="账期">
                {{ selected.creditDays }} 天
              </el-descriptions-item><el-descriptions-item label="付款条件">
                {{ selected.paymentTerms || '—' }}
              </el-descriptions-item><el-descriptions-item
                label="地址"
                :span="2"
              >
                {{ [selected.province, selected.city, selected.address].filter(Boolean).join(' ') || '—' }}
              </el-descriptions-item><el-descriptions-item label="开户行">
                {{ selected.bankName || '—' }}
              </el-descriptions-item><el-descriptions-item label="账户名">
                {{ selected.bankAccountName || '—' }}
              </el-descriptions-item><el-descriptions-item
                label="备注"
                :span="2"
              >
                {{ selected.remark || '—' }}
              </el-descriptions-item>
            </el-descriptions>
          </el-tab-pane>
          <el-tab-pane label="采购记录">
            <el-table
              :data="supplierPurchases"
              stripe
            >
              <el-table-column
                prop="code"
                label="采购单号"
                width="155"
              />
              <el-table-column
                prop="projectName"
                label="项目"
                min-width="200"
              />
              <el-table-column
                prop="orderDate"
                label="下单日"
                width="110"
              />
              <el-table-column
                label="状态"
                width="110"
              >
                <template #default="scope">
                  {{ purchaseStatusLabels[scope.row.status as PurchaseOrderStatus] }}
                </template>
              </el-table-column>
              <el-table-column
                label="金额"
                width="140"
                align="right"
              >
                <template #default="scope">
                  {{ money(scope.row.totalAmount) }}
                </template>
              </el-table-column>
              <el-table-column
                label="收货进度"
                width="160"
              >
                <template #default="scope">
                  <el-progress :percentage="scope.row.receiptProgress" />
                </template>
              </el-table-column>
            </el-table>
            <el-empty
              v-if="supplierPurchases.length === 0"
              description="暂无采购记录"
            />
          </el-tab-pane>
          <el-tab-pane label="应付账款">
            <el-table
              :data="supplierPayables"
              stripe
              table-layout="fixed"
            >
              <el-table-column
                prop="code"
                label="应付编号"
                width="145"
              /><el-table-column
                prop="projectName"
                label="项目"
                min-width="180"
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
                label="已付"
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
            </el-table>
            <el-empty
              v-if="supplierPayables.length === 0"
              description="暂无应付账款"
            />
          </el-tab-pane>
          <el-tab-pane label="付款记录">
            <el-table
              :data="supplierPayments"
              stripe
              table-layout="fixed"
            >
              <el-table-column
                prop="code"
                label="付款编号"
                width="145"
              /><el-table-column
                prop="paymentDate"
                label="付款日期"
                width="110"
              /><el-table-column
                prop="amount"
                label="付款金额"
                width="130"
                align="right"
              >
                <template #default="scope">
                  {{ money(scope.row.amount) }}
                </template>
              </el-table-column><el-table-column
                prop="allocatedAmount"
                label="已核销"
                width="125"
                align="right"
              >
                <template #default="scope">
                  {{ money(scope.row.allocatedAmount) }}
                </template>
              </el-table-column><el-table-column
                prop="unallocatedAmount"
                label="未核销"
                width="125"
                align="right"
              >
                <template #default="scope">
                  {{ money(scope.row.unallocatedAmount) }}
                </template>
              </el-table-column><el-table-column
                prop="paymentMethod"
                label="付款方式"
                width="105"
              >
                <template #default="scope">
                  {{ paymentMethodLabels[scope.row.paymentMethod as PaymentMethod] }}
                </template>
              </el-table-column><el-table-column
                prop="payeeName"
                label="收款方"
                min-width="170"
              /><el-table-column
                prop="bankReference"
                label="银行参考号"
                min-width="170"
              />
            </el-table>
            <el-empty
              v-if="supplierPayments.length === 0"
              description="暂无付款记录"
            />
          </el-tab-pane>
          <el-tab-pane label="附件">
            <EntityAttachmentsPanel
              entity-type="Supplier"
              :entity-id="selected.id"
            />
          </el-tab-pane>
          <el-tab-pane
            v-if="canViewAudit"
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
              v-if="audits.length === 0"
              description="暂无操作记录"
            />
          </el-tab-pane>
        </el-tabs>
      </template>
    </el-drawer>

    <el-dialog
      v-model="formVisible"
      draggable
      :title="editing ? '编辑供应商' : '新增供应商'"
      width="760px"
      destroy-on-close
    >
      <el-form label-position="top">
        <el-row :gutter="16">
          <el-col :span="16">
            <el-form-item
              label="供应商名称"
              required
            >
              <el-input
                v-model="form.name"
                maxlength="200"
              />
            </el-form-item>
          </el-col><el-col :span="8">
            <el-form-item label="简称">
              <el-input
                v-model="form.shortName"
                maxlength="100"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="主营类别">
              <el-input
                v-model="form.category"
                maxlength="100"
              />
            </el-form-item>
          </el-col><el-col :span="12">
            <el-form-item label="状态">
              <el-select v-model="form.status">
                <el-option
                  label="启用"
                  value="Active"
                /><el-option
                  label="停用"
                  value="Inactive"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="联系人">
              <el-input v-model="form.contactName" />
            </el-form-item>
          </el-col><el-col :span="8">
            <el-form-item label="手机">
              <el-input v-model="form.mobile" />
            </el-form-item>
          </el-col><el-col :span="8">
            <el-form-item label="电话">
              <el-input v-model="form.phone" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="邮箱">
              <el-input v-model="form.email" />
            </el-form-item>
          </el-col><el-col :span="12">
            <el-form-item label="微信">
              <el-input v-model="form.weChat" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="16">
          <el-col :span="6">
            <el-form-item label="省">
              <el-input v-model="form.province" />
            </el-form-item>
          </el-col><el-col :span="6">
            <el-form-item label="市">
              <el-input v-model="form.city" />
            </el-form-item>
          </el-col><el-col :span="12">
            <el-form-item label="地址">
              <el-input v-model="form.address" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="账期天数">
              <el-input-number
                v-model="form.creditDays"
                :min="0"
                :max="3650"
              />
            </el-form-item>
          </el-col><el-col :span="16">
            <el-form-item label="付款条件">
              <el-input v-model="form.paymentTerms" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="开户行">
              <el-input v-model="form.bankName" />
            </el-form-item>
          </el-col><el-col :span="12">
            <el-form-item label="银行账户名">
              <el-input v-model="form.bankAccountName" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="备注">
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
