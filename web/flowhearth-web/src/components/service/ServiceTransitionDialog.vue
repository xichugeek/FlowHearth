<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { ServiceStatus, ServiceTicketDetails } from '../../types/service'
import { serviceStatusLabel } from '../../utils/service-format'
const props = defineProps<{ modelValue: boolean; ticket?: ServiceTicketDetails | null; target?: ServiceStatus | null; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; save: [{ status: ServiceStatus; rootCause: string | null; solution: string | null; downtimeMinutes: number | null }] }>()
const form = reactive({ rootCause: '', solution: '', downtimeMinutes: 0 })
watch(() => [props.modelValue, props.ticket, props.target] as const, ([visible, ticket]) => { if (visible) Object.assign(form, { rootCause: ticket?.rootCause ?? '', solution: ticket?.solution ?? '', downtimeMinutes: ticket?.downtimeMinutes ?? 0 }) }, { immediate: true })
function submit() { if (!props.target) return; if (props.target === 'Resolved' && !form.solution.trim()) { ElMessage.warning('解决工单前必须填写解决方案。'); return } emit('save', { status: props.target, rootCause: form.rootCause.trim() || null, solution: form.solution.trim() || null, downtimeMinutes: form.downtimeMinutes }) }
</script>
<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="target ? `变更为${serviceStatusLabel(target)}` : '变更状态'"
    width="min(650px,94vw)"
    @update:model-value="emit('update:modelValue',$event)"
  >
    <el-form label-position="top">
      <el-form-item label="根因">
        <el-input
          v-model="form.rootCause"
          type="textarea"
          :rows="2"
          maxlength="8000"
        />
      </el-form-item><el-form-item
        label="解决方案"
        :required="target==='Resolved'"
      >
        <el-input
          v-model="form.solution"
          type="textarea"
          :rows="3"
          maxlength="8000"
        />
      </el-form-item><el-form-item label="停机分钟">
        <el-input-number
          v-model="form.downtimeMinutes"
          :min="0"
          :max="52560000"
          style="width:100%"
        />
      </el-form-item>
    </el-form><template #footer>
      <el-button @click="emit('update:modelValue',false)">
        取消
      </el-button><el-button
        type="primary"
        :loading="saving"
        @click="submit"
      >
        确认变更
      </el-button>
    </template>
  </el-dialog>
</template>
