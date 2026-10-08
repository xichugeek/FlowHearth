<script setup lang="ts">
import { Lock, User } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { reactive } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { ApiError } from '../../api/problem-details'
import { useAuthStore } from '../../stores/auth'

const authStore = useAuthStore()
const route = useRoute()
const router = useRouter()
const form = reactive({ username: '', password: '' })

async function submit() {
  if (!form.username.trim() || !form.password) {
    ElMessage.warning('请输入用户名和密码。')
    return
  }

  try {
    await authStore.login(form.username.trim(), form.password)
    const redirect = typeof route.query.redirect === 'string'
      && route.query.redirect.startsWith('/')
      ? route.query.redirect
      : '/dashboard'
    await router.replace(redirect)
  } catch (error) {
    ElMessage.error(
      error instanceof ApiError ? error.message : '登录失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <main class="login-page">
    <section
      class="login-form-card"
      aria-labelledby="login-title"
    >
      <header class="login-brand">
        <div
          class="login-brand-mark"
          aria-hidden="true"
        >
          F
        </div>
        <h1 id="login-title">
          FlowHearth 业务管理平台
        </h1>
      </header>

      <el-form
        class="login-form"
        label-position="top"
        @submit.prevent="submit"
      >
        <el-form-item label="用户名">
          <el-input
            v-model="form.username"
            size="large"
            autocomplete="username"
            placeholder="请输入用户名"
            :prefix-icon="User"
            @keyup.enter="submit"
          />
        </el-form-item>
        <el-form-item label="密码">
          <el-input
            v-model="form.password"
            size="large"
            type="password"
            autocomplete="current-password"
            show-password
            placeholder="请输入密码"
            :prefix-icon="Lock"
            @keyup.enter="submit"
          />
        </el-form-item>
        <el-button
          class="login-submit"
          type="primary"
          size="large"
          native-type="submit"
          :loading="authStore.loading"
        >
          登录工作台
        </el-button>
      </el-form>
    </section>

    <footer class="login-footer">
      © 2026 FlowHearth contributors
    </footer>
  </main>
</template>
