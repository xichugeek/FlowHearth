<script setup lang="ts">
import EntityAttachmentsPanel from '../attachments/EntityAttachmentsPanel.vue'
import type {
  OpportunityDetails,
  OpportunityStage,
} from '../../types/opportunities'
import { formatChinaDateTime } from '../../utils/customer-format'
import {
  activeOpportunityStages,
  formatCurrency,
  isTerminalOpportunityStage,
  opportunityStageLabel,
  opportunityStageTagType,
} from '../../utils/opportunity-format'

const transitionStages: OpportunityStage[] = [
  ...activeOpportunityStages,
  'Won',
  'Lost',
]

defineProps<{
  modelValue: boolean
  opportunity?: OpportunityDetails | null
  canManage?: boolean
  busy?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  edit: []
  transition: [stage: OpportunityStage]
  archive: []
  convert: []
}>()

function transition(stage: OpportunityStage) {
  emit('transition', stage)
}
</script>

<template>
  <el-drawer
    :model-value="modelValue"
    size="min(680px, 96vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <template #header>
      <div
        v-if="opportunity"
        class="opportunity-drawer-heading"
      >
        <div>
          <p class="section-kicker">
            {{ opportunity.code }}
          </p>
          <h3>{{ opportunity.title }}</h3>
          <p>{{ opportunity.customerName }} · {{ opportunity.customerCode }}</p>
        </div>
        <el-tag :type="opportunityStageTagType(opportunity.stage)">
          {{ opportunityStageLabel(opportunity.stage) }}
        </el-tag>
      </div>
    </template>

    <template v-if="opportunity">
      <div
        v-if="canManage"
        class="detail-action-row"
      >
        <el-button
          :disabled="opportunity.isArchived || isTerminalOpportunityStage(opportunity.stage)"
          @click="emit('edit')"
        >
          编辑资料
        </el-button>
        <el-dropdown
          v-if="!opportunity.isArchived && !isTerminalOpportunityStage(opportunity.stage)"
          trigger="click"
          @command="transition"
        >
          <el-button type="primary">
            推进阶段
          </el-button>
          <template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item
                v-for="stage in transitionStages"
                :key="stage"
                :command="stage"
                :disabled="stage === opportunity.stage"
              >
                {{ opportunityStageLabel(stage) }}
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown>
        <el-button
          v-if="opportunity.stage === 'Won'"
          type="success"
          :disabled="Boolean(opportunity.convertedProject) || opportunity.isArchived"
          :loading="busy"
          @click="emit('convert')"
        >
          {{ opportunity.convertedProject ? '已生成项目' : '生成项目' }}
        </el-button>
        <el-button
          :type="opportunity.isArchived ? 'primary' : 'danger'"
          plain
          @click="emit('archive')"
        >
          {{ opportunity.isArchived ? '恢复' : '归档' }}
        </el-button>
      </div>

      <el-descriptions
        :column="2"
        border
        class="opportunity-descriptions"
      >
        <el-descriptions-item label="预计金额">
          {{ formatCurrency(opportunity.expectedAmount) }}
        </el-descriptions-item>
        <el-descriptions-item label="成交概率">
          {{ opportunity.probabilityPercent }}%
        </el-descriptions-item>
        <el-descriptions-item label="加权金额">
          {{ formatCurrency(opportunity.expectedAmount * opportunity.probabilityPercent / 100) }}
        </el-descriptions-item>
        <el-descriptions-item label="预计成交">
          {{ opportunity.expectedCloseDate?.slice(0, 10) || '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="最近更新">
          {{ formatChinaDateTime(opportunity.updatedAtUtc) }}
        </el-descriptions-item>
        <el-descriptions-item label="状态">
          {{ opportunity.isArchived ? '已归档' : '在用' }}
        </el-descriptions-item>
      </el-descriptions>

      <div class="opportunity-detail-block">
        <h4>商机说明</h4>
        <p>{{ opportunity.description || '暂无说明。' }}</p>
      </div>
      <div
        v-if="opportunity.lostReason"
        class="opportunity-detail-block lost-reason"
      >
        <h4>丢单原因</h4>
        <p>{{ opportunity.lostReason }}</p>
      </div>
      <div
        v-if="opportunity.convertedProject"
        class="converted-project-card"
      >
        <span>已生成项目</span>
        <strong>{{ opportunity.convertedProject.code }}</strong>
        <p>{{ opportunity.convertedProject.name }}</p>
      </div>
      <EntityAttachmentsPanel
        entity-type="Opportunity"
        :entity-id="opportunity.id"
      />
    </template>
  </el-drawer>
</template>
