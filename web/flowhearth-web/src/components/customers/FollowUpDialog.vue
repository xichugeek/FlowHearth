<script setup lang="ts">
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { reactive, watch } from 'vue'

import type {
  ContactDetails,
  CustomerFollowUpInput,
  CustomerFollowUpMethod,
  CustomerStatus,
} from '../../types/customers'

const props = defineProps<{
  modelValue: boolean
  contacts: ContactDetails[]
  customerStatus?: CustomerStatus
  saving?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  save: [value: CustomerFollowUpInput]
}>()

const form = reactive({
  contactId: null as number | null,
  method: 'Phone' as CustomerFollowUpMethod,
  occurredAt: '',
  summary: '',
  details: '',
  nextFollowUpAt: '',
})

const methods: Array<{ value: CustomerFollowUpMethod; label: string }> = [
  { value: 'Phone', label: '电话' },
  { value: 'Visit', label: '拜访' },
  { value: 'Email', label: '邮件' },
  { value: 'WeChat', label: '微信' },
  { value: 'Other', label: '其他' },
]

watch(
  () => props.modelValue,
  (visible) => {
    if (!visible) return
    Object.assign(form, {
      contactId: null,
      method: 'Phone',
      occurredAt: dayjs().format('YYYY-MM-DD HH:mm'),
      summary: '',
      details: '',
      nextFollowUpAt: '',
    })
  },
  { immediate: true },
)

function submit() {
  if (!form.summary.trim()) {
    ElMessage.warning('请填写跟进摘要。')
    return
  }
  const occurredAt = dayjs(form.occurredAt)
  if (!occurredAt.isValid()) {
    ElMessage.warning('请选择有效的跟进时间。')
    return
  }
  const nextAt = form.nextFollowUpAt ? dayjs(form.nextFollowUpAt) : null
  if (nextAt && (!nextAt.isValid() || !nextAt.isAfter(occurredAt))) {
    ElMessage.warning('下次跟进时间必须晚于本次跟进时间。')
    return
  }

  emit('save', {
    contactId: form.contactId,
    method: form.method,
    occurredAtUtc: occurredAt.toISOString(),
    summary: form.summary.trim(),
    details: form.details.trim() || null,
    nextFollowUpAtUtc: nextAt?.toISOString() ?? null,
  })
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    title="记录客户跟进"
    width="min(620px, 94vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-alert
      v-if="customerStatus === 'Dormant'"
      class="dormant-follow-up-guide"
      title="沉睡客户重新激活"
      description="首次联系建议先核实当前负责人、原设备现状和潜在需求；不急于推销，并设置明确的下次联系或拜访时间。"
      type="warning"
      show-icon
      :closable="false"
    />
    <el-form label-position="top">
      <div class="two-column-form">
        <el-form-item
          label="跟进方式"
          required
        >
          <el-select v-model="form.method">
            <el-option
              v-for="item in methods"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="关联联系人">
          <el-select
            v-model="form.contactId"
            clearable
            placeholder="可选"
          >
            <el-option
              v-for="contact in contacts"
              :key="contact.id"
              :label="contact.name"
              :value="contact.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          label="跟进时间"
          required
        >
          <el-date-picker
            v-model="form.occurredAt"
            type="datetime"
            value-format="YYYY-MM-DD HH:mm"
            format="YYYY-MM-DD HH:mm"
          />
        </el-form-item>
        <el-form-item label="下次联系/拜访时间">
          <el-date-picker
            v-model="form.nextFollowUpAt"
            type="datetime"
            value-format="YYYY-MM-DD HH:mm"
            format="YYYY-MM-DD HH:mm"
            clearable
          />
        </el-form-item>
      </div>
      <el-form-item
        label="跟进摘要"
        required
      >
        <el-input
          v-model="form.summary"
          maxlength="300"
          placeholder="例如：历史客户电话重联、预约登门拜访"
        />
      </el-form-item>
      <el-form-item label="详细记录">
        <el-input
          v-model="form.details"
          type="textarea"
          :rows="5"
          maxlength="4000"
          placeholder="建议记录：当前负责人、原设备现状、现阶段问题或需求、采购计划以及下一步动作"
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
        保存跟进
      </el-button>
    </template>
  </el-dialog>
</template>
