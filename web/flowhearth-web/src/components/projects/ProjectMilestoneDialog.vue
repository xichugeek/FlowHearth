<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { ProjectMilestoneDetails, ProjectMilestoneInput, UpdateProjectMilestoneInput } from '../../types/projects'

const props = defineProps<{ modelValue: boolean; milestone?: ProjectMilestoneDetails | null; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; save: [ProjectMilestoneInput | UpdateProjectMilestoneInput] }>()
const form = reactive({ name: '', dueDate: '', isCompleted: false, notes: '', sortOrder: 0 })
watch(() => [props.modelValue, props.milestone] as const, ([visible, milestone]) => { if (visible) Object.assign(form, { name: milestone?.name ?? '', dueDate: milestone?.dueDate?.slice(0, 10) ?? '', isCompleted: Boolean(milestone?.completedAtUtc), notes: milestone?.notes ?? '', sortOrder: milestone?.sortOrder ?? 0 }) }, { immediate: true })
function submit() {
  if (!form.name.trim()) { ElMessage.warning('请填写里程碑名称。'); return }
  const base = { name: form.name.trim(), dueDate: form.dueDate || null, isCompleted: form.isCompleted, notes: form.notes.trim() || null, sortOrder: form.sortOrder }
  emit('save', props.milestone ? { ...base, version: props.milestone.version } : base)
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="milestone ? '编辑里程碑' : '添加里程碑'"
    width="min(600px, 94vw)"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <el-form-item
        label="名称"
        required
      >
        <el-input
          v-model="form.name"
          maxlength="200"
        />
      </el-form-item><div class="two-column-form">
        <el-form-item label="计划日期">
          <el-date-picker
            v-model="form.dueDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
          />
        </el-form-item><el-form-item label="排序">
          <el-input-number
            v-model="form.sortOrder"
            :min="0"
            :max="100000"
            style="width: 100%"
          />
        </el-form-item>
      </div><el-form-item>
        <el-checkbox v-model="form.isCompleted">
          已完成
        </el-checkbox>
      </el-form-item><el-form-item label="说明">
        <el-input
          v-model="form.notes"
          type="textarea"
          :rows="3"
          maxlength="2000"
        />
      </el-form-item>
    </el-form>
    <template #footer>
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
