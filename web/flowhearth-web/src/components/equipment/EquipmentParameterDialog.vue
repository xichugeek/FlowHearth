<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { EquipmentParameterDetails, EquipmentParameterInput, UpdateEquipmentParameterInput } from '../../types/equipment'

const props = defineProps<{ modelValue: boolean; parameter?: EquipmentParameterDetails | null; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; save: [EquipmentParameterInput | UpdateEquipmentParameterInput] }>()
const form = reactive({ parameterGroup: '', name: '', value: '', unit: '', notes: '', sortOrder: 0 })
watch(() => [props.modelValue, props.parameter] as const, ([visible, item]) => { if (visible) Object.assign(form, { parameterGroup: item?.parameterGroup ?? '', name: item?.name ?? '', value: item?.value ?? '', unit: item?.unit ?? '', notes: item?.notes ?? '', sortOrder: item?.sortOrder ?? 0 }) }, { immediate: true })
function submit() {
  if (!form.name.trim() || !form.value.trim()) { ElMessage.warning('请填写参数名称和参数值。'); return }
  const input: EquipmentParameterInput = { parameterGroup: form.parameterGroup.trim() || null, name: form.name.trim(), value: form.value.trim(), unit: form.unit.trim() || null, notes: form.notes.trim() || null, sortOrder: form.sortOrder }
  emit('save', props.parameter ? { ...input, version: props.parameter.version } : input)
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="parameter ? '编辑参数' : '添加参数'"
    width="min(680px, 94vw)"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <div class="two-column-form">
        <el-form-item label="参数分组">
          <el-input
            v-model="form.parameterGroup"
            maxlength="100"
          />
        </el-form-item><el-form-item
          label="参数名称"
          required
        >
          <el-input
            v-model="form.name"
            maxlength="200"
          />
        </el-form-item>
      </div>
      <el-form-item
        label="参数值"
        required
      >
        <el-input
          v-model="form.value"
          type="textarea"
          :rows="3"
          maxlength="2000"
          placeholder="支持 Auto / Manual / Jog、5.5kW / 380VAC 等工业字符串"
        />
      </el-form-item>
      <div class="two-column-form">
        <el-form-item label="单位">
          <el-input
            v-model="form.unit"
            maxlength="50"
          />
        </el-form-item><el-form-item label="排序">
          <el-input-number
            v-model="form.sortOrder"
            :min="0"
            :max="100000"
            style="width: 100%"
          />
        </el-form-item>
      </div>
      <el-form-item label="说明">
        <el-input
          v-model="form.notes"
          type="textarea"
          :rows="2"
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
