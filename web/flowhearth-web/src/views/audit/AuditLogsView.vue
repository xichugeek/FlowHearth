<script setup lang="ts">
import { Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { onMounted, reactive, ref } from 'vue'

import { getAuditLog, listAuditLogs } from '../../api/audit'
import { useSettingsStore } from '../../stores/settings'
import type { AuditLogDetails, AuditLogSummary } from '../../types/audit'
import { formatAuditJson } from '../../utils/attachment-format'
import { formatChinaDateTime } from '../../utils/customer-format'

const entityTypes = [
  { label: '客户', value: 'customer' },
  { label: '联系人', value: 'contact' },
  { label: '客户跟进', value: 'customer_followup' },
  { label: '商机', value: 'opportunity' },
  { label: '项目', value: 'project' },
  { label: '项目成员', value: 'project_member' },
  { label: '项目里程碑', value: 'project_milestone' },
  { label: '设备', value: 'equipment' },
  { label: '设备组件', value: 'equipment_component' },
  { label: '设备参数', value: 'equipment_parameter' },
  { label: '设备版本', value: 'equipment_version' },
  { label: '服务工单', value: 'service_ticket' },
  { label: '服务记录', value: 'service_record' },
  { label: '附件', value: 'Attachment' },
  { label: '字典项', value: 'LookupItem' },
  { label: '系统设置', value: 'SystemSetting' },
  { label: '用户', value: 'User' },
  { label: '角色', value: 'Role' },
]

const settingsStore = useSettingsStore()

const loading = ref(false)
const detailLoading = ref(false)
const rows = ref<AuditLogSummary[]>([])
const total = ref(0)
const detailVisible = ref(false)
const selected = ref<AuditLogDetails>()
const dateRange = ref<[Date, Date]>()
const query = reactive({
  page: 1,
  pageSize: settingsStore.defaultPageSize,
  search: '',
  entityType: '',
  action: '',
})

async function load() {
  loading.value = true
  try {
    const result = await listAuditLogs({
      page: query.page,
      pageSize: query.pageSize,
      search: query.search || undefined,
      entityType: query.entityType || undefined,
      action: query.action || undefined,
      occurredFromUtc: dateRange.value?.[0].toISOString(),
      occurredToUtc: dateRange.value?.[1].toISOString(),
      sortBy: 'occurredAt',
      sortDescending: true,
    })
    rows.value = result.items
    total.value = result.total
  } catch {
    ElMessage.error('审计日志加载失败。')
  } finally {
    loading.value = false
  }
}

function search() {
  query.page = 1
  void load()
}

function reset() {
  Object.assign(query, { page: 1, pageSize: settingsStore.defaultPageSize, search: '', entityType: '', action: '' })
  dateRange.value = undefined
  void load()
}

async function openDetail(row: AuditLogSummary) {
  detailVisible.value = true
  detailLoading.value = true
  selected.value = undefined
  try {
    selected.value = await getAuditLog(row.id)
  } catch {
    ElMessage.error('审计详情加载失败。')
    detailVisible.value = false
  } finally {
    detailLoading.value = false
  }
}

onMounted(load)
</script>

<template>
  <section class="page-stack audit-page">
    <el-card shadow="never">
      <div class="audit-filter-grid">
        <el-input
          v-model="query.search"
          clearable
          placeholder="摘要、业务编号、操作人"
          :prefix-icon="Search"
          @keyup.enter="search"
        />
        <el-select
          v-model="query.entityType"
          clearable
          filterable
          placeholder="对象类型"
        >
          <el-option
            v-for="item in entityTypes"
            :key="item.value"
            :label="item.label"
            :value="item.value"
          />
        </el-select>
        <el-input
          v-model="query.action"
          clearable
          placeholder="操作代码，例如 Updated"
          @keyup.enter="search"
        />
        <el-date-picker
          v-model="dateRange"
          type="datetimerange"
          start-placeholder="开始时间"
          end-placeholder="结束时间"
          range-separator="至"
        />
        <div class="audit-filter-actions">
          <el-button
            type="primary"
            :icon="Search"
            @click="search"
          >
            查询
          </el-button>
          <el-button
            :icon="Refresh"
            @click="reset"
          >
            重置
          </el-button>
        </div>
      </div>
    </el-card>

    <el-card shadow="never">
      <el-table
        v-loading="loading"
        class="flowhearth-data-table"
        :data="rows"
        border
        stripe
        row-class-name="clickable-row"
        @row-click="openDetail"
      >
        <el-table-column
          label="时间"
          width="170"
        >
          <template #default="scope">
            {{ formatChinaDateTime(scope.row.occurredAtUtc) }}
          </template>
        </el-table-column>
        <el-table-column
          label="操作人"
          min-width="140"
        >
          <template #default="scope">
            <strong>{{ scope.row.actorDisplayName || '系统' }}</strong>
            <p class="cell-secondary">
              {{ scope.row.actorUsername || '—' }}
            </p>
          </template>
        </el-table-column>
        <el-table-column
          prop="action"
          label="操作"
          min-width="150"
        />
        <el-table-column
          label="对象"
          min-width="180"
        >
          <template #default="scope">
            <el-tag size="small">
              {{ scope.row.entityType }}
            </el-tag>
            <span class="audit-entity-code">{{ scope.row.entityCode || `#${scope.row.entityId}` }}</span>
          </template>
        </el-table-column>
        <el-table-column
          prop="summary"
          label="摘要"
          min-width="280"
          show-overflow-tooltip
        />
      </el-table>
      <div class="table-pagination">
        <span>共 {{ total }} 条</span>
        <el-pagination
          v-model:current-page="query.page"
          v-model:page-size="query.pageSize"
          :total="total"
          :page-sizes="[10, 20, 50, 100]"
          layout="sizes, prev, pager, next"
          @change="load"
        />
      </div>
    </el-card>

    <el-drawer
      v-model="detailVisible"
      title="审计详情"
      size="min(760px, 96vw)"
      destroy-on-close
    >
      <div
        v-loading="detailLoading"
        class="audit-detail"
      >
        <template v-if="selected">
          <el-descriptions
            :column="2"
            border
          >
            <el-descriptions-item label="时间">
              {{ formatChinaDateTime(selected.occurredAtUtc) }}
            </el-descriptions-item>
            <el-descriptions-item label="操作人">
              {{ selected.actorDisplayName || '系统' }}
            </el-descriptions-item>
            <el-descriptions-item label="操作">
              {{ selected.action }}
            </el-descriptions-item>
            <el-descriptions-item label="对象">
              {{ selected.entityType }} · {{ selected.entityCode || `#${selected.entityId}` }}
            </el-descriptions-item>
            <el-descriptions-item
              label="摘要"
              :span="2"
            >
              {{ selected.summary }}
            </el-descriptions-item>
            <el-descriptions-item
              label="关联 ID"
              :span="2"
            >
              {{ selected.correlationId || '—' }}
            </el-descriptions-item>
          </el-descriptions>
          <div class="audit-json-grid">
            <section>
              <h4>变更前</h4>
              <pre>{{ formatAuditJson(selected.beforeJson) }}</pre>
            </section>
            <section>
              <h4>变更后</h4>
              <pre>{{ formatAuditJson(selected.afterJson) }}</pre>
            </section>
          </div>
        </template>
      </div>
    </el-drawer>
  </section>
</template>
