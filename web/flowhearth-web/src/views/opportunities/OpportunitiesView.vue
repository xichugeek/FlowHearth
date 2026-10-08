<script setup lang="ts">
import { Grid, List, Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, onMounted, ref, watch } from 'vue'

import * as customerApi from '../../api/customers'
import * as opportunityApi from '../../api/opportunities'
import { ApiError } from '../../api/problem-details'
import OpportunityDetailDrawer from '../../components/opportunities/OpportunityDetailDrawer.vue'
import OpportunityFormDialog from '../../components/opportunities/OpportunityFormDialog.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type { CustomerSummary } from '../../types/customers'
import type {
  OpportunityDetails,
  OpportunityInput,
  OpportunityStage,
  OpportunitySummary,
  UpdateOpportunityInput,
} from '../../types/opportunities'
import { formatChinaDateTime } from '../../utils/customer-format'
import {
  expectedWeightedValue,
  formatCurrency,
  isTerminalOpportunityStage,
  opportunityStageLabel,
  opportunityStages,
  opportunityStageTagType,
} from '../../utils/opportunity-format'

const authStore = useAuthStore()
const settingsStore = useSettingsStore()
const canManage = computed(() =>
  authStore.canAny([permissions.opportunitiesManage]),
)
const loading = ref(false)
const saving = ref(false)
const opportunities = ref<OpportunitySummary[]>([])
const customerOptions = ref<CustomerSummary[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(settingsStore.defaultPageSize)
const search = ref('')
const archive = ref<'active' | 'archived' | 'all'>('active')
const stage = ref<OpportunityStage | ''>('')
const sortBy = ref<
  | 'code'
  | 'title'
  | 'stage'
  | 'expectedAmount'
  | 'probability'
  | 'expectedCloseDate'
  | 'updatedAt'
>('updatedAt')
const sortDescending = ref(true)
const viewMode = ref<'list' | 'board'>('list')
const selectedOpportunity = ref<OpportunityDetails | null>(null)
const editingOpportunity = ref<OpportunityDetails | null>(null)
const detailVisible = ref(false)
const formVisible = ref(false)
const draggedOpportunityId = ref<number | null>(null)

const activePipelineAmount = computed(() =>
  opportunities.value
    .filter((item) => !isTerminalOpportunityStage(item.stage))
    .reduce((totalValue, item) => totalValue + item.expectedAmount, 0),
)
const weightedPipelineAmount = computed(() =>
  opportunities.value
    .filter((item) => !isTerminalOpportunityStage(item.stage))
    .reduce(
      (totalValue, item) =>
        totalValue +
        expectedWeightedValue(item.expectedAmount, item.probabilityPercent),
      0,
    ),
)

onMounted(async () => {
  await Promise.all([loadOpportunities(), searchCustomers('')])
})

watch(viewMode, async () => {
  page.value = 1
  await loadOpportunities()
})

async function loadOpportunities() {
  loading.value = true
  try {
    const result = await opportunityApi.listOpportunities({
      page: viewMode.value === 'board' ? 1 : page.value,
      pageSize: viewMode.value === 'board' ? 100 : pageSize.value,
      search: search.value.trim() || undefined,
      archive: archive.value,
      stage: stage.value || undefined,
      sortBy: viewMode.value === 'board' ? 'updatedAt' : sortBy.value,
      sortDescending: viewMode.value === 'board' ? true : sortDescending.value,
    })
    opportunities.value = result.items
    total.value = result.total
  } catch (error) {
    showError(error, '商机列表加载失败。')
  } finally {
    loading.value = false
  }
}

async function searchCustomers(query: string) {
  try {
    const result = await customerApi.listCustomers({
      page: 1,
      pageSize: 100,
      search: query.trim() || undefined,
      archive: 'active',
      sortBy: 'name',
      sortDescending: false,
    })
    customerOptions.value = result.items
  } catch (error) {
    showError(error, '客户选项加载失败。')
  }
}

async function openOpportunity(row: OpportunitySummary) {
  loading.value = true
  try {
    selectedOpportunity.value = await opportunityApi.getOpportunity(row.id)
    detailVisible.value = true
  } catch (error) {
    showError(error, '商机详情加载失败。')
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editingOpportunity.value = null
  formVisible.value = true
}

async function openEdit() {
  if (!selectedOpportunity.value) return
  editingOpportunity.value = selectedOpportunity.value
  await searchCustomers(selectedOpportunity.value.customerCode)
  formVisible.value = true
}

async function saveOpportunity(
  input: OpportunityInput | UpdateOpportunityInput,
) {
  saving.value = true
  try {
    const saved =
      editingOpportunity.value && 'version' in input
        ? await opportunityApi.updateOpportunity(
            editingOpportunity.value.id,
            input,
          )
        : await opportunityApi.createOpportunity(input)
    selectedOpportunity.value = saved
    formVisible.value = false
    detailVisible.value = true
    await loadOpportunities()
    ElMessage.success(editingOpportunity.value ? '商机已更新。' : '商机已创建。')
  } catch (error) {
    showError(error, '商机保存失败。')
  } finally {
    saving.value = false
  }
}

async function transitionSelected(target: OpportunityStage) {
  if (!selectedOpportunity.value) return
  await transitionOpportunity(selectedOpportunity.value, target)
}

async function transitionOpportunity(
  opportunity: OpportunitySummary | OpportunityDetails,
  target: OpportunityStage,
) {
  if (
    opportunity.stage === target ||
    opportunity.isArchived ||
    isTerminalOpportunityStage(opportunity.stage)
  ) {
    return
  }

  let lostReason: string | null = null
  try {
    if (target === 'Lost') {
      const result = await ElMessageBox.prompt(
        `请填写“${opportunity.title}”的丢单原因。`,
        '商机丢单',
        {
          confirmButtonText: '确认丢单',
          cancelButtonText: '取消',
          inputType: 'textarea',
          inputValidator: (value) =>
            value.trim().length > 0 || '丢单原因不能为空。',
          inputErrorMessage: '丢单原因不能为空。',
        },
      )
      lostReason = result.value.trim()
    } else if (target === 'Won') {
      await ElMessageBox.confirm(
        `确认将“${opportunity.title}”标记为赢单？赢单后阶段不能回退。`,
        '确认赢单',
        { type: 'success', confirmButtonText: '确认赢单', cancelButtonText: '取消' },
      )
    }

    saving.value = true
    const updated = await opportunityApi.transitionOpportunity(
      opportunity.id,
      target,
      lostReason,
      opportunity.version,
    )
    if (selectedOpportunity.value?.id === updated.id) {
      selectedOpportunity.value = updated
    }
    await loadOpportunities()
    ElMessage.success(`商机已转为“${opportunityStageLabel(target)}”。`)
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    showError(error, '商机阶段更新失败。')
  } finally {
    saving.value = false
    draggedOpportunityId.value = null
  }
}

async function toggleArchive() {
  if (!selectedOpportunity.value) return
  const current = selectedOpportunity.value
  const action = current.isArchived ? '恢复' : '归档'
  try {
    await ElMessageBox.confirm(
      `${action}商机“${current.title}”？`,
      `${action}商机`,
      { type: 'warning', confirmButtonText: action, cancelButtonText: '取消' },
    )
    selectedOpportunity.value = await opportunityApi.setOpportunityArchived(
      current.id,
      !current.isArchived,
      current.version,
    )
    await loadOpportunities()
    ElMessage.success(`商机已${action}。`)
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    showError(error, `商机${action}失败。`)
  }
}

async function convertToProject() {
  if (!selectedOpportunity.value) return
  const current = selectedOpportunity.value
  try {
    await ElMessageBox.confirm(
      `将赢单商机“${current.title}”生成项目？项目金额将采用当前预计金额。`,
      '生成项目',
      { type: 'success', confirmButtonText: '生成项目', cancelButtonText: '取消' },
    )
    saving.value = true
    const project = await opportunityApi.convertOpportunityToProject(
      current.id,
      current.version,
    )
    selectedOpportunity.value = await opportunityApi.getOpportunity(current.id)
    await loadOpportunities()
    ElMessage.success(`项目 ${project.code} 已创建。`)
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    showError(error, '项目生成失败。')
  } finally {
    saving.value = false
  }
}

async function runSearch() {
  page.value = 1
  await loadOpportunities()
}

async function changeSort(value: { prop?: string | null; order?: string | null }) {
  if (!value.prop) return
  const mapping: Record<string, typeof sortBy.value> = {
    code: 'code',
    title: 'title',
    stage: 'stage',
    expectedAmount: 'expectedAmount',
    probabilityPercent: 'probability',
    expectedCloseDate: 'expectedCloseDate',
    updatedAtUtc: 'updatedAt',
  }
  const nextSort = mapping[value.prop]
  if (!nextSort) return
  sortBy.value = nextSort
  sortDescending.value = value.order !== 'ascending'
  await loadOpportunities()
}

function boardItems(column: OpportunityStage) {
  return opportunities.value.filter((item) => item.stage === column)
}

function startDrag(opportunity: OpportunitySummary) {
  if (!canManage.value || isTerminalOpportunityStage(opportunity.stage)) return
  draggedOpportunityId.value = opportunity.id
}

async function dropOnStage(target: OpportunityStage) {
  const opportunity = opportunities.value.find(
    (item) => item.id === draggedOpportunityId.value,
  )
  if (opportunity) {
    await transitionOpportunity(opportunity, target)
  }
}

function showError(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    const validation = Object.values(error.problem?.errors ?? {}).flat()[0]
    ElMessage.error(
      validation ?? error.problem?.detail ?? error.problem?.title ?? fallback,
    )
    return
  }
  ElMessage.error(fallback)
}
</script>

<template>
  <section class="opportunity-page">
    <div class="pipeline-metrics">
      <div>
        <span>当前结果</span>
        <strong>{{ total }} 个商机</strong>
      </div>
      <div>
        <span>当前可见在手金额</span>
        <strong>{{ formatCurrency(activePipelineAmount) }}</strong>
      </div>
      <div>
        <span>当前可见加权金额</span>
        <strong>{{ formatCurrency(weightedPipelineAmount) }}</strong>
      </div>
    </div>

    <div class="opportunity-toolbar">
      <el-input
        v-model="search"
        class="opportunity-search"
        clearable
        placeholder="搜索商机、客户或编号"
        :prefix-icon="Search"
        @keyup.enter="runSearch"
        @clear="runSearch"
      />
      <el-select
        v-model="stage"
        clearable
        placeholder="全部阶段"
        @change="runSearch"
      >
        <el-option
          v-for="item in opportunityStages"
          :key="item"
          :label="opportunityStageLabel(item)"
          :value="item"
        />
      </el-select>
      <el-select
        v-model="archive"
        @change="runSearch"
      >
        <el-option
          label="在用商机"
          value="active"
        />
        <el-option
          label="已归档"
          value="archived"
        />
        <el-option
          label="全部商机"
          value="all"
        />
      </el-select>
      <el-radio-group v-model="viewMode">
        <el-radio-button value="list">
          <el-icon><List /></el-icon>
          列表
        </el-radio-button>
        <el-radio-button value="board">
          <el-icon><Grid /></el-icon>
          看板
        </el-radio-button>
      </el-radio-group>
      <el-button
        :icon="Refresh"
        @click="loadOpportunities"
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
        新建商机
      </el-button>
    </div>

    <template v-if="viewMode === 'list'">
      <el-table
        v-loading="loading"
        class="flowhearth-data-table"
        :data="opportunities"
        border
        stripe
        row-class-name="customer-table-row"
        @row-click="openOpportunity"
        @sort-change="changeSort"
      >
        <el-table-column
          prop="code"
          label="商机编号"
          width="150"
          sortable="custom"
        />
        <el-table-column
          prop="title"
          label="商机名称"
          min-width="220"
          sortable="custom"
        >
          <template #default="scope">
            <strong>{{ scope.row.title }}</strong>
            <p class="cell-secondary">
              {{ scope.row.customerName }} · {{ scope.row.customerCode }}
            </p>
          </template>
        </el-table-column>
        <el-table-column
          prop="stage"
          label="阶段"
          width="120"
          sortable="custom"
        >
          <template #default="scope">
            <el-tag :type="opportunityStageTagType(scope.row.stage)">
              {{ opportunityStageLabel(scope.row.stage) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column
          prop="expectedAmount"
          label="预计金额"
          width="150"
          sortable="custom"
        >
          <template #default="scope">
            {{ formatCurrency(scope.row.expectedAmount) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="probabilityPercent"
          label="概率"
          width="100"
          sortable="custom"
        >
          <template #default="scope">
            {{ scope.row.probabilityPercent }}%
          </template>
        </el-table-column>
        <el-table-column
          prop="expectedCloseDate"
          label="预计成交"
          width="130"
          sortable="custom"
        >
          <template #default="scope">
            {{ scope.row.expectedCloseDate?.slice(0, 10) || '—' }}
          </template>
        </el-table-column>
        <el-table-column
          prop="updatedAtUtc"
          label="最近更新"
          width="175"
          sortable="custom"
        >
          <template #default="scope">
            {{ formatChinaDateTime(scope.row.updatedAtUtc) }}
          </template>
        </el-table-column>
        <el-table-column
          label="转化"
          width="145"
        >
          <template #default="scope">
            <span
              v-if="scope.row.convertedProject"
              class="project-link-label"
            >
              {{ scope.row.convertedProject.code }}
            </span>
            <span v-else>—</span>
          </template>
        </el-table-column>
      </el-table>
      <el-empty
        v-if="!loading && opportunities.length === 0"
        description="暂无符合条件的商机"
      />
      <el-pagination
        v-if="total > 0"
        v-model:current-page="page"
        v-model:page-size="pageSize"
        class="table-pagination"
        layout="total, sizes, prev, pager, next"
        :page-sizes="[10, 20, 50, 100]"
        :total="total"
        @change="loadOpportunities"
      />
    </template>

    <template v-else>
      <p class="board-scroll-hint">
        看板可横向滚动，点击卡片查看详情。
      </p>
      <div
        v-loading="loading"
        class="opportunity-board"
      >
        <section
          v-for="column in opportunityStages"
          :key="column"
          class="opportunity-column"
          @dragover.prevent
          @drop="dropOnStage(column)"
        >
          <header>
            <span>{{ opportunityStageLabel(column) }}</span>
            <strong>{{ boardItems(column).length }}</strong>
          </header>
          <div class="opportunity-column-body">
            <article
              v-for="item in boardItems(column)"
              :key="item.id"
              class="opportunity-card"
              :class="{ draggable: canManage && !isTerminalOpportunityStage(item.stage) }"
              :draggable="canManage && !isTerminalOpportunityStage(item.stage)"
              @dragstart="startDrag(item)"
              @dragend="draggedOpportunityId = null"
              @click="openOpportunity(item)"
            >
              <span>{{ item.code }}</span>
              <h4>{{ item.title }}</h4>
              <p>{{ item.customerName }}</p>
              <div>
                <strong>{{ formatCurrency(item.expectedAmount) }}</strong>
                <small>{{ item.probabilityPercent }}%</small>
              </div>
              <em>{{ item.expectedCloseDate?.slice(0, 10) || '未设成交日' }}</em>
            </article>
            <el-empty
              v-if="boardItems(column).length === 0"
              :image-size="44"
              description="暂无"
            />
          </div>
        </section>
        <p
          v-if="total > 100"
          class="board-limit-note"
        >
          看板显示最近更新的 100 个商机，请使用搜索或阶段筛选缩小范围。
        </p>
      </div>
    </template>
  </section>

  <OpportunityDetailDrawer
    v-model="detailVisible"
    :opportunity="selectedOpportunity"
    :can-manage="canManage"
    :busy="saving"
    @edit="openEdit"
    @transition="transitionSelected"
    @archive="toggleArchive"
    @convert="convertToProject"
  />
  <OpportunityFormDialog
    v-model="formVisible"
    :opportunity="editingOpportunity"
    :customers="customerOptions"
    :saving="saving"
    @search-customer="searchCustomers"
    @save="saveOpportunity"
  />
</template>
