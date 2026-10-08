<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'

import type {
  ContactDetails,
  ContactInput,
  UpdateContactInput,
} from '../../types/customers'

const props = defineProps<{
  modelValue: boolean
  contact?: ContactDetails | null
  saving?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  save: [value: ContactInput | UpdateContactInput]
}>()

const form = reactive({
  name: '',
  title: '',
  department: '',
  mobile: '',
  phone: '',
  email: '',
  weChat: '',
  isPrimary: false,
  notes: '',
})

watch(
  () => [props.modelValue, props.contact] as const,
  ([visible, contact]) => {
    if (!visible) return
    Object.assign(form, {
      name: contact?.name ?? '',
      title: contact?.title ?? '',
      department: contact?.department ?? '',
      mobile: contact?.mobile ?? '',
      phone: contact?.phone ?? '',
      email: contact?.email ?? '',
      weChat: contact?.weChat ?? '',
      isPrimary: contact?.isPrimary ?? false,
      notes: contact?.notes ?? '',
    })
  },
  { immediate: true },
)

function submit() {
  if (!form.name.trim()) {
    ElMessage.warning('请填写联系人姓名。')
    return
  }

  const input: ContactInput = {
    name: form.name.trim(),
    title: nullable(form.title),
    department: nullable(form.department),
    mobile: nullable(form.mobile),
    phone: nullable(form.phone),
    email: nullable(form.email),
    weChat: nullable(form.weChat),
    isPrimary: form.isPrimary,
    notes: nullable(form.notes),
  }
  emit(
    'save',
    props.contact ? { ...input, version: props.contact.version } : input,
  )
}

function nullable(value: string) {
  return value.trim() || null
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="contact ? '编辑联系人' : '添加联系人'"
    width="min(660px, 94vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <div class="two-column-form">
        <el-form-item
          label="姓名"
          required
        >
          <el-input
            v-model="form.name"
            maxlength="100"
          />
        </el-form-item>
        <el-form-item label="主要联系人">
          <el-switch v-model="form.isPrimary" />
        </el-form-item>
        <el-form-item label="职务">
          <el-input
            v-model="form.title"
            maxlength="100"
          />
        </el-form-item>
        <el-form-item label="部门">
          <el-input
            v-model="form.department"
            maxlength="100"
          />
        </el-form-item>
        <el-form-item label="手机">
          <el-input
            v-model="form.mobile"
            maxlength="50"
          />
        </el-form-item>
        <el-form-item label="电话">
          <el-input
            v-model="form.phone"
            maxlength="50"
          />
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input
            v-model="form.email"
            maxlength="254"
          />
        </el-form-item>
        <el-form-item label="微信">
          <el-input
            v-model="form.weChat"
            maxlength="100"
          />
        </el-form-item>
      </div>
      <el-form-item label="备注">
        <el-input
          v-model="form.notes"
          type="textarea"
          :rows="3"
          maxlength="1000"
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
