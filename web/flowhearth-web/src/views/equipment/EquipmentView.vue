<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import * as customerApi from '../../api/customers'
import * as equipmentApi from '../../api/equipment'
import { ApiError } from '../../api/problem-details'
import * as projectApi from '../../api/projects'
import EquipmentComponentDialog from '../../components/equipment/EquipmentComponentDialog.vue'
import EquipmentDetailDrawer from '../../components/equipment/EquipmentDetailDrawer.vue'
import EquipmentFormDialog from '../../components/equipment/EquipmentFormDialog.vue'
import EquipmentParameterDialog from '../../components/equipment/EquipmentParameterDialog.vue'
import EquipmentVersionDialog from '../../components/equipment/EquipmentVersionDialog.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { CustomerSummary } from '../../types/customers'
import type { EquipmentCategory, EquipmentComponentDetails, EquipmentComponentInput, EquipmentDetails, EquipmentInput, EquipmentParameterDetails, EquipmentParameterInput, EquipmentSummary, EquipmentVersionDetails, EquipmentVersionInput, UpdateEquipmentComponentInput, UpdateEquipmentInput, UpdateEquipmentParameterInput, UpdateEquipmentVersionInput } from '../../types/equipment'
import type { ProjectSummary } from '../../types/projects'
import { formatChinaDateTime } from '../../utils/customer-format'
import { equipmentCategories, equipmentCategoryLabel, equipmentModelLabel } from '../../utils/equipment-format'

const authStore = useAuthStore()
const settingsStore = useSettingsStore()
const route = useRoute()
const canManage = computed(() => authStore.canAny([permissions.equipmentManage]))
const loading = ref(false); const saving = ref(false)
const equipment = ref<EquipmentSummary[]>([]); const total = ref(0); const page = ref(1); const pageSize = ref(settingsStore.defaultPageSize)
const search = ref(''); const archive = ref<'active' | 'archived' | 'all'>('active'); const category = ref<EquipmentCategory | ''>('')
const selected = ref<EquipmentDetails | null>(null); const editing = ref<EquipmentDetails | null>(null)
const customers = ref<CustomerSummary[]>([]); const projects = ref<ProjectSummary[]>([])
const editingComponent = ref<EquipmentComponentDetails | null>(null); const editingParameter = ref<EquipmentParameterDetails | null>(null); const editingVersion = ref<EquipmentVersionDetails | null>(null)
const detailVisible = ref(false); const formVisible = ref(false); const componentVisible = ref(false); const parameterVisible = ref(false); const versionVisible = ref(false)

onMounted(async () => { await Promise.all([loadEquipment(), searchCustomers('')]); await openEquipmentFromRoute(route.query.entityId) })
watch(() => route.query.entityId, openEquipmentFromRoute)

async function loadEquipment() {
  loading.value = true
  try {
    const result = await equipmentApi.listEquipment({ page: page.value, pageSize: pageSize.value, search: search.value.trim() || undefined, archive: archive.value, category: category.value || undefined, sortBy: 'updatedAt', sortDescending: true })
    equipment.value = result.items; total.value = result.total
  } catch (error) { showError(error, '设备列表加载失败。') } finally { loading.value = false }
}
async function searchCustomers(query: string) { try { customers.value = (await customerApi.listCustomers({ page: 1, pageSize: 100, search: query.trim() || undefined, archive: 'active', sortBy: 'name', sortDescending: false })).items } catch (error) { showError(error, '客户选项加载失败。') } }
async function loadProjectOptions(customerId: number) { try { projects.value = (await projectApi.listProjects({ page: 1, pageSize: 100, customerId, archive: 'active', sortBy: 'updatedAt', sortDescending: true })).items } catch (error) { showError(error, '项目选项加载失败。') } }
async function openEquipment(row: EquipmentSummary) { await openEquipmentById(row.id) }
async function openEquipmentFromRoute(value: unknown) { const raw = Array.isArray(value) ? value[0] : value; const id = Number(raw); if (!Number.isSafeInteger(id) || id <= 0 || selected.value?.id === id) return; await openEquipmentById(id) }
async function openEquipmentById(equipmentId: number) { loading.value = true; try { selected.value = await equipmentApi.getEquipment(equipmentId); detailVisible.value = true } catch (error) { showError(error, '设备详情加载失败。') } finally { loading.value = false } }
async function refreshSelected() { if (selected.value) selected.value = await equipmentApi.getEquipment(selected.value.id) }
function openCreate() { editing.value = null; projects.value = []; formVisible.value = true }
async function openEdit() { if (!selected.value) return; editing.value = selected.value; await Promise.all([searchCustomers(selected.value.customerCode), loadProjectOptions(selected.value.customerId)]); formVisible.value = true }
async function saveEquipment(input: EquipmentInput | UpdateEquipmentInput) { saving.value = true; try { const saved = editing.value && 'version' in input ? await equipmentApi.updateEquipment(editing.value.id, input) : await equipmentApi.createEquipment(input); selected.value = saved; formVisible.value = false; detailVisible.value = true; await loadEquipment(); ElMessage.success(editing.value ? '设备已更新。' : '设备已创建。') } catch (error) { showError(error, '设备保存失败。') } finally { saving.value = false } }
async function toggleArchive() { if (!selected.value) return; const action = selected.value.isArchived ? '恢复' : '归档'; try { await ElMessageBox.confirm(`${action}设备“${selected.value.name}”？`, `${action}设备`); saving.value = true; selected.value = await equipmentApi.setEquipmentArchived(selected.value.id, !selected.value.isArchived, selected.value.version); await loadEquipment(); ElMessage.success(`设备已${action}。`) } catch (error) { if (error === 'cancel' || error === 'close') return; showError(error, `设备${action}失败。`) } finally { saving.value = false } }

function addComponent() { editingComponent.value = null; componentVisible.value = true }
function editComponent(item: EquipmentComponentDetails) { editingComponent.value = item; componentVisible.value = true }
async function saveComponent(input: EquipmentComponentInput | UpdateEquipmentComponentInput) { if (!selected.value) return; saving.value = true; try { if (editingComponent.value && 'version' in input) await equipmentApi.updateComponent(selected.value.id, editingComponent.value.id, input); else await equipmentApi.createComponent(selected.value.id, input as EquipmentComponentInput); componentVisible.value = false; await Promise.all([refreshSelected(), loadEquipment()]); ElMessage.success('设备组件已保存。') } catch (error) { showError(error, '设备组件保存失败。') } finally { saving.value = false } }
async function deleteComponent(item: EquipmentComponentDetails) { if (!selected.value) return; try { await ElMessageBox.confirm(`删除组件“${item.name}”？`, '删除组件'); await equipmentApi.deleteComponent(selected.value.id, item.id, item.version); await Promise.all([refreshSelected(), loadEquipment()]); ElMessage.success('设备组件已删除。') } catch (error) { if (error === 'cancel' || error === 'close') return; showError(error, '设备组件删除失败。') } }

function addParameter() { editingParameter.value = null; parameterVisible.value = true }
function editParameter(item: EquipmentParameterDetails) { editingParameter.value = item; parameterVisible.value = true }
async function saveParameter(input: EquipmentParameterInput | UpdateEquipmentParameterInput) { if (!selected.value) return; saving.value = true; try { if (editingParameter.value && 'version' in input) await equipmentApi.updateParameter(selected.value.id, editingParameter.value.id, input); else await equipmentApi.createParameter(selected.value.id, input as EquipmentParameterInput); parameterVisible.value = false; await Promise.all([refreshSelected(), loadEquipment()]); ElMessage.success('设备参数已保存。') } catch (error) { showError(error, '设备参数保存失败。') } finally { saving.value = false } }
async function deleteParameter(item: EquipmentParameterDetails) { if (!selected.value) return; try { await ElMessageBox.confirm(`删除参数“${item.name}”？`, '删除参数'); await equipmentApi.deleteParameter(selected.value.id, item.id, item.version); await Promise.all([refreshSelected(), loadEquipment()]); ElMessage.success('设备参数已删除。') } catch (error) { if (error === 'cancel' || error === 'close') return; showError(error, '设备参数删除失败。') } }

function addVersion() { editingVersion.value = null; versionVisible.value = true }
function editVersion(item: EquipmentVersionDetails) { editingVersion.value = item; versionVisible.value = true }
async function saveVersion(input: EquipmentVersionInput | UpdateEquipmentVersionInput) { if (!selected.value) return; saving.value = true; try { if (editingVersion.value && 'version' in input) await equipmentApi.updateVersion(selected.value.id, editingVersion.value.id, input); else await equipmentApi.createVersion(selected.value.id, input as EquipmentVersionInput); versionVisible.value = false; await Promise.all([refreshSelected(), loadEquipment()]); ElMessage.success('设备版本已保存。') } catch (error) { showError(error, '设备版本保存失败。') } finally { saving.value = false } }
async function deleteVersion(item: EquipmentVersionDetails) { if (!selected.value) return; try { await ElMessageBox.confirm(`删除版本“${item.versionLabel}”？`, '删除版本'); await equipmentApi.deleteVersion(selected.value.id, item.id, item.version); await Promise.all([refreshSelected(), loadEquipment()]); ElMessage.success('设备版本已删除。') } catch (error) { if (error === 'cancel' || error === 'close') return; showError(error, '设备版本删除失败。') } }
async function runSearch() { page.value = 1; await loadEquipment() }
function showError(error: unknown, fallback: string) { if (error instanceof ApiError) { const validation = Object.values(error.problem?.errors ?? {}).flat()[0]; ElMessage.error(validation ?? error.problem?.detail ?? error.problem?.title ?? fallback); return } ElMessage.error(fallback) }
</script>

<template>
  <section class="equipment-page">
    <div class="equipment-toolbar">
      <el-input
        v-model="search"
        clearable
        :prefix-icon="Search"
        placeholder="搜索设备、客户、项目、型号或序列号"
        @keyup.enter="runSearch"
        @clear="runSearch"
      />
      <el-select
        v-model="category"
        clearable
        placeholder="全部分类"
        @change="runSearch"
      >
        <el-option
          v-for="item in equipmentCategories"
          :key="item"
          :label="equipmentCategoryLabel(item)"
          :value="item"
        />
      </el-select>
      <el-select
        v-model="archive"
        @change="runSearch"
      >
        <el-option
          label="在用设备"
          value="active"
        /><el-option
          label="已归档"
          value="archived"
        /><el-option
          label="全部设备"
          value="all"
        />
      </el-select>
      <el-button
        :icon="Refresh"
        @click="loadEquipment"
      >
        刷新
      </el-button>
      <el-button
        v-if="canManage"
        class="toolbar-primary-action"
        type="primary"
        :icon="Plus"
        @click="openCreate"
      >
        新建设备
      </el-button>
    </div>
    <el-table
      v-loading="loading"
      class="flowhearth-data-table"
      :data="equipment"
      border
      stripe
      row-class-name="customer-table-row"
      @row-click="openEquipment"
    >
      <el-table-column
        prop="code"
        label="设备编号"
        width="150"
      />
      <el-table-column
        label="设备"
        min-width="230"
      >
        <template #default="scope">
          <strong>{{ scope.row.name }}</strong><p class="cell-secondary">
            {{ equipmentModelLabel(scope.row.manufacturer, scope.row.model) }}
          </p>
        </template>
      </el-table-column>
      <el-table-column
        label="分类"
        width="110"
      >
        <template #default="scope">
          <el-tag>{{ equipmentCategoryLabel(scope.row.category) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column
        label="客户 / 项目"
        min-width="220"
      >
        <template #default="scope">
          <strong>{{ scope.row.customerName }}</strong><p class="cell-secondary">
            {{ scope.row.projectName || '未关联项目' }}
          </p>
        </template>
      </el-table-column>
      <el-table-column
        prop="serialNumber"
        label="序列号"
        min-width="140"
      />
      <el-table-column
        label="组件 / 参数 / 版本"
        width="160"
      >
        <template #default="scope">
          {{ scope.row.componentCount }} / {{ scope.row.parameterCount }} / {{ scope.row.versionCount }}
        </template>
      </el-table-column>
      <el-table-column
        label="最近更新"
        width="175"
      >
        <template #default="scope">
          {{ formatChinaDateTime(scope.row.updatedAtUtc) }}
        </template>
      </el-table-column>
    </el-table>
    <el-empty
      v-if="!loading && equipment.length === 0"
      description="暂无符合条件的设备"
    />
    <el-pagination
      v-if="total > 0"
      v-model:current-page="page"
      v-model:page-size="pageSize"
      class="table-pagination"
      layout="total, sizes, prev, pager, next"
      :page-sizes="[10,20,50,100]"
      :total="total"
      @change="loadEquipment"
    />
  </section>
  <EquipmentDetailDrawer
    v-model="detailVisible"
    :equipment="selected"
    :can-manage="canManage"
    :busy="saving"
    @edit="openEdit"
    @archive="toggleArchive"
    @add-component="addComponent"
    @edit-component="editComponent"
    @delete-component="deleteComponent"
    @add-parameter="addParameter"
    @edit-parameter="editParameter"
    @delete-parameter="deleteParameter"
    @add-version="addVersion"
    @edit-version="editVersion"
    @delete-version="deleteVersion"
  />
  <EquipmentFormDialog
    v-model="formVisible"
    :equipment="editing"
    :customers="customers"
    :projects="projects"
    :saving="saving"
    @search-customer="searchCustomers"
    @customer-change="loadProjectOptions"
    @save="saveEquipment"
  />
  <EquipmentComponentDialog
    v-model="componentVisible"
    :component="editingComponent"
    :saving="saving"
    @save="saveComponent"
  />
  <EquipmentParameterDialog
    v-model="parameterVisible"
    :parameter="editingParameter"
    :saving="saving"
    @save="saveParameter"
  />
  <EquipmentVersionDialog
    v-model="versionVisible"
    :equipment-version="editingVersion"
    :saving="saving"
    @save="saveVersion"
  />
</template>
