<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { listAuditLogs } from '../../api/audit'
import * as financeApi from '../../api/finance'
import { ApiError } from '../../api/problem-details'
import { listCustomers } from '../../api/customers'
import { listProjects } from '../../api/projects'
import EntityAttachmentsPanel from '../../components/attachments/EntityAttachmentsPanel.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { AuditLogSummary } from '../../types/audit'
import type { CustomerSummary } from '../../types/customers'
import type { ShipmentDetails, ShipmentEquipmentCandidate, ShipmentInput, ShipmentStatus, ShipmentSummary } from '../../types/finance'
import type { ProjectSummary } from '../../types/projects'
import { appendShipmentEquipmentItems, canArchiveShipment, canEditShipment, filterShipmentEquipmentCandidates, removeShipmentItem, resolveShipmentProject, shipmentStatusLabels, shipmentStatusType, validateShipmentItems } from '../../utils/shipment'

const auth = useAuthStore()
const settings = useSettingsStore()
const route = useRoute()
const router = useRouter()
const canManage = computed(() => auth.canAny([permissions.shipmentsManage]))
const canAudit = computed(() => auth.canAny([permissions.auditView]))
const loading = ref(false)
const saving = ref(false)
const rows = ref<ShipmentSummary[]>([])
const customers = ref<CustomerSummary[]>([])
const projects = ref<ProjectSummary[]>([])
const equipmentCandidates = ref<ShipmentEquipmentCandidate[]>([])
const equipmentSelection = ref<number[]>([])
const selected = ref<ShipmentDetails>()
const audits = ref<AuditLogSummary[]>([])
const detailVisible = ref(false)
const formVisible = ref(false)
const receiveVisible = ref(false)
const editing = ref(false)
const page = ref(1)
const pageSize = ref(settings.defaultPageSize)
const total = ref(0)
const sortBy = ref('shipmentDate')
const sortDescending = ref(true)
const filters = reactive({ search: '', customerId: undefined as number | undefined, projectId: undefined as number | undefined, status: '' as '' | ShipmentStatus, archive: 'active', isReceived: undefined as boolean | undefined, dates: [] as string[] })
const form = reactive<ShipmentInput & { version?: number }>({ customerId: 0, projectId: 0, shipmentDate: today(), shippingAddress: '', items: [] })
const receiveForm = reactive({ signedAt: localDateTime(), receiverName: '', remark: '' })

const filteredProjects = computed(() => projects.value.filter(project => !form.customerId || project.customerId === form.customerId))
const filterProjects = computed(() => projects.value.filter(project => !filters.customerId || project.customerId === filters.customerId))
const selectableEquipment = computed(() => filterShipmentEquipmentCandidates(equipmentCandidates.value, form.items))
const canEditSelected = computed(() => !!selected.value && !selected.value.isArchived && canEditShipment(selected.value.status))

onMounted(async () => {
  await loadOptions()
  const routeCustomerId = Number(route.query.customerId)
  const routeProjectId = Number(route.query.projectId)
  if (customers.value.some(item => item.id === routeCustomerId)) filters.customerId = routeCustomerId
  const routeProject = resolveShipmentProject(projects.value, routeProjectId)
  if (routeProject) {
    filters.projectId = routeProject.projectId
    filters.customerId = routeProject.customerId
  }
  await load()
  const id = Number(route.query.entityId)
  if (id) await openDetails(id)
  if (route.query.create === '1') openCreateFromRoute()
})
watch(() => route.query.entityId, async value => { const id = Number(value); if (id && selected.value?.id !== id) await openDetails(id) })

function today() { const date = new Date(); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 10) }
function localDateTime() { const date = new Date(); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16) }
function displayDateTime(value?: string | null) { return value ? new Date(value).toLocaleString('zh-CN') : '—' }

async function loadOptions() {
  try {
    const [customerPage, projectPage] = await Promise.all([
      listCustomers({ page: 1, pageSize: 100, archive: 'active', sortBy: 'name', sortDescending: false }),
      listProjects({ page: 1, pageSize: 100, archive: 'active', sortBy: 'updatedAt', sortDescending: true }),
    ])
    customers.value = customerPage.items
    projects.value = projectPage.items
  } catch { ElMessage.error('客户或项目选项加载失败。') }
}

async function load() {
  loading.value = true
  try {
    const result = await financeApi.listShipments({ page: page.value, pageSize: pageSize.value, search: filters.search.trim() || undefined, customerId: filters.customerId, projectId: filters.projectId, status: filters.status || undefined, archive: filters.archive, isReceived: filters.isReceived, shipmentFrom: filters.dates[0], shipmentTo: filters.dates[1], sortBy: sortBy.value, sortDescending: sortDescending.value })
    rows.value = result.items
    total.value = result.total
  } catch (error) { showError(error, '出货单列表加载失败。') } finally { loading.value = false }
}

async function openDetails(id: number) {
  loading.value = true
  try {
    selected.value = await financeApi.getShipment(id)
    audits.value = []
    if (canAudit.value) {
      const result = await listAuditLogs({ page: 1, pageSize: 100, entityType: 'shipment', search: selected.value.code, sortBy: 'occurredAt', sortDescending: true })
      audits.value = result.items.filter(item => item.entityId === id)
    }
    detailVisible.value = true
    if (String(route.query.entityId ?? '') !== String(id)) await router.replace({ query: { ...route.query, entityId: String(id), create: undefined } })
  } catch (error) { showError(error, '出货单详情加载失败。') } finally { loading.value = false }
}

function closeDetails() { void router.replace({ query: { ...route.query, entityId: undefined } }) }
function resetForm() { Object.assign(form, { customerId: 0, projectId: 0, shipmentDate: today(), receiverName: undefined, receiverMobile: undefined, logisticsCompany: undefined, trackingNumber: undefined, shippingAddress: '', remark: undefined, items: [], version: undefined }); equipmentCandidates.value = []; equipmentSelection.value = [] }
function openCreate() { editing.value = false; resetForm(); formVisible.value = true }
function openCreateFromRoute() {
  openCreate()
  const projectId = Number(route.query.projectId)
  const project = resolveShipmentProject(projects.value, projectId)
  if (project) { form.projectId = project.projectId; form.customerId = project.customerId; void loadEquipment() }
}
function openEdit() {
  if (!selected.value) return
  editing.value = true
  Object.assign(form, { customerId: selected.value.customerId, projectId: selected.value.projectId, shipmentDate: selected.value.shipmentDate, receiverName: selected.value.receiverName, receiverMobile: selected.value.receiverMobile, logisticsCompany: selected.value.logisticsCompany, trackingNumber: selected.value.trackingNumber, shippingAddress: selected.value.shippingAddress, remark: selected.value.remark, version: selected.value.version, items: selected.value.items.map(item => ({ id: item.id, equipmentId: item.equipmentId, itemName: item.itemName, manufacturer: item.manufacturer, model: item.model, quantity: item.quantity, unit: item.unit, remark: item.remark })) })
  formVisible.value = true
  void loadEquipment()
}
function customerChanged() { form.projectId = 0; form.items = []; equipmentCandidates.value = [] }
function filterCustomerChanged() { filters.projectId = undefined; page.value = 1; void load() }
async function projectChanged() { form.items = form.items.filter(item => !item.equipmentId); await loadEquipment() }
async function loadEquipment() {
  equipmentSelection.value = []
  if (!form.projectId) { equipmentCandidates.value = []; return }
  try { equipmentCandidates.value = await financeApi.listShipmentEquipmentCandidates(form.projectId) } catch { ElMessage.error('项目设备加载失败。') }
}
function addSelectedEquipment() {
  form.items = appendShipmentEquipmentItems(form.items, equipmentCandidates.value, equipmentSelection.value)
  equipmentSelection.value = []
}
function addManualItem() { form.items.push({ itemName: '', quantity: 1, unit: '件' }) }
function removeItem(index: number) { form.items = removeShipmentItem(form.items, index) }

async function save() {
  if (!form.customerId || !form.projectId || !form.shipmentDate || !form.shippingAddress.trim()) { ElMessage.warning('请选择客户和项目，并填写出货日期、收货地址。'); return }
  const itemError = validateShipmentItems(form.items)
  if (itemError) { ElMessage.warning(itemError); return }
  saving.value = true
  try {
    const input = { ...form, shippingAddress: form.shippingAddress.trim(), items: form.items.map(item => ({ ...item, itemName: item.itemName.trim(), unit: item.unit.trim() })) }
    const saved = editing.value && selected.value ? await financeApi.updateShipment(selected.value.id, input as ShipmentInput & { version: number }) : await financeApi.createShipment(input)
    selected.value = saved
    formVisible.value = false
    detailVisible.value = true
    await load()
    await router.replace({ query: { entityId: String(saved.id) } })
    ElMessage.success(editing.value ? '出货单已更新。' : '出货单草稿已创建。')
  } catch (error) { showError(error, '出货单保存失败。') } finally { saving.value = false }
}

async function transition(action: 'ship' | 'in-transit' | 'cancel') {
  if (!selected.value) return
  const prompts = { ship: '确认实际出货？出货明细将被锁定。', 'in-transit': '确认标记为运输中？', cancel: '确认取消这张尚未出货的出货单？' }
  try {
    await ElMessageBox.confirm(prompts[action], '出货状态操作', { type: 'warning' })
    selected.value = await financeApi.transitionShipment(selected.value.id, action, selected.value.version)
    await load()
    ElMessage.success('出货状态已更新。')
  } catch (error) { if (error !== 'cancel') showError(error, '出货状态更新失败。') }
}
function openReceive() { if (!selected.value) return; receiveForm.signedAt = localDateTime(); receiveForm.receiverName = selected.value.receiverName ?? ''; receiveForm.remark = ''; receiveVisible.value = true }
async function saveReceive() {
  if (!selected.value || !receiveForm.signedAt) { ElMessage.warning('签收时间必填。'); return }
  saving.value = true
  try {
    selected.value = await financeApi.receiveShipment(selected.value.id, new Date(receiveForm.signedAt).toISOString(), receiveForm.receiverName.trim() || null, receiveForm.remark.trim() || null, selected.value.version)
    receiveVisible.value = false
    await load()
    ElMessage.success('客户签收已确认。')
  } catch (error) { showError(error, '确认签收失败。') } finally { saving.value = false }
}
async function toggleArchive() {
  if (!selected.value) return
  const archived = !selected.value.isArchived
  try {
    await ElMessageBox.confirm(archived ? '确认归档该出货单？' : '确认恢复该出货单？', archived ? '归档出货单' : '恢复出货单')
    selected.value = await financeApi.setShipmentArchived(selected.value.id, archived, selected.value.version)
    await load()
  } catch (error) { if (error !== 'cancel') showError(error, '归档操作失败。') }
}
function showError(error: unknown, fallback: string) { ElMessage.error(error instanceof ApiError ? error.message : fallback) }
</script>

<template>
  <section class="module-page shipment-page">
    <div class="module-toolbar">
      <div class="filter-row">
        <el-input
          v-model="filters.search"
          clearable
          placeholder="出货单号、客户、项目、运单号、收货人"
          class="search-input"
          @keyup.enter="page = 1; load()"
        >
          <template #prefix>
            <el-icon><Search /></el-icon>
          </template>
        </el-input>
        <el-select
          v-model="filters.customerId"
          clearable
          filterable
          placeholder="客户"
          @change="filterCustomerChanged"
        >
          <el-option
            v-for="item in customers"
            :key="item.id"
            :label="item.name"
            :value="item.id"
          />
        </el-select>
        <el-select
          v-model="filters.projectId"
          clearable
          filterable
          placeholder="项目"
          @change="page = 1; load()"
        >
          <el-option
            v-for="item in filterProjects"
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
            v-for="(label, value) in shipmentStatusLabels"
            :key="value"
            :label="label"
            :value="value"
          />
        </el-select>
        <el-date-picker
          v-model="filters.dates"
          type="daterange"
          value-format="YYYY-MM-DD"
          start-placeholder="出货起日"
          end-placeholder="出货止日"
          @change="page = 1; load()"
        />
        <el-select
          v-model="filters.isReceived"
          clearable
          placeholder="签收情况"
          @change="page = 1; load()"
        >
          <el-option
            label="已签收"
            :value="true"
          /><el-option
            label="未签收"
            :value="false"
          />
        </el-select>
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
        <el-select
          v-model="sortBy"
          placeholder="排序字段"
          @change="page = 1; load()"
        >
          <el-option
            label="出货日期"
            value="shipmentDate"
          />
          <el-option
            label="创建时间"
            value="createdAt"
          />
          <el-option
            label="状态"
            value="status"
          />
          <el-option
            label="签收日期"
            value="signedAt"
          />
        </el-select>
        <el-select
          v-model="sortDescending"
          placeholder="排序方向"
          @change="page = 1; load()"
        >
          <el-option
            label="降序"
            :value="true"
          />
          <el-option
            label="升序"
            :value="false"
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
          新建出货单
        </el-button>
      </div>
    </div>

    <div class="table-shell shipment-table-shell">
      <el-table
        v-loading="loading"
        class="flowhearth-data-table"
        :data="rows"
        border
        stripe
        row-key="id"
        @row-click="row => openDetails(row.id)"
      >
        <el-table-column
          prop="code"
          label="出货单号"
          width="150"
          fixed
        />
        <el-table-column
          prop="customerName"
          label="客户"
          min-width="230"
          show-overflow-tooltip
        />
        <el-table-column
          label="项目"
          min-width="220"
        >
          <template #default="{ row }">
            <strong>{{ row.projectCode }}</strong><br><span class="muted">{{ row.projectName }}</span>
          </template>
        </el-table-column>
        <el-table-column
          prop="shipmentDate"
          label="出货日期"
          width="120"
        />
        <el-table-column
          label="状态"
          width="100"
        >
          <template #default="{ row }">
            <el-tag :type="shipmentStatusType(row.status)">
              {{ shipmentStatusLabels[row.status as ShipmentStatus] }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column
          prop="logisticsCompany"
          label="物流公司"
          min-width="140"
        />
        <el-table-column
          prop="trackingNumber"
          label="运单号"
          min-width="170"
          show-overflow-tooltip
        />
        <el-table-column
          prop="receiverName"
          label="收货人"
          width="120"
        />
        <el-table-column
          label="签收时间"
          width="180"
        >
          <template #default="{ row }">
            {{ displayDateTime(row.signedAtUtc) }}
          </template>
        </el-table-column>
        <el-table-column
          label="明细"
          width="105"
        >
          <template #default="{ row }">
            {{ row.itemCount }} 项 / {{ row.equipmentCount }} 台
          </template>
        </el-table-column>
      </el-table>
    </div>
    <el-pagination
      v-model:current-page="page"
      v-model:page-size="pageSize"
      layout="total, sizes, prev, pager, next"
      :total="total"
      :page-sizes="[10, 20, 50, 100]"
      @change="load"
    />

    <el-drawer
      v-model="detailVisible"
      size="min(1040px, 94vw)"
      destroy-on-close
      @closed="closeDetails"
    >
      <template #header>
        <div
          v-if="selected"
          class="detail-title"
        >
          <strong>{{ selected.code }}</strong><el-tag :type="shipmentStatusType(selected.status)">
            {{ shipmentStatusLabels[selected.status] }}
          </el-tag>
        </div>
      </template>
      <template v-if="selected">
        <div class="detail-actions">
          <el-button
            v-if="canManage && canEditSelected"
            @click="openEdit"
          >
            编辑
          </el-button>
          <el-button
            v-if="canManage && selected.status === 'Preparing' && !selected.isArchived"
            type="primary"
            @click="transition('ship')"
          >
            确认出货
          </el-button>
          <el-button
            v-if="canManage && selected.status === 'Shipped'"
            @click="transition('in-transit')"
          >
            进入运输
          </el-button>
          <el-button
            v-if="canManage && ['Shipped', 'InTransit'].includes(selected.status)"
            type="success"
            @click="openReceive"
          >
            确认签收
          </el-button>
          <el-button
            v-if="canManage && selected.status === 'Preparing' && !selected.isArchived"
            type="danger"
            plain
            @click="transition('cancel')"
          >
            取消
          </el-button>
          <el-button
            v-if="canManage && (selected.isArchived || canArchiveShipment(selected.status))"
            @click="toggleArchive"
          >
            {{ selected.isArchived ? '恢复' : '归档' }}
          </el-button>
        </div>
        <el-tabs>
          <el-tab-pane label="概览">
            <el-descriptions
              :column="2"
              border
            >
              <el-descriptions-item label="客户">
                {{ selected.customerCode }} · {{ selected.customerName }}
              </el-descriptions-item>
              <el-descriptions-item label="项目">
                {{ selected.projectCode }} · {{ selected.projectName }}
              </el-descriptions-item>
              <el-descriptions-item label="出货日期">
                {{ selected.shipmentDate }}
              </el-descriptions-item>
              <el-descriptions-item label="签收时间">
                {{ displayDateTime(selected.signedAtUtc) }}
              </el-descriptions-item>
              <el-descriptions-item label="物流公司">
                {{ selected.logisticsCompany || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="运单号">
                {{ selected.trackingNumber || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="收货人">
                {{ selected.receiverName || '—' }}
              </el-descriptions-item>
              <el-descriptions-item label="电话">
                {{ selected.receiverMobile || '—' }}
              </el-descriptions-item>
              <el-descriptions-item
                label="地址"
                :span="2"
              >
                {{ selected.shippingAddress }}
              </el-descriptions-item>
              <el-descriptions-item
                label="备注"
                :span="2"
              >
                {{ selected.remark || '—' }}
              </el-descriptions-item>
            </el-descriptions>
          </el-tab-pane>
          <el-tab-pane label="出货明细">
            <div class="table-shell">
              <el-table :data="selected.items">
                <el-table-column
                  label="类型"
                  width="90"
                >
                  <template #default="{ row }">
                    {{ row.equipmentId ? '项目设备' : '普通物料' }}
                  </template>
                </el-table-column><el-table-column
                  prop="itemName"
                  label="名称"
                  min-width="180"
                /><el-table-column
                  prop="equipmentCode"
                  label="设备编号"
                  width="150"
                /><el-table-column
                  prop="manufacturer"
                  label="品牌"
                  width="130"
                /><el-table-column
                  prop="model"
                  label="型号"
                  width="140"
                /><el-table-column
                  label="数量"
                  width="110"
                >
                  <template #default="{ row }">
                    {{ row.quantity }} {{ row.unit }}
                  </template>
                </el-table-column><el-table-column
                  prop="remark"
                  label="备注"
                  min-width="180"
                />
              </el-table>
            </div>
          </el-tab-pane>
          <el-tab-pane label="关联设备">
            <el-empty
              v-if="!selected.items.some(item => item.equipmentId)"
              description="没有关联项目设备"
            /><el-table
              v-else
              :data="selected.items.filter(item => item.equipmentId)"
            >
              <el-table-column
                prop="equipmentCode"
                label="设备编号"
                width="160"
              /><el-table-column
                prop="itemName"
                label="设备名称"
              /><el-table-column
                prop="manufacturer"
                label="品牌"
              /><el-table-column
                prop="model"
                label="型号"
              />
            </el-table>
          </el-tab-pane>
          <el-tab-pane label="附件">
            <EntityAttachmentsPanel
              entity-type="Shipment"
              :entity-id="selected.id"
              :can-manage="canManage"
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
                :timestamp="displayDateTime(item.occurredAtUtc)"
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
      :title="editing ? '编辑出货单' : '新建出货单'"
      width="min(1080px, 96vw)"
      destroy-on-close
    >
      <el-form label-width="92px">
        <div class="form-grid">
          <el-form-item
            label="客户"
            required
          >
            <el-select
              v-model="form.customerId"
              filterable
              :disabled="editing && selected?.status !== 'Preparing'"
              @change="customerChanged"
            >
              <el-option
                v-for="item in customers"
                :key="item.id"
                :label="item.name"
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
              :disabled="editing && selected?.status !== 'Preparing'"
              @change="projectChanged"
            >
              <el-option
                v-for="item in filteredProjects"
                :key="item.id"
                :label="`${item.code} · ${item.name}`"
                :value="item.id"
              />
            </el-select>
          </el-form-item>
          <el-form-item
            label="出货日期"
            required
          >
            <el-date-picker
              v-model="form.shipmentDate"
              value-format="YYYY-MM-DD"
              :disabled="editing && selected?.status !== 'Preparing'"
            />
          </el-form-item>
          <el-form-item label="收货人">
            <el-input
              v-model="form.receiverName"
              :disabled="editing && selected?.status === 'Received'"
            />
          </el-form-item>
          <el-form-item label="联系电话">
            <el-input
              v-model="form.receiverMobile"
              :disabled="editing && selected?.status === 'Received'"
            />
          </el-form-item>
          <el-form-item label="物流公司">
            <el-input
              v-model="form.logisticsCompany"
              :disabled="editing && selected?.status === 'Received'"
            />
          </el-form-item>
          <el-form-item label="运单号">
            <el-input
              v-model="form.trackingNumber"
              :disabled="editing && selected?.status === 'Received'"
            />
          </el-form-item>
          <el-form-item
            label="收货地址"
            required
            class="span-2"
          >
            <el-input
              v-model="form.shippingAddress"
              :disabled="editing && selected?.status === 'Received'"
            />
          </el-form-item>
          <el-form-item
            label="备注"
            class="span-2"
          >
            <el-input
              v-model="form.remark"
              type="textarea"
              :rows="2"
            />
          </el-form-item>
        </div>
        <template v-if="!editing || selected?.status === 'Preparing'">
          <div class="item-actions">
            <el-select
              v-model="equipmentSelection"
              multiple
              filterable
              collapse-tags
              placeholder="选择项目设备"
              class="equipment-picker"
            >
              <el-option
                v-for="item in selectableEquipment"
                :key="item.id"
                :label="`${item.code} · ${item.name}`"
                :value="item.id"
              />
            </el-select><el-button
              :disabled="!equipmentSelection.length"
              @click="addSelectedEquipment"
            >
              加入设备
            </el-button><el-button @click="addManualItem">
              添加普通出货项
            </el-button>
          </div>
          <div class="table-shell">
            <el-table :data="form.items">
              <el-table-column
                label="类型"
                width="90"
              >
                <template #default="{ row }">
                  {{ row.equipmentId ? '设备' : '物料' }}
                </template>
              </el-table-column><el-table-column
                label="名称"
                min-width="160"
              >
                <template #default="{ row }">
                  <el-input
                    v-model="row.itemName"
                    :disabled="!!row.equipmentId"
                  />
                </template>
              </el-table-column><el-table-column
                label="品牌"
                width="130"
              >
                <template #default="{ row }">
                  <el-input
                    v-model="row.manufacturer"
                    :disabled="!!row.equipmentId"
                  />
                </template>
              </el-table-column><el-table-column
                label="型号"
                width="130"
              >
                <template #default="{ row }">
                  <el-input
                    v-model="row.model"
                    :disabled="!!row.equipmentId"
                  />
                </template>
              </el-table-column><el-table-column
                label="数量"
                width="120"
              >
                <template #default="{ row }">
                  <el-input-number
                    v-model="row.quantity"
                    :min="0.0001"
                    :precision="4"
                    :controls="false"
                  />
                </template>
              </el-table-column><el-table-column
                label="单位"
                width="90"
              >
                <template #default="{ row }">
                  <el-input v-model="row.unit" />
                </template>
              </el-table-column><el-table-column
                label="备注"
                min-width="160"
              >
                <template #default="{ row }">
                  <el-input v-model="row.remark" />
                </template>
              </el-table-column><el-table-column
                width="76"
                fixed="right"
              >
                <template #default="{ $index }">
                  <el-button
                    link
                    type="danger"
                    @click="removeItem($index)"
                  >
                    删除
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </div>
        </template>
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
      v-model="receiveVisible"
      draggable
      title="确认客户签收"
      width="480px"
    >
      <el-form label-width="92px">
        <el-form-item
          label="签收时间"
          required
        >
          <el-date-picker
            v-model="receiveForm.signedAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm"
          />
        </el-form-item><el-form-item label="收货人">
          <el-input
            v-model="receiveForm.receiverName"
            placeholder="无法确认时可留空"
          />
        </el-form-item><el-form-item label="签收备注">
          <el-input
            v-model="receiveForm.remark"
            type="textarea"
            :rows="3"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="receiveVisible = false">
          取消
        </el-button><el-button
          type="success"
          :loading="saving"
          @click="saveReceive"
        >
          确认签收
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.shipment-page { min-width: 0; }
.filter-row, .item-actions, .detail-actions, .detail-title { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.search-input { width: min(420px, 100%); min-width: 360px; }
.shipment-table-shell, .table-shell { width: 100%; overflow-x: auto; }
.shipment-table-shell :deep(.el-table) { min-width: 1640px; }
.form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.span-2 { grid-column: span 2; }
.equipment-picker { width: min(520px, 60vw); }
.item-actions { margin: 8px 0 14px; }
.detail-actions { margin-bottom: 14px; }
.detail-title strong { font-size: 18px; }
.muted { color: var(--el-text-color-secondary); }
@media (max-width: 760px) {
  .form-grid { grid-template-columns: 1fr; }
  .span-2 { grid-column: auto; }
  .search-input, .equipment-picker { width: 100%; min-width: 0; }
}
</style>
