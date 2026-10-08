<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'

import type { CustomerSummary } from '../../types/customers'
import type {
  OpportunityDetails,
  OpportunityInput,
  OpportunityStage,
  UpdateOpportunityInput,
} from '../../types/opportunities'
import {
  activeOpportunityStages,
  opportunityStageLabel,
} from '../../utils/opportunity-format'

const props = defineProps<{
  modelValue: boolean
  opportunity?: OpportunityDetails | null
  customers: CustomerSummary[]
  saving?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  'search-customer': [value: string]
  save: [value: OpportunityInput | UpdateOpportunityInput]
}>()

const form = reactive({
  customerId: undefined as number | undefined,
  title: '',
  stage: 'Lead' as OpportunityStage,
  expectedAmount: 0,
  probabilityPercent: 10,
  expectedCloseDate: '',
  description: '',
})

watch(
  () => [props.modelValue, props.opportunity] as const,
  ([visible, opportunity]) => {
    if (!visible) return
    Object.assign(form, {
      customerId: opportunity?.customerId,
      title: opportunity?.title ?? '',
      stage: opportunity?.stage ?? 'Lead',
      expectedAmount: opportunity?.expectedAmount ?? 0,
      probabilityPercent: opportunity?.probabilityPercent ?? 10,
      expectedCloseDate: opportunity?.expectedCloseDate?.slice(0, 10) ?? '',
      description: opportunity?.description ?? '',
    })
  },
  { immediate: true },
)

function submit() {
  if (!form.customerId) {
    ElMessage.warning('请选择客户。')
    return
  }
  if (!form.title.trim()) {
    ElMessage.warning('请填写商机名称。')
    return
  }
  if (form.expectedAmount < 0) {
    ElMessage.warning('预计金额不能小于 0。')
    return
  }
  if (form.probabilityPercent < 0 || form.probabilityPercent > 100) {
    ElMessage.warning('成交概率必须为 0 至 100。')
    return
  }

  const input: OpportunityInput = {
    customerId: form.customerId,
    title: form.title.trim(),
    stage: props.opportunity ? undefined : form.stage,
    expectedAmount: form.expectedAmount,
    probabilityPercent: form.probabilityPercent,
    expectedCloseDate: form.expectedCloseDate || null,
    description: form.description.trim() || null,
  }
  emit(
    'save',
    props.opportunity
      ? { ...input, stage: undefined, version: props.opportunity.version }
      : input,
  )
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="opportunity ? '编辑商机' : '新建商机'"
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
          clearable
          :remote-method="(value: string) => emit('search-customer', value)"
          placeholder="输入客户名称或编号"
          style="width: 100%"
        >
          <el-option
            v-for="customer in customers"
            :key="customer.id"
            :label="`${customer.name} · ${customer.code}`"
            :value="customer.id"
          />
        </el-select>
      </el-form-item>
      <div class="two-column-form">
        <el-form-item
          label="商机名称"
          required
        >
          <el-input
            v-model="form.title"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item
          v-if="!opportunity"
          label="初始阶段"
        >
          <el-select
            v-model="form.stage"
            style="width: 100%"
          >
            <el-option
              v-for="stage in activeOpportunityStages"
              :key="stage"
              :label="opportunityStageLabel(stage)"
              :value="stage"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="预计金额（元）">
          <el-input-number
            v-model="form.expectedAmount"
            :min="0"
            :precision="2"
            :step="10000"
            controls-position="right"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="成交概率（%）">
          <el-input-number
            v-model="form.probabilityPercent"
            :min="0"
            :max="100"
            :step="5"
            controls-position="right"
            style="width: 100%"
          />
        </el-form-item>
        <el-form-item label="预计成交日期">
          <el-date-picker
            v-model="form.expectedCloseDate"
            type="date"
            value-format="YYYY-MM-DD"
            format="YYYY-MM-DD"
            clearable
            style="width: 100%"
          />
        </el-form-item>
      </div>
      <el-form-item label="商机说明">
        <el-input
          v-model="form.description"
          type="textarea"
          :rows="4"
          maxlength="4000"
          show-word-limit
        />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="emit('update:modelValue', false)">
        取消
      </el-button>
      <el-button
        type="primary"
        :loading="saving"
        @click="submit"
      >
        保存
      </el-button>
    </template>
  </el-dialog>
</template>
