<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import * as customerApi from '../../api/customers'
import * as projectApi from '../../api/projects'
import { ApiError } from '../../api/problem-details'
import ProjectDetailDrawer from '../../components/projects/ProjectDetailDrawer.vue'
import ProjectFormDialog from '../../components/projects/ProjectFormDialog.vue'
import ProjectMemberDialog from '../../components/projects/ProjectMemberDialog.vue'
import ProjectMilestoneDialog from '../../components/projects/ProjectMilestoneDialog.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { CustomerSummary } from '../../types/customers'
import type { ProjectDetails, ProjectInput, ProjectMemberCandidate, ProjectMemberDetails, ProjectMemberInput, ProjectMilestoneDetails, ProjectMilestoneInput, ProjectStatus, ProjectSummary, UpdateProjectInput, UpdateProjectMemberInput, UpdateProjectMilestoneInput } from '../../types/projects'
import { formatChinaDateTime } from '../../utils/customer-format'
import { formatCurrency } from '../../utils/opportunity-format'
import { projectStatuses, projectStatusLabel, projectStatusTagType } from '../../utils/project-format'

const authStore = useAuthStore()
const settingsStore = useSettingsStore()
const route = useRoute()
const canManage = computed(() => authStore.canAny([permissions.projectsManage]))
const loading = ref(false); const saving = ref(false)
const projects = ref<ProjectSummary[]>([]); const total = ref(0); const page = ref(1); const pageSize = ref(settingsStore.defaultPageSize)
const search = ref(''); const archive = ref<'active' | 'archived' | 'all'>('active'); const status = ref<ProjectStatus | ''>('')
const selected = ref<ProjectDetails | null>(null); const editing = ref<ProjectDetails | null>(null)
const customers = ref<CustomerSummary[]>([]); const candidates = ref<ProjectMemberCandidate[]>([])
const editingMember = ref<ProjectMemberDetails | null>(null); const editingMilestone = ref<ProjectMilestoneDetails | null>(null)
const detailVisible = ref(false); const formVisible = ref(false); const memberVisible = ref(false); const milestoneVisible = ref(false)
const todayParts = new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date())
const todayInChina = `${todayParts.find((item) => item.type === 'year')?.value}-${todayParts.find((item) => item.type === 'month')?.value}-${todayParts.find((item) => item.type === 'day')?.value}`
function isOverdue(value: unknown) { const project = value as ProjectSummary; return !!project.plannedEndDate && project.plannedEndDate.slice(0, 10) < todayInChina && !['Completed', 'Cancelled'].includes(project.status) }

onMounted(async () => {
  await Promise.all([loadProjects(), searchCustomers(''), loadCandidates()])
  await openProjectFromRoute(route.query.entityId)
})
watch(() => route.query.entityId, openProjectFromRoute)

async function loadProjects() {
  loading.value = true
  try { const result = await projectApi.listProjects({ page: page.value, pageSize: pageSize.value, search: search.value.trim() || undefined, archive: archive.value, status: status.value || undefined, sortBy: 'updatedAt', sortDescending: true }); projects.value = result.items; total.value = result.total }
  catch (error) { showError(error, '项目列表加载失败。') } finally { loading.value = false }
}
async function searchCustomers(query: string) { try { customers.value = (await customerApi.listCustomers({ page: 1, pageSize: 100, search: query.trim() || undefined, archive: 'active', sortBy: 'name', sortDescending: false })).items } catch (error) { showError(error, '客户选项加载失败。') } }
async function loadCandidates() { if (!canManage.value) return; try { candidates.value = await projectApi.listMemberCandidates() } catch (error) { showError(error, '成员选项加载失败。') } }
async function openProject(row: ProjectSummary) { await openProjectById(row.id) }
async function openProjectFromRoute(value: unknown) { const raw = Array.isArray(value) ? value[0] : value; const id = Number(raw); if (!Number.isSafeInteger(id) || id <= 0 || selected.value?.id === id) return; await openProjectById(id) }
async function openProjectById(projectId: number) { loading.value = true; try { selected.value = await projectApi.getProject(projectId); detailVisible.value = true } catch (error) { showError(error, '项目详情加载失败。') } finally { loading.value = false } }
async function refreshSelected() { if (selected.value) selected.value = await projectApi.getProject(selected.value.id) }
function openCreate() { editing.value = null; formVisible.value = true }
async function openEdit() { if (!selected.value) return; editing.value = selected.value; await searchCustomers(selected.value.customerCode); formVisible.value = true }
async function saveProject(input: ProjectInput | UpdateProjectInput) { saving.value = true; try { const saved = editing.value && 'version' in input ? await projectApi.updateProject(editing.value.id, input) : await projectApi.createProject(input); selected.value = saved; formVisible.value = false; detailVisible.value = true; await loadProjects(); ElMessage.success(editing.value ? '项目已更新。' : '项目已创建。') } catch (error) { showError(error, '项目保存失败。') } finally { saving.value = false } }
async function transitionProject(target: ProjectStatus) { if (!selected.value) return; try { await ElMessageBox.confirm(`确认将项目变更为“${projectStatusLabel(target)}”？`, '项目状态', { type: 'warning' }); saving.value = true; selected.value = await projectApi.transitionProject(selected.value.id, target, selected.value.version); await loadProjects(); ElMessage.success('项目状态已更新。') } catch (error) { if (error === 'cancel' || error === 'close') return; showError(error, '项目状态更新失败。') } finally { saving.value = false } }
async function toggleArchive() { if (!selected.value) return; const action = selected.value.isArchived ? '恢复' : '归档'; try { await ElMessageBox.confirm(`${action}项目“${selected.value.name}”？`, `${action}项目`); selected.value = await projectApi.setProjectArchived(selected.value.id, !selected.value.isArchived, selected.value.version); await loadProjects(); ElMessage.success(`项目已${action}。`) } catch (error) { if (error === 'cancel' || error === 'close') return; showError(error, `项目${action}失败。`) } }
function addMember() { editingMember.value = null; memberVisible.value = true }
function editMember(item: ProjectMemberDetails) { editingMember.value = item; memberVisible.value = true }
async function saveMember(input: ProjectMemberInput | UpdateProjectMemberInput) { if (!selected.value) return; saving.value = true; try { if (editingMember.value && 'version' in input) await projectApi.updateMember(selected.value.id, editingMember.value.id, input); else await projectApi.createMember(selected.value.id, input as ProjectMemberInput); memberVisible.value = false; await Promise.all([refreshSelected(), loadProjects()]); ElMessage.success('项目成员已保存。') } catch (error) { showError(error, '项目成员保存失败。') } finally { saving.value = false } }
async function deleteMember(item: ProjectMemberDetails) { if (!selected.value) return; try { await ElMessageBox.confirm(`移除成员“${item.displayName}”？`, '移除成员'); await projectApi.deleteMember(selected.value.id, item.id, item.version); await Promise.all([refreshSelected(), loadProjects()]); ElMessage.success('成员已移除。') } catch (error) { if (error === 'cancel' || error === 'close') return; showError(error, '成员移除失败。') } }
function addMilestone() { editingMilestone.value = null; milestoneVisible.value = true }
function editMilestone(item: ProjectMilestoneDetails) { editingMilestone.value = item; milestoneVisible.value = true }
async function saveMilestone(input: ProjectMilestoneInput | UpdateProjectMilestoneInput) { if (!selected.value) return; saving.value = true; try { if (editingMilestone.value && 'version' in input) await projectApi.updateMilestone(selected.value.id, editingMilestone.value.id, input); else await projectApi.createMilestone(selected.value.id, input); milestoneVisible.value = false; await Promise.all([refreshSelected(), loadProjects()]); ElMessage.success('里程碑已保存。') } catch (error) { showError(error, '里程碑保存失败。') } finally { saving.value = false } }
async function deleteMilestone(item: ProjectMilestoneDetails) { if (!selected.value) return; try { await ElMessageBox.confirm(`删除里程碑“${item.name}”？`, '删除里程碑'); await projectApi.deleteMilestone(selected.value.id, item.id, item.version); await Promise.all([refreshSelected(), loadProjects()]); ElMessage.success('里程碑已删除。') } catch (error) { if (error === 'cancel' || error === 'close') return; showError(error, '里程碑删除失败。') } }
async function runSearch() { page.value = 1; await loadProjects() }
function showError(error: unknown, fallback: string) { if (error instanceof ApiError) { const validation = Object.values(error.problem?.errors ?? {}).flat()[0]; ElMessage.error(validation ?? error.problem?.detail ?? error.problem?.title ?? fallback); return } ElMessage.error(fallback) }
</script>

<template>
  <section class="project-page">
    <div class="project-toolbar">
      <el-input
        v-model="search"
        clearable
        :prefix-icon="Search"
        placeholder="搜索项目、客户或来源商机"
        @keyup.enter="runSearch"
        @clear="runSearch"
      /><el-select
        v-model="status"
        clearable
        placeholder="全部状态"
        @change="runSearch"
      >
        <el-option
          v-for="item in projectStatuses"
          :key="item"
          :label="projectStatusLabel(item)"
          :value="item"
        />
      </el-select><el-select
        v-model="archive"
        @change="runSearch"
      >
        <el-option
          label="在用项目"
          value="active"
        /><el-option
          label="已归档"
          value="archived"
        /><el-option
          label="全部项目"
          value="all"
        />
      </el-select><el-button
        :icon="Refresh"
        @click="loadProjects"
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
        新建项目
      </el-button>
    </div>
    <el-table
      v-loading="loading"
      class="flowhearth-data-table"
      :data="projects"
      border
      stripe
      row-class-name="customer-table-row"
      @row-click="openProject"
    >
      <el-table-column
        prop="code"
        label="项目编号"
        width="150"
      /><el-table-column
        label="项目名称"
        min-width="230"
      >
        <template #default="scope">
          <strong>{{ scope.row.name }}</strong><p class="cell-secondary">
            {{ scope.row.customerName }} · {{ scope.row.sourceOpportunityCode || '直接创建' }}
          </p>
        </template>
      </el-table-column><el-table-column
        label="状态"
        width="110"
      >
        <template #default="scope">
          <el-tag :type="projectStatusTagType(scope.row.status)">
            {{ projectStatusLabel(scope.row.status) }}
          </el-tag>
        </template>
      </el-table-column><el-table-column
        label="合同金额"
        width="150"
      >
        <template #default="scope">
          {{ formatCurrency(scope.row.contractAmount) }}
        </template>
      </el-table-column><el-table-column
        label="进度"
        width="180"
      >
        <template #default="scope">
          <el-progress :percentage="scope.row.progressPercent" />
        </template>
      </el-table-column><el-table-column
        label="团队/里程碑"
        width="130"
      >
        <template #default="scope">
          {{ scope.row.memberCount }} / {{ scope.row.milestoneCount }}
        </template>
      </el-table-column><el-table-column
        label="计划结束"
        width="125"
      >
        <template #default="scope">
          <span :class="{ 'project-overdue-date': isOverdue(scope.row) }">{{ scope.row.plannedEndDate?.slice(0, 10) || '—' }}</span>
          <el-tag
            v-if="isOverdue(scope.row)"
            class="project-overdue-tag"
            size="small"
            type="danger"
            effect="plain"
          >
            延期
          </el-tag>
        </template>
      </el-table-column><el-table-column
        label="最近更新"
        width="175"
      >
        <template #default="scope">
          {{ formatChinaDateTime(scope.row.updatedAtUtc) }}
        </template>
      </el-table-column>
    </el-table>
    <el-empty
      v-if="!loading && projects.length === 0"
      description="暂无符合条件的项目"
    /><el-pagination
      v-if="total > 0"
      v-model:current-page="page"
      v-model:page-size="pageSize"
      class="table-pagination"
      layout="total, sizes, prev, pager, next"
      :page-sizes="[10,20,50,100]"
      :total="total"
      @change="loadProjects"
    />
  </section>
  <ProjectDetailDrawer
    v-model="detailVisible"
    :project="selected"
    :can-manage="canManage"
    :busy="saving"
    :initial-tab="route.query.tab === 'finance' ? 'finance' : undefined"
    @edit="openEdit"
    @transition="transitionProject"
    @archive="toggleArchive"
    @add-member="addMember"
    @edit-member="editMember"
    @delete-member="deleteMember"
    @add-milestone="addMilestone"
    @edit-milestone="editMilestone"
    @delete-milestone="deleteMilestone"
  />
  <ProjectFormDialog
    v-model="formVisible"
    :project="editing"
    :customers="customers"
    :saving="saving"
    @search-customer="searchCustomers"
    @save="saveProject"
  />
  <ProjectMemberDialog
    v-model="memberVisible"
    :member="editingMember"
    :candidates="candidates"
    :saving="saving"
    @save="saveMember"
  />
  <ProjectMilestoneDialog
    v-model="milestoneVisible"
    :milestone="editingMilestone"
    :saving="saving"
    @save="saveMilestone"
  />
</template>
