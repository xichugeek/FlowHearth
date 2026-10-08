<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { EquipmentVersionDetails, EquipmentVersionInput, UpdateEquipmentVersionInput } from '../../types/equipment'

const props = defineProps<{ modelValue: boolean; equipmentVersion?: EquipmentVersionDetails | null; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; save: [EquipmentVersionInput | UpdateEquipmentVersionInput] }>()
const form = reactive({ versionType: 'PLC Program', versionLabel: '', gitCommit: '', changelog: '', releasedDate: '', notes: '' })
watch(() => [props.modelValue, props.equipmentVersion] as const, ([visible, item]) => { if (visible) Object.assign(form, { versionType: item?.versionType ?? 'PLC Program', versionLabel: item?.versionLabel ?? '', gitCommit: item?.gitCommit ?? '', changelog: item?.changelog ?? '', releasedDate: item?.releasedDate?.slice(0, 10) ?? '', notes: item?.notes ?? '' }) }, { immediate: true })
function submit() {
  if (!form.versionType.trim() || !form.versionLabel.trim()) { ElMessage.warning('请填写版本类型和版本标识。'); return }
  const input: EquipmentVersionInput = { versionType: form.versionType.trim(), versionLabel: form.versionLabel.trim(), gitCommit: form.gitCommit.trim() || null, changelog: form.changelog.trim() || null, releasedDate: form.releasedDate || null, notes: form.notes.trim() || null }
  emit('save', props.equipmentVersion ? { ...input, version: props.equipmentVersion.version } : input)
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="equipmentVersion ? '编辑版本' : '添加版本'"
    width="min(700px, 94vw)"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <div class="two-column-form">
        <el-form-item
          label="版本类型"
          required
        >
          <el-input
            v-model="form.versionType"
            maxlength="50"
            placeholder="PLC Program / HMI Project / Firmware"
          />
        </el-form-item><el-form-item
          label="版本标识"
          required
        >
          <el-input
            v-model="form.versionLabel"
            maxlength="100"
            placeholder="v1.2.0"
          />
        </el-form-item><el-form-item label="Git 提交或引用">
          <el-input
            v-model="form.gitCommit"
            maxlength="100"
            placeholder="commit SHA / tag / branch"
          />
        </el-form-item><el-form-item label="发布日期">
          <el-date-picker
            v-model="form.releasedDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
          />
        </el-form-item>
      </div>
      <el-form-item label="变更记录">
        <el-input
          v-model="form.changelog"
          type="textarea"
          :rows="4"
          maxlength="8000"
        />
      </el-form-item><el-form-item label="环境/工具说明">
        <el-input
          v-model="form.notes"
          type="textarea"
          :rows="2"
          maxlength="2000"
          placeholder="例如 TIA Portal V20"
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
