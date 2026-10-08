<script setup lang="ts">
import { Plus } from '@element-plus/icons-vue'
import { computed, ref, watch } from 'vue'
import EntityAttachmentsPanel from '../attachments/EntityAttachmentsPanel.vue'
import ProjectFinancePanel from '../finance/ProjectFinancePanel.vue'
import ProjectShipmentPanel from '../shipments/ProjectShipmentPanel.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import type { ProjectDetails, ProjectMemberDetails, ProjectMilestoneDetails, ProjectStatus } from '../../types/projects'
import { formatCurrency } from '../../utils/opportunity-format'
import { isTerminalProjectStatus, projectStatusLabel, projectStatusTagType, projectStatusTargets } from '../../utils/project-format'

const props = defineProps<{ modelValue: boolean; project?: ProjectDetails | null; canManage?: boolean; busy?: boolean; initialTab?: string }>()
const emit = defineEmits<{
  'update:modelValue': [boolean]
  edit: []
  transition: [ProjectStatus]
  archive: []
  'add-member': []
  'edit-member': [ProjectMemberDetails]
  'delete-member': [ProjectMemberDetails]
  'add-milestone': []
  'edit-milestone': [ProjectMilestoneDetails]
  'delete-milestone': [ProjectMilestoneDetails]
}>()
const auth = useAuthStore()
const canViewFinance = computed(() => auth.canAny([permissions.receivablesView]))
const canViewShipments = computed(() => auth.canAny([permissions.shipmentsView]))
const canManageShipments = computed(() => auth.canAny([permissions.shipmentsManage]))
const activeTab = ref('members')
watch(
  () => [props.modelValue, props.initialTab] as const,
  ([visible, tab]) => {
    if (visible) activeTab.value = tab === 'finance' && canViewFinance.value ? 'finance' : 'members'
  },
  { immediate: true },
)
</script>

<template>
  <el-drawer
    :model-value="modelValue"
    size="min(760px, 96vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <template #header>
      <div
        v-if="project"
        class="project-drawer-heading"
      >
        <div>
          <p class="section-kicker">
            {{ project.code }}
          </p><h3>{{ project.name }}</h3><p>{{ project.customerName }} · {{ project.customerCode }}</p>
        </div><el-tag :type="projectStatusTagType(project.status)">
          {{ projectStatusLabel(project.status) }}
        </el-tag>
      </div>
    </template>
    <template v-if="project">
      <div
        v-if="canManage"
        class="detail-action-row"
      >
        <el-button
          :disabled="project.isArchived || isTerminalProjectStatus(project.status)"
          @click="emit('edit')"
        >
          编辑项目
        </el-button><el-dropdown
          v-if="!project.isArchived && projectStatusTargets(project.status).length"
          @command="emit('transition', $event as ProjectStatus)"
        >
          <el-button type="primary">
            变更状态
          </el-button><template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item
                v-for="target in projectStatusTargets(project.status)"
                :key="target"
                :command="target"
              >
                {{ projectStatusLabel(target) }}
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown><el-button
          :type="project.isArchived ? 'primary' : 'danger'"
          plain
          @click="emit('archive')"
        >
          {{ project.isArchived ? '恢复' : '归档' }}
        </el-button>
      </div>
      <div class="project-overview-grid">
        <div><span>合同金额</span><strong>{{ formatCurrency(project.contractAmount) }}</strong></div><div><span>交付进度</span><strong>{{ project.progressPercent }}%</strong></div><div><span>计划周期</span><strong>{{ project.plannedStartDate?.slice(0, 10) || '—' }} 至 {{ project.plannedEndDate?.slice(0, 10) || '—' }}</strong></div>
      </div>
      <el-progress
        :percentage="project.progressPercent"
        :status="project.status === 'Completed' ? 'success' : undefined"
        class="project-progress"
      />
      <p class="project-description">
        {{ project.description || '暂无项目说明。' }}
      </p>
      <el-alert
        v-if="project.sourceOpportunityCode"
        :title="`来源商机 ${project.sourceOpportunityCode}`"
        type="success"
        :closable="false"
      />
      <el-tabs
        v-model="activeTab"
        class="project-detail-tabs"
      >
        <el-tab-pane
          :label="`项目成员 (${project.members.length})`"
          name="members"
        >
          <div class="detail-tab-toolbar">
            <el-button
              v-if="canManage && !project.isArchived && !isTerminalProjectStatus(project.status)"
              type="primary"
              plain
              :icon="Plus"
              @click="emit('add-member')"
            >
              添加成员
            </el-button>
          </div>
          <div class="project-member-grid">
            <article
              v-for="member in project.members"
              :key="member.id"
            >
              <div><strong>{{ member.displayName }}</strong><span>{{ member.username }}</span></div><el-tag>{{ member.roleName }}</el-tag><p>{{ member.responsibility || '暂无职责说明' }}</p><footer v-if="canManage && !isTerminalProjectStatus(project.status)">
                <el-button
                  link
                  @click="emit('edit-member', member)"
                >
                  编辑
                </el-button><el-button
                  link
                  type="danger"
                  @click="emit('delete-member', member)"
                >
                  移除
                </el-button>
              </footer>
            </article>
          </div><el-empty
            v-if="project.members.length === 0"
            description="暂无项目成员"
          />
        </el-tab-pane>
        <el-tab-pane
          :label="`里程碑 (${project.milestones.length})`"
          name="milestones"
        >
          <div class="detail-tab-toolbar">
            <el-button
              v-if="canManage && !project.isArchived && !isTerminalProjectStatus(project.status)"
              type="primary"
              plain
              :icon="Plus"
              @click="emit('add-milestone')"
            >
              添加里程碑
            </el-button>
          </div>
          <el-timeline>
            <el-timeline-item
              v-for="item in project.milestones"
              :key="item.id"
              :type="item.completedAtUtc ? 'success' : 'primary'"
              :hollow="!item.completedAtUtc"
              :timestamp="item.dueDate?.slice(0, 10) || '未设日期'"
              placement="top"
            >
              <div class="milestone-card">
                <div>
                  <strong>{{ item.name }}</strong><el-tag
                    size="small"
                    :type="item.completedAtUtc ? 'success' : 'info'"
                  >
                    {{ item.completedAtUtc ? '已完成' : '待完成' }}
                  </el-tag>
                </div><p>{{ item.notes || '暂无说明' }}</p><footer v-if="canManage && !isTerminalProjectStatus(project.status)">
                  <el-button
                    link
                    @click="emit('edit-milestone', item)"
                  >
                    编辑
                  </el-button><el-button
                    link
                    type="danger"
                    @click="emit('delete-milestone', item)"
                  >
                    删除
                  </el-button>
                </footer>
              </div>
            </el-timeline-item>
          </el-timeline><el-empty
            v-if="project.milestones.length === 0"
            description="暂无里程碑"
          />
        </el-tab-pane>
        <el-tab-pane
          label="附件"
          name="attachments"
        >
          <EntityAttachmentsPanel
            entity-type="Project"
            :entity-id="project.id"
          />
        </el-tab-pane>
        <el-tab-pane
          v-if="canViewShipments"
          label="交付"
          name="shipments"
        >
          <ProjectShipmentPanel
            :project-id="project.id"
            :can-manage="canManageShipments && !project.isArchived"
          />
        </el-tab-pane>
        <el-tab-pane
          v-if="canViewFinance"
          label="经营财务"
          name="finance"
        >
          <ProjectFinancePanel :project-id="project.id" />
        </el-tab-pane>
      </el-tabs>
    </template>
  </el-drawer>
</template>
