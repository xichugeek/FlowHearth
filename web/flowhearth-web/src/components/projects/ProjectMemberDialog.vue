<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { computed, reactive, watch } from 'vue'
import { useSettingsStore } from '../../stores/settings'
import { lookupDictionaryCodes } from '../../types/settings'
import type { ProjectMemberCandidate, ProjectMemberDetails, ProjectMemberInput, UpdateProjectMemberInput } from '../../types/projects'

const props = defineProps<{ modelValue: boolean; member?: ProjectMemberDetails | null; candidates: ProjectMemberCandidate[]; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; save: [ProjectMemberInput | UpdateProjectMemberInput] }>()
const settingsStore = useSettingsStore()
const roleOptions = computed(() =>
  settingsStore.lookupItems(lookupDictionaryCodes.projectMemberRole),
)
const form = reactive({ userId: undefined as number | undefined, roleName: '', responsibility: '' })
watch(() => [props.modelValue, props.member] as const, ([visible, member]) => { if (visible) Object.assign(form, { userId: member?.userId, roleName: member?.roleName ?? '', responsibility: member?.responsibility ?? '' }) }, { immediate: true })
function submit() {
  if (!form.userId || !form.roleName.trim()) { ElMessage.warning('请选择成员并填写项目角色。'); return }
  const base = { roleName: form.roleName.trim(), responsibility: form.responsibility.trim() || null }
  emit('save', props.member ? { ...base, version: props.member.version } : { ...base, userId: form.userId })
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="member ? '编辑项目成员' : '添加项目成员'"
    width="min(560px, 94vw)"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <el-form-item
        label="成员"
        required
      >
        <el-select
          v-model="form.userId"
          filterable
          :disabled="Boolean(member)"
          style="width: 100%"
        >
          <el-option
            v-for="item in candidates"
            :key="item.userId"
            :label="`${item.displayName} · ${item.username}`"
            :value="item.userId"
          />
        </el-select>
      </el-form-item><el-form-item
        label="项目角色"
        required
      >
        <el-select
          v-model="form.roleName"
          filterable
          allow-create
          default-first-option
          style="width: 100%"
        >
          <el-option
            v-for="item in roleOptions"
            :key="item.id"
            :label="item.label"
            :value="item.value"
          />
        </el-select>
      </el-form-item><el-form-item label="职责">
        <el-input
          v-model="form.responsibility"
          type="textarea"
          :rows="3"
          maxlength="1000"
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
