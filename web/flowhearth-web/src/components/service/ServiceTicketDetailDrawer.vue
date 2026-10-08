<script setup lang="ts">
import { Plus } from '@element-plus/icons-vue'
import EntityAttachmentsPanel from '../attachments/EntityAttachmentsPanel.vue'
import type { ServiceRecordDetails, ServiceStatus, ServiceTicketDetails } from '../../types/service'
import { formatChinaDateTime } from '../../utils/customer-format'
import { formatDowntime, isTerminalServiceStatus, servicePriorityLabel, servicePriorityTagType, serviceRecordTypeLabel, serviceStatusLabel, serviceStatusTagType, serviceStatusTargets } from '../../utils/service-format'
defineProps<{modelValue:boolean;ticket?:ServiceTicketDetails|null;canManage?:boolean;busy?:boolean}>()
const emit=defineEmits<{'update:modelValue':[boolean];edit:[];assign:[];transition:[ServiceStatus];archive:[];'add-record':[];'edit-record':[ServiceRecordDetails];'delete-record':[ServiceRecordDetails]}>()
function manual(record:ServiceRecordDetails){return record.recordType==='Note'||record.recordType==='Diagnosis'||record.recordType==='Action'}
</script>
<template>
  <el-drawer
    :model-value="modelValue"
    size="min(840px,97vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue',$event)"
  >
    <template #header>
      <div
        v-if="ticket"
        class="service-drawer-heading"
      >
        <div>
          <p class="section-kicker">
            {{ ticket.code }}
          </p><h3>{{ ticket.title }}</h3><p>{{ ticket.customerName }} · {{ ticket.equipmentName||ticket.projectName||'未关联设备/项目' }}</p>
        </div><div>
          <el-tag :type="servicePriorityTagType(ticket.priority)">
            {{ servicePriorityLabel(ticket.priority) }}
          </el-tag><el-tag :type="serviceStatusTagType(ticket.status)">
            {{ serviceStatusLabel(ticket.status) }}
          </el-tag>
        </div>
      </div>
    </template>
    <template v-if="ticket">
      <div
        v-if="canManage"
        class="detail-action-row"
      >
        <el-button
          :disabled="ticket.isArchived||isTerminalServiceStatus(ticket.status)"
          @click="emit('edit')"
        >
          编辑工单
        </el-button><el-button
          :disabled="ticket.isArchived||isTerminalServiceStatus(ticket.status)"
          @click="emit('assign')"
        >
          指派
        </el-button><el-dropdown
          v-if="!ticket.isArchived&&serviceStatusTargets(ticket.status).length"
          @command="emit('transition',$event as ServiceStatus)"
        >
          <el-button type="primary">
            变更状态
          </el-button><template #dropdown>
            <el-dropdown-menu>
              <el-dropdown-item
                v-for="target in serviceStatusTargets(ticket.status)"
                :key="target"
                :command="target"
              >
                {{ serviceStatusLabel(target) }}
              </el-dropdown-item>
            </el-dropdown-menu>
          </template>
        </el-dropdown><el-button
          :type="ticket.isArchived?'primary':'danger'"
          plain
          :loading="busy"
          @click="emit('archive')"
        >
          {{ ticket.isArchived?'恢复':'归档' }}
        </el-button>
      </div>
      <div class="service-overview-grid">
        <div><span>负责人</span><strong>{{ ticket.assignedDisplayName||'未指派' }}</strong></div><div><span>停机时长</span><strong>{{ formatDowntime(ticket.downtimeMinutes) }}</strong></div><div><span>报修时间</span><strong>{{ formatChinaDateTime(ticket.reportedAtUtc) }}</strong></div><div><span>响应 / 解决</span><strong>{{ ticket.respondedAtUtc?formatChinaDateTime(ticket.respondedAtUtc):'—' }} / {{ ticket.resolvedAtUtc?formatChinaDateTime(ticket.resolvedAtUtc):'—' }}</strong></div>
      </div>
      <div class="service-description">
        <strong>问题描述</strong><p>{{ ticket.description||'暂无问题描述。' }}</p>
      </div><div
        v-if="ticket.rootCause||ticket.solution"
        class="service-resolution-grid"
      >
        <article><strong>根因</strong><p>{{ ticket.rootCause||'尚未确认' }}</p></article><article><strong>解决方案</strong><p>{{ ticket.solution||'尚未填写' }}</p></article>
      </div>
      <div class="detail-tab-toolbar">
        <h4>服务时间线（{{ ticket.records.length }}）</h4><el-button
          v-if="canManage&&!ticket.isArchived&&!isTerminalServiceStatus(ticket.status)"
          type="primary"
          plain
          :icon="Plus"
          @click="emit('add-record')"
        >
          添加记录
        </el-button>
      </div><el-timeline>
        <el-timeline-item
          v-for="item in ticket.records"
          :key="item.id"
          :type="item.recordType==='StatusChange'?'primary':item.recordType==='Assignment'?'warning':'success'"
          :timestamp="formatChinaDateTime(item.occurredAtUtc)"
          placement="top"
        >
          <article class="service-record-card">
            <header>
              <el-tag size="small">
                {{ serviceRecordTypeLabel(item.recordType) }}
              </el-tag><span>{{ item.createdByDisplayName||'系统' }}{{ item.durationMinutes!=null?` · ${item.durationMinutes} 分钟`:'' }}</span>
            </header><p>{{ item.content }}</p><footer v-if="canManage&&manual(item)&&!isTerminalServiceStatus(ticket.status)">
              <el-button
                link
                @click="emit('edit-record',item)"
              >
                编辑
              </el-button><el-button
                link
                type="danger"
                @click="emit('delete-record',item)"
              >
                删除
              </el-button>
            </footer>
          </article>
        </el-timeline-item>
      </el-timeline><el-empty
        v-if="ticket.records.length===0"
        description="暂无服务记录"
      /><EntityAttachmentsPanel
        entity-type="ServiceTicket"
        :entity-id="ticket.id"
      />
    </template>
  </el-drawer>
</template>
