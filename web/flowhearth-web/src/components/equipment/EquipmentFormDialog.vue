<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { CustomerSummary } from '../../types/customers'
import type { EquipmentCategory, EquipmentDetails, EquipmentInput, UpdateEquipmentInput } from '../../types/equipment'
import type { ProjectSummary } from '../../types/projects'
import { equipmentCategories, equipmentCategoryLabel } from '../../utils/equipment-format'

const props = defineProps<{ modelValue: boolean; equipment?: EquipmentDetails | null; customers: CustomerSummary[]; projects: ProjectSummary[]; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; 'search-customer': [string]; 'customer-change': [number]; save: [EquipmentInput | UpdateEquipmentInput] }>()
const form = reactive({ customerId: undefined as number | undefined, projectId: undefined as number | undefined, name: '', category: 'PLC' as EquipmentCategory, manufacturer: '', model: '', serialNumber: '', installLocation: '', commissionedDate: '', notes: '' })

watch(() => [props.modelValue, props.equipment] as const, ([visible, equipment]) => {
  if (!visible) return
  Object.assign(form, { customerId: equipment?.customerId, projectId: equipment?.projectId ?? undefined, name: equipment?.name ?? '', category: equipment?.category ?? 'PLC', manufacturer: equipment?.manufacturer ?? '', model: equipment?.model ?? '', serialNumber: equipment?.serialNumber ?? '', installLocation: equipment?.installLocation ?? '', commissionedDate: equipment?.commissionedDate?.slice(0, 10) ?? '', notes: equipment?.notes ?? '' })
  if (equipment?.customerId) emit('customer-change', equipment.customerId)
}, { immediate: true })

function customerChanged(value: number) { form.projectId = undefined; emit('customer-change', value) }
function submit() {
  if (!form.customerId || !form.name.trim()) { ElMessage.warning('请选择客户并填写设备名称。'); return }
  const input: EquipmentInput = { customerId: form.customerId, projectId: form.projectId ?? null, name: form.name.trim(), category: form.category, manufacturer: form.manufacturer.trim() || null, model: form.model.trim() || null, serialNumber: form.serialNumber.trim() || null, installLocation: form.installLocation.trim() || null, commissionedDate: form.commissionedDate || null, notes: form.notes.trim() || null }
  emit('save', props.equipment ? { ...input, version: props.equipment.version } : input)
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="equipment ? '编辑设备' : '新建设备'"
    width="min(820px, 94vw)"
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
            style="width: 100%"
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
        <el-form-item label="所属项目">
          <el-select
            v-model="form.projectId"
            clearable
            filterable
            style="width: 100%"
            :disabled="!form.customerId"
          >
            <el-option
              v-for="item in projects"
              :key="item.id"
              :label="`${item.name} · ${item.code}`"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          label="设备名称"
          required
        >
          <el-input
            v-model="form.name"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item
          label="设备分类"
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
        <el-form-item label="投产日期">
          <el-date-picker
            v-model="form.commissionedDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
          />
        </el-form-item>
      </div>
      <el-form-item label="安装位置">
        <el-input
          v-model="form.installLocation"
          maxlength="500"
        />
      </el-form-item>
      <el-form-item label="设备说明">
        <el-input
          v-model="form.notes"
          type="textarea"
          :rows="3"
          maxlength="4000"
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
