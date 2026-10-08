<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { CustomerSummary } from '../../types/customers'
import type { EquipmentSummary } from '../../types/equipment'
import type { ProjectSummary } from '../../types/projects'
import type { ServiceAssignee, ServicePriority, ServiceTicketDetails, ServiceTicketInput, UpdateServiceTicketInput } from '../../types/service'
import { servicePriorities, servicePriorityLabel } from '../../utils/service-format'

const props = defineProps<{ modelValue: boolean; ticket?: ServiceTicketDetails | null; customers: CustomerSummary[]; projects: ProjectSummary[]; equipment: EquipmentSummary[]; assignees: ServiceAssignee[]; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; 'search-customer': [string]; 'customer-change': [number]; save: [ServiceTicketInput | UpdateServiceTicketInput] }>()
const form = reactive({ customerId: undefined as number | undefined, projectId: undefined as number | undefined, equipmentId: undefined as number | undefined, title: '', description: '', priority: 'P3' as ServicePriority, reportedAtUtc: '', assignedUserId: undefined as number | undefined, rootCause: '', solution: '', downtimeMinutes: 0 })
watch(() => [props.modelValue, props.ticket] as const, ([visible, ticket]) => { if (!visible) return; Object.assign(form, { customerId: ticket?.customerId, projectId: ticket?.projectId ?? undefined, equipmentId: ticket?.equipmentId ?? undefined, title: ticket?.title ?? '', description: ticket?.description ?? '', priority: ticket?.priority ?? 'P3', reportedAtUtc: ticket?.reportedAtUtc?.slice(0, 16) ?? '', assignedUserId: ticket?.assignedUserId ?? undefined, rootCause: ticket?.rootCause ?? '', solution: ticket?.solution ?? '', downtimeMinutes: ticket?.downtimeMinutes ?? 0 }); if (ticket?.customerId) emit('customer-change', ticket.customerId) }, { immediate: true })
function customerChanged(value: number) { form.projectId = undefined; form.equipmentId = undefined; emit('customer-change', value) }
function submit() { if (!form.customerId || !form.title.trim()) { ElMessage.warning('请选择客户并填写工单标题。'); return } const base: ServiceTicketInput = { customerId: form.customerId, projectId: form.projectId ?? null, equipmentId: form.equipmentId ?? null, title: form.title.trim(), description: form.description.trim() || null, priority: form.priority, reportedAtUtc: form.reportedAtUtc ? new Date(form.reportedAtUtc).toISOString() : null, assignedUserId: form.assignedUserId ?? null }; emit('save', props.ticket ? { ...base, assignedUserId: undefined, rootCause: form.rootCause.trim() || null, solution: form.solution.trim() || null, downtimeMinutes: form.downtimeMinutes, version: props.ticket.version } as UpdateServiceTicketInput : base) }
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="ticket ? '编辑服务工单' : '新建服务工单'"
    width="min(840px, 94vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <div class="two-column-form">
        <el-form-item
          label="客户"
          required
        >
          <el-select
            v-model="form.customerId"
            filterable
            remote
            :remote-method="(value: string) => emit('search-customer', value)"
            style="width:100%"
            @change="customerChanged"
          >
            <el-option
              v-for="item in customers"
              :key="item.id"
              :label="`${item.name} · ${item.code}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="项目">
          <el-select
            v-model="form.projectId"
            clearable
            filterable
            style="width:100%"
          >
            <el-option
              v-for="item in projects"
              :key="item.id"
              :label="`${item.name} · ${item.code}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="设备">
          <el-select
            v-model="form.equipmentId"
            clearable
            filterable
            style="width:100%"
          >
            <el-option
              v-for="item in equipment"
              :key="item.id"
              :label="`${item.name} · ${item.code}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          v-if="!ticket"
          label="初始指派"
        >
          <el-select
            v-model="form.assignedUserId"
            clearable
            filterable
            style="width:100%"
          >
            <el-option
              v-for="item in assignees"
              :key="item.userId"
              :label="`${item.displayName} · ${item.username}`"
              :value="item.userId"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          label="工单标题"
          required
        >
          <el-input
            v-model="form.title"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="优先级">
          <el-select
            v-model="form.priority"
            style="width:100%"
          >
            <el-option
              v-for="item in servicePriorities"
              :key="item"
              :label="servicePriorityLabel(item)"
              :value="item"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="报修时间">
          <el-date-picker
            v-model="form.reportedAtUtc"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm"
            style="width:100%"
          />
        </el-form-item>
        <el-form-item
          v-if="ticket"
          label="停机分钟"
        >
          <el-input-number
            v-model="form.downtimeMinutes"
            :min="0"
            :max="52560000"
            style="width:100%"
          />
        </el-form-item>
      </div><el-form-item label="问题描述">
        <el-input
          v-model="form.description"
          type="textarea"
          :rows="3"
          maxlength="8000"
        />
      </el-form-item>
      <template v-if="ticket">
        <el-form-item label="根因">
          <el-input
            v-model="form.rootCause"
            type="textarea"
            :rows="2"
            maxlength="8000"
          />
        </el-form-item><el-form-item label="解决方案">
          <el-input
            v-model="form.solution"
            type="textarea"
            :rows="3"
            maxlength="8000"
          />
        </el-form-item>
      </template>
    </el-form><template #footer>
      <el-button @click="emit('update:modelValue', false)">
        取消
      </el-button><el-button
        type="primary"
        :loading="saving"
        @click="submit"
      >
        保存
      </el-button>
    </template>
  </el-dialog>
</template>
