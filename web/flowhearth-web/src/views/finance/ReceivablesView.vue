<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

import * as customerApi from '../../api/customers'
import * as financeApi from '../../api/finance'
import { ApiError } from '../../api/problem-details'
import * as projectApi from '../../api/projects'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { CustomerSummary } from '../../types/customers'
import type { ReceivableDetails, ReceivableInput, ReceivableSummary, ReceivableType, ReceivableUpdateInput } from '../../types/finance'
import type { ProjectSummary } from '../../types/projects'

const auth = useAuthStore()
const route = useRoute()
const settings = useSettingsStore()
const canManage = computed(() => auth.canAny([permissions.receivablesManage]))
const loading = ref(false)
const saving = ref(false)
const formVisible = ref(false)
const detailVisible = ref(false)
const editing = ref(false)
const items = ref<ReceivableSummary[]>([])
const selected = ref<ReceivableDetails>()
const customers = ref<CustomerSummary[]>([])
const projects = ref<ProjectSummary[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(settings.defaultPageSize)
const search = ref('')
const customerId = ref<number>()
const projectId = ref<number>()
const status = ref('')
const receivableType = ref('')
const dueRange = ref<[string, string]>()
const overdueOnly = ref(false)
const archive = ref('active')
const sortBy = ref('updatedAt')
const sortDescending = ref(true)
const form = reactive<ReceivableInput & { version?: number }>({ customerId: 0, projectId: 0, title: '', receivableType: 'AdvancePayment', amount: 0, dueDate: '' })
const typeLabels: Record<ReceivableType, string> = { AdvancePayment: '预付款', ShipmentPayment: '发货款', AcceptancePayment: '验收款', WarrantyRetention: '质保金', ProgressPayment: '进度款', Other: '其它' }
const statusLabels: Record<string, string> = { NotDue: '未收', PartiallyPaid: '部分收款', Paid: '已收清', Overdue: '未收逾期', PartiallyOverdue: '部分逾期' }
const money = (value: number) => new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY' }).format(value)

onMounted(async () => {
  await Promise.all([loadLookups(), load()])
  await openFromRoute(route.query.entityId)
})
watch(() => route.query.entityId, openFromRoute)

async function openFromRoute(value: unknown) {
  const raw = Array.isArray(value) ? value[0] : value
  const id = Number(raw)
  if (Number.isSafeInteger(id) && id > 0 && selected.value?.id !== id) await openDetails(id)
}

async function loadLookups() {
  const [customerPage, projectPage] = await Promise.all([
    customerApi.listCustomers({ page: 1, pageSize: 100, archive: 'active', sortBy: 'name', sortDescending: false }),
    projectApi.listProjects({ page: 1, pageSize: 100, archive: 'active', sortBy: 'updatedAt', sortDescending: true }),
  ])
  customers.value = customerPage.items
  projects.value = projectPage.items
}

async function load() {
  loading.value = true
  try {
    const result = await financeApi.listReceivables({ page: page.value, pageSize: pageSize.value, search: search.value.trim() || undefined, customerId: customerId.value, projectId: projectId.value, status: status.value || undefined, receivableType: receivableType.value || undefined, dueFrom: dueRange.value?.[0], dueTo: dueRange.value?.[1], overdueOnly: overdueOnly.value || undefined, archive: archive.value, sortBy: sortBy.value, sortDescending: sortDescending.value })
    items.value = result.items
    total.value = result.total
  } catch (error) { showError(error, '应收列表加载失败。') } finally { loading.value = false }
}

async function openDetails(id: number) {
  loading.value = true
  try { selected.value = await financeApi.getReceivable(id); detailVisible.value = true } catch (error) { showError(error, '应收详情加载失败。') } finally { loading.value = false }
}

function openCreate() {
  editing.value = false
  Object.assign(form, { customerId: customerId.value ?? 0, projectId: projectId.value ?? 0, title: '', receivableType: 'AdvancePayment', amount: 0, dueDate: '', description: undefined, remark: undefined, version: undefined })
  formVisible.value = true
}

function openEdit() {
  if (!selected.value) return
  editing.value = true
  Object.assign(form, { ...selected.value })
  formVisible.value = true
}

async function save() {
  if (!form.customerId || !form.projectId || !form.title.trim() || !form.dueDate || form.amount <= 0) { ElMessage.warning('请完整填写客户、项目、标题、金额和到期日。'); return }
  saving.value = true
  try {
    const saved = editing.value && selected.value
      ? await financeApi.updateReceivable(selected.value.id, { title: form.title.trim(), receivableType: form.receivableType, amount: form.amount, dueDate: form.dueDate, description: form.description, remark: form.remark, version: form.version! } satisfies ReceivableUpdateInput)
      : await financeApi.createReceivable({ ...form, title: form.title.trim() })
    selected.value = saved
    formVisible.value = false
    detailVisible.value = true
    await load()
    ElMessage.success(editing.value ? '应收账款已更新。' : '应收账款已创建。')
  } catch (error) { showError(error, '应收账款保存失败。') } finally { saving.value = false }
}

async function toggleArchive() {
  if (!selected.value) return
  const archived = !selected.value.isArchived
  try {
    await ElMessageBox.confirm(archived ? '确认归档该应收？存在有效核销时系统会拒绝。' : '确认恢复该应收？', archived ? '归档应收' : '恢复应收', { type: archived ? 'warning' : 'info' })
    selected.value = await financeApi.setReceivableArchived(selected.value.id, archived, selected.value.version)
    await load()
    ElMessage.success(archived ? '应收已归档。' : '应收已恢复。')
  } catch (error) { if (error !== 'cancel') showError(error, archived ? '归档失败。' : '恢复失败。') }
}

async function cancelAllocation(allocationId: number, version: number) {
  if (!selected.value) return
  try {
    await ElMessageBox.confirm('取消核销后收款余额和应收状态会重新计算，确认继续？', '取消核销', { type: 'warning' })
    const allocation = selected.value.allocations.find(item => item.id === allocationId)
    await financeApi.cancelReceiptAllocation(allocation!.receiptId, allocationId, version)
    selected.value = await financeApi.getReceivable(selected.value.id)
    await load()
    ElMessage.success('核销已取消。')
  } catch (error) { if (error !== 'cancel') showError(error, '取消核销失败。') }
}

function changeSort({ prop, order }: { prop: string | null; order: string | null }) { sortBy.value = prop || 'updatedAt'; sortDescending.value = order !== 'ascending'; load() }
function statusTag(value: string) { return value === 'Paid' ? 'success' : value.includes('Overdue') ? 'danger' : 'warning' }
function showError(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    if (error.status === 409) { ElMessage.error('财务数据已被其他用户修改，请刷新后重新操作。'); return }
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
        placeholder="应收编号、客户、项目"
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
        v-model="receivableType"
        clearable
        placeholder="应收类型"
        @change="page = 1; load()"
      >
        <el-option
          v-for="(label, value) in typeLabels"
          :key="value"
          :label="label"
          :value="value"
        />
      </el-select>
      <el-select
        v-model="status"
        clearable
        placeholder="状态"
        @change="page = 1; load()"
      >
        <el-option
          v-for="(label, value) in statusLabels"
          :key="value"
          :label="label"
          :value="value"
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
      <el-checkbox
        v-model="overdueOnly"
        @change="page = 1; load()"
      >
        只看逾期
      </el-checkbox>
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
        新增应收
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
        label="应收编号"
        width="145"
        sortable="custom"
      />
      <el-table-column
        prop="customerName"
        label="客户"
        min-width="220"
      />
      <el-table-column
        label="项目"
        min-width="220"
      >
        <template #default="scope">
          {{ scope.row.projectCode }} · {{ scope.row.projectName }}
        </template>
      </el-table-column>
      <el-table-column
        label="类型"
        width="110"
      >
        <template #default="scope">
          {{ typeLabels[scope.row.receivableType as ReceivableType] }}
        </template>
      </el-table-column>
      <el-table-column
        label="应收金额"
        width="145"
        align="right"
        prop="amount"
        sortable="custom"
      >
        <template #default="scope">
          {{ money(scope.row.amount) }}
        </template>
      </el-table-column>
      <el-table-column
        label="已收"
        width="135"
        align="right"
      >
        <template #default="scope">
          {{ money(scope.row.allocatedAmount) }}
        </template>
      </el-table-column>
      <el-table-column
        label="未收"
        width="135"
        align="right"
        prop="outstandingAmount"
        sortable="custom"
      >
        <template #default="scope">
          <span :class="{ 'finance-negative': scope.row.outstandingAmount > 0 }">{{ money(scope.row.outstandingAmount) }}</span>
        </template>
      </el-table-column>
      <el-table-column
        prop="dueDate"
        label="到期日"
        width="120"
        sortable="custom"
      />
      <el-table-column
        label="状态"
        width="115"
      >
        <template #default="scope">
          <el-tag :type="statusTag(scope.row.status)">
            {{ statusLabels[scope.row.status] }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column
        label="逾期天数"
        width="100"
        align="right"
      >
        <template #default="scope">
          <span :class="{ 'finance-negative': scope.row.overdueDays > 0 }">{{ scope.row.overdueDays || '—' }}</span>
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
        <strong v-if="selected">{{ selected.code }} · {{ selected.title }}</strong>
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
        <div class="finance-card-grid">
          <div><span>应收金额</span><strong>{{ money(selected.amount) }}</strong></div><div><span>已核销</span><strong>{{ money(selected.allocatedAmount) }}</strong></div><div><span>未收金额</span><strong>{{ money(selected.outstandingAmount) }}</strong></div><div><span>逾期天数</span><strong>{{ selected.overdueDays || '—' }}</strong></div>
        </div>
        <el-descriptions
          :column="2"
          border
        >
          <el-descriptions-item label="客户">
            {{ selected.customerCode }} · {{ selected.customerName }}
          </el-descriptions-item><el-descriptions-item label="项目">
            {{ selected.projectCode }} · {{ selected.projectName }}
          </el-descriptions-item><el-descriptions-item label="类型">
            {{ typeLabels[selected.receivableType] }}
          </el-descriptions-item><el-descriptions-item label="状态">
            <el-tag :type="statusTag(selected.status)">
              {{ statusLabels[selected.status] }}
            </el-tag>
          </el-descriptions-item><el-descriptions-item label="到期日">
            {{ selected.dueDate }}
          </el-descriptions-item><el-descriptions-item label="合同金额">
            {{ money(selected.projectContractAmount) }}
          </el-descriptions-item><el-descriptions-item
            label="备注"
            :span="2"
          >
            {{ selected.remark || '—' }}
          </el-descriptions-item>
        </el-descriptions>
        <h3 class="finance-section-title">
          核销记录
        </h3>
        <el-table
          :data="selected.allocations"
          stripe
        >
          <el-table-column
            prop="receiptCode"
            label="收款编号"
            width="145"
          /><el-table-column
            prop="receiptDate"
            label="收款日期"
            width="115"
          /><el-table-column
            label="收款金额"
            width="140"
            align="right"
          >
            <template #default="scope">
              {{ money(scope.row.receiptAmount) }}
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
            prop="createdByDisplayName"
            label="操作人"
            min-width="110"
          /><el-table-column
            prop="createdAtUtc"
            label="核销时间"
            min-width="170"
          /><el-table-column
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
          description="暂无核销记录；请从收款详情执行核销"
        />
      </template>
    </el-drawer>

    <el-dialog
      v-model="formVisible"
      draggable
      :title="editing ? '编辑应收账款' : '新增应收账款'"
      width="680px"
      destroy-on-close
    >
      <el-form label-position="top">
        <el-row :gutter="16">
          <el-col :span="12">
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
            </el-form-item>
          </el-col><el-col :span="12">
            <el-form-item
              label="项目"
              required
            >
              <el-select
                v-model="form.projectId"
                filterable
                :disabled="editing"
              >
                <el-option
                  v-for="item in projects.filter(project => !form.customerId || project.customerId === form.customerId)"
                  :key="item.id"
                  :label="`${item.code} · ${item.name}`"
                  :value="item.id"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item
          label="应收标题"
          required
        >
          <el-input
            v-model="form.title"
            maxlength="200"
          />
        </el-form-item>
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="类型">
              <el-select v-model="form.receivableType">
                <el-option
                  v-for="(label, value) in typeLabels"
                  :key="value"
                  :label="label"
                  :value="value"
                />
              </el-select>
            </el-form-item>
          </el-col><el-col :span="8">
            <el-form-item
              label="金额"
              required
            >
              <el-input-number
                v-model="form.amount"
                :min="0.01"
                :precision="2"
                :step="1000"
              />
            </el-form-item>
          </el-col><el-col :span="8">
            <el-form-item
              label="到期日"
              required
            >
              <el-date-picker
                v-model="form.dueDate"
                type="date"
                value-format="YYYY-MM-DD"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <el-alert
          v-if="editing && selected?.allocatedAmount"
          :title="`已核销 ${money(selected.allocatedAmount)}；新金额不得低于已核销金额。`"
          type="warning"
          :closable="false"
        />
        <el-form-item label="说明">
          <el-input
            v-model="form.description"
            type="textarea"
            :rows="2"
          />
        </el-form-item><el-form-item label="备注">
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
