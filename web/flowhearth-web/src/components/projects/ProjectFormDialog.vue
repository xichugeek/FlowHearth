<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { CustomerSummary } from '../../types/customers'
import type { ProjectDetails, ProjectInput, UpdateProjectInput } from '../../types/projects'

const props = defineProps<{ modelValue: boolean; project?: ProjectDetails | null; customers: CustomerSummary[]; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; 'search-customer': [string]; save: [ProjectInput | UpdateProjectInput] }>()
const form = reactive({ customerId: undefined as number | undefined, name: '', contractAmount: 0, progressPercent: 0, description: '', plannedStartDate: '', plannedEndDate: '' })

watch(() => [props.modelValue, props.project] as const, ([visible, project]) => {
  if (!visible) return
  Object.assign(form, { customerId: project?.customerId, name: project?.name ?? '', contractAmount: project?.contractAmount ?? 0, progressPercent: project?.progressPercent ?? 0, description: project?.description ?? '', plannedStartDate: project?.plannedStartDate?.slice(0, 10) ?? '', plannedEndDate: project?.plannedEndDate?.slice(0, 10) ?? '' })
}, { immediate: true })

function submit() {
  if (!form.customerId || !form.name.trim()) { ElMessage.warning('请选择客户并填写项目名称。'); return }
  if (form.plannedStartDate && form.plannedEndDate && form.plannedEndDate < form.plannedStartDate) { ElMessage.warning('计划结束日期不能早于开始日期。'); return }
  const input: ProjectInput = { customerId: form.customerId, name: form.name.trim(), contractAmount: form.contractAmount, progressPercent: form.progressPercent, description: form.description.trim() || null, plannedStartDate: form.plannedStartDate || null, plannedEndDate: form.plannedEndDate || null }
  emit('save', props.project ? { ...input, version: props.project.version } : input)
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="project ? '编辑项目' : '新建项目'"
    width="min(760px, 94vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
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
        >
          <el-option
            v-for="item in customers"
            :key="item.id"
            :label="`${item.name} · ${item.code}`"
            :value="item.id"
          />
        </el-select>
      </el-form-item>
      <div class="two-column-form">
        <el-form-item
          label="项目名称"
          required
        >
          <el-input
            v-model="form.name"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="合同金额（元）">
          <el-input-number
            v-model="form.contractAmount"
            :min="0"
            :precision="2"
            :step="10000"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="项目进度（%）">
          <el-input-number
            v-model="form.progressPercent"
            :min="0"
            :max="99.99"
            :precision="2"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="计划开始">
          <el-date-picker
            v-model="form.plannedStartDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="计划结束">
          <el-date-picker
            v-model="form.plannedEndDate"
            type="date"
            value-format="YYYY-MM-DD"
            style="width: 100%"
          />
        </el-form-item>
      </div>
      <el-form-item label="项目说明">
        <el-input
          v-model="form.description"
          type="textarea"
          :rows="4"
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
