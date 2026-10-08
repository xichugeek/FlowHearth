<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, ref } from 'vue'

import { ApiError } from '../../api/problem-details'
import { useAuthStore } from '../../stores/auth'

const visible = defineModel<boolean>({ required: true })
const authStore = useAuthStore()
const saving = ref(false)
const form = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})
const errors = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})

type PasswordField = keyof typeof errors

function clearError(field: PasswordField) {
  errors[field] = ''
}

function resetErrors() {
  errors.currentPassword = ''
  errors.newPassword = ''
  errors.confirmPassword = ''
}

function reset() {
  form.currentPassword = ''
  form.newPassword = ''
  form.confirmPassword = ''
  resetErrors()
}

function hasRequiredCharacterTypes(password: string) {
  const characters = [...password]
  return characters.some((character) => /\p{Lu}/u.test(character))
    && characters.some((character) => /\p{Ll}/u.test(character))
    && characters.some((character) => /\p{Nd}/u.test(character))
    && characters.some((character) => /[^\p{L}\p{N}]/u.test(character))
}

function validate() {
  resetErrors()

  if (!form.currentPassword) {
    errors.currentPassword = '请输入当前密码。'
  }

  if (!form.newPassword) {
    errors.newPassword = '请输入新密码。'
  } else if (form.newPassword.length < 12 || form.newPassword.length > 128) {
    errors.newPassword = '新密码长度必须为 12 至 128 个字符。'
  } else if (!hasRequiredCharacterTypes(form.newPassword)) {
    errors.newPassword = '新密码必须同时包含大写字母、小写字母、数字和特殊字符。'
  }

  if (!form.confirmPassword) {
    errors.confirmPassword = '请再次输入新密码。'
  } else if (form.newPassword !== form.confirmPassword) {
    errors.confirmPassword = '两次输入的新密码不一致。'
  }

  return !Object.values(errors).some(Boolean)
}

function applyServerErrors(error: ApiError) {
  const validationErrors = error.problem?.errors
  for (const field of ['currentPassword', 'newPassword'] as const) {
    const message = validationErrors?.[field]?.[0]
    if (message) errors[field] = message
  }

  return Object.values(errors).find(Boolean)
    ?? error.problem?.detail
    ?? error.problem?.title
    ?? error.message
}

async function submit() {
  if (!validate()) {
    ElMessage.warning('请按输入框提示修正密码信息。')
    return
  }

  saving.value = true
  try {
    await authStore.changePassword(form.currentPassword, form.newPassword)
    ElMessage.success('密码已更新，其他旧会话将失效。')
    visible.value = false
    reset()
  } catch (error) {
    ElMessage.error(
      error instanceof ApiError
        ? applyServerErrors(error)
        : '密码修改失败。',
    )
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <el-dialog
    v-model="visible"
    draggable
    title="修改密码"
    width="min(480px, 92vw)"
    destroy-on-close
    @closed="reset"
  >
    <el-alert
      class="password-hint"
      type="info"
      :closable="false"
      title="至少 12 位，并包含大小写字母、数字和特殊字符。"
    />
    <el-form label-position="top">
      <el-form-item
        label="当前密码"
      >
        <el-input
          v-model="form.currentPassword"
          type="password"
          show-password
          autocomplete="current-password"
          @input="clearError('currentPassword')"
        />
        <p
          v-if="errors.currentPassword"
          class="password-field-error"
          role="alert"
        >
          {{ errors.currentPassword }}
        </p>
      </el-form-item>
      <el-form-item
        label="新密码"
      >
        <el-input
          v-model="form.newPassword"
          type="password"
          show-password
          autocomplete="new-password"
          maxlength="128"
          @input="clearError('newPassword')"
        />
        <p
          v-if="errors.newPassword"
          class="password-field-error"
          role="alert"
        >
          {{ errors.newPassword }}
        </p>
      </el-form-item>
      <el-form-item
        label="确认新密码"
      >
        <el-input
          v-model="form.confirmPassword"
          type="password"
          show-password
          autocomplete="new-password"
          maxlength="128"
          @input="clearError('confirmPassword')"
          @keyup.enter="submit"
        />
        <p
          v-if="errors.confirmPassword"
          class="password-field-error"
          role="alert"
        >
          {{ errors.confirmPassword }}
        </p>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="visible = false">
        取消
      </el-button>
      <el-button
        type="primary"
        :loading="saving"
        @click="submit"
      >
        保存新密码
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.password-field-error {
  width: 100%;
  margin: 5px 0 0;
  color: var(--el-color-danger);
  font-size: 12px;
  line-height: 1.35;
}
</style>
