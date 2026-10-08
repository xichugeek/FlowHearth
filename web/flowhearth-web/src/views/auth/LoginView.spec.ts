import { createPinia, setActivePinia } from 'pinia'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '../../stores/auth'
import LoginView from './LoginView.vue'

const { replaceRoute } = vi.hoisted(() => ({
  replaceRoute: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ replace: replaceRoute }),
}))

describe('LoginView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    replaceRoute.mockReset()
  })

  it('renders only the compact FlowHearth brand and login controls', async () => {
    const wrapper = mount(LoginView)

    expect(wrapper.text()).toContain('FlowHearth 业务管理平台')
    expect(wrapper.text()).toContain('用户名')
    expect(wrapper.text()).toContain('密码')
    expect(wrapper.text()).toContain('登录工作台')
    expect(wrapper.text()).toContain('© 2026 FlowHearth contributors')
    expect(wrapper.text()).not.toContain('SECURE ACCESS')
    expect(wrapper.text()).not.toContain('连接客户、项目、设备与服务。')
    expect(wrapper.text()).not.toContain('HttpOnly Cookie')
    expect(wrapper.findAll('.el-input__prefix')).toHaveLength(2)

    const passwordInput = wrapper.get('input[autocomplete="current-password"]')
    expect(passwordInput.attributes('type')).toBe('password')
    await passwordInput.setValue('test-password')
    await wrapper.get('.el-input__password').trigger('click')
    expect(wrapper.get('input[autocomplete="current-password"]').attributes('type')).toBe('text')
  })

  it('preserves the existing trimmed login and dashboard redirect flow', async () => {
    const wrapper = mount(LoginView)
    const authStore = useAuthStore()
    const login = vi.spyOn(authStore, 'login').mockResolvedValue()

    await wrapper.get('input[autocomplete="username"]').setValue('  administrator  ')
    await wrapper.get('input[autocomplete="current-password"]').setValue('test-password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(login).toHaveBeenCalledWith('administrator', 'test-password')
    expect(replaceRoute).toHaveBeenCalledWith('/dashboard')
  })
})
