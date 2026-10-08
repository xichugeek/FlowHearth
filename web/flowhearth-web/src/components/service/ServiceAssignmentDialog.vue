<script setup lang="ts">
import { ref, watch } from 'vue'
import type { ServiceAssignee, ServiceTicketDetails } from '../../types/service'
const props = defineProps<{ modelValue: boolean; ticket?: ServiceTicketDetails | null; assignees: ServiceAssignee[]; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; save: [number | null] }>()
const userId = ref<number | null>(null)
watch(() => [props.modelValue, props.ticket] as const, ([visible, ticket]) => { if (visible) userId.value = ticket?.assignedUserId ?? null }, { immediate: true })
</script>
<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    title="指派服务工单"
    width="min(520px,94vw)"
    @update:model-value="emit('update:modelValue',$event)"
  >
    <el-select
      v-model="userId"
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
    </el-select><template #footer>
      <el-button @click="emit('update:modelValue',false)">
        取消
      </el-button><el-button
        type="primary"
        :loading="saving"
        @click="emit('save',userId)"
      >
        保存指派
      </el-button>
    </template>
  </el-dialog>
</template>
