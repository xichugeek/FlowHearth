<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { EquipmentCategory, EquipmentComponentDetails, EquipmentComponentInput, UpdateEquipmentComponentInput } from '../../types/equipment'
import { equipmentCategories, equipmentCategoryLabel } from '../../utils/equipment-format'

const props = defineProps<{ modelValue: boolean; component?: EquipmentComponentDetails | null; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; save: [EquipmentComponentInput | UpdateEquipmentComponentInput] }>()
const form = reactive({ category: 'PLC' as EquipmentCategory, name: '', manufacturer: '', model: '', serialNumber: '', firmwareVersion: '', quantity: 1, installLocation: '', notes: '', sortOrder: 0 })
watch(() => [props.modelValue, props.component] as const, ([visible, item]) => { if (visible) Object.assign(form, { category: item?.category ?? 'PLC', name: item?.name ?? '', manufacturer: item?.manufacturer ?? '', model: item?.model ?? '', serialNumber: item?.serialNumber ?? '', firmwareVersion: item?.firmwareVersion ?? '', quantity: item?.quantity ?? 1, installLocation: item?.installLocation ?? '', notes: item?.notes ?? '', sortOrder: item?.sortOrder ?? 0 }) }, { immediate: true })
function submit() {
  if (!form.name.trim()) { ElMessage.warning('请填写组件名称。'); return }
  const input: EquipmentComponentInput = { category: form.category, name: form.name.trim(), manufacturer: form.manufacturer.trim() || null, model: form.model.trim() || null, serialNumber: form.serialNumber.trim() || null, firmwareVersion: form.firmwareVersion.trim() || null, quantity: form.quantity, installLocation: form.installLocation.trim() || null, notes: form.notes.trim() || null, sortOrder: form.sortOrder }
  emit('save', props.component ? { ...input, version: props.component.version } : input)
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="component ? '编辑组件' : '添加组件'"
    width="min(760px, 94vw)"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <div class="two-column-form">
        <el-form-item
          label="分类"
          required
        >
          <el-select
            v-model="form.category"
            style="width: 100%"
          >
            <el-option
              v-for="item in equipmentCategories"
              :key="item"
              :label="equipmentCategoryLabel(item)"
              :value="item"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          label="组件名称"
          required
        >
          <el-input
            v-model="form.name"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="制造商">
          <el-input
            v-model="form.manufacturer"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="型号">
          <el-input
            v-model="form.model"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="序列号">
          <el-input
            v-model="form.serialNumber"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="固件版本">
          <el-input
            v-model="form.firmwareVersion"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="数量">
          <el-input-number
            v-model="form.quantity"
            :min="1"
            :max="100000"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number
            v-model="form.sortOrder"
            :min="0"
            :max="100000"
            style="width: 100%"
          />
        </el-form-item>
      </div><el-form-item label="安装位置">
        <el-input
          v-model="form.installLocation"
          maxlength="500"
        />
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
