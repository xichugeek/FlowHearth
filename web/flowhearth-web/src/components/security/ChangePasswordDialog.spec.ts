import { createPinia, setActivePinia } from 'pinia'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '../../api/problem-details'
import { useAuthStore } from '../../stores/auth'
import ChangePasswordDialog from './ChangePasswordDialog.vue'

async function mountDialog() {
  const wrapper = mount(ChangePasswordDialog, {
    props: { modelValue: true },
    global: {
      stubs: { teleport: true },
    },
  })
  await flushPromises()
  return wrapper
}

function getSaveButton(wrapper: VueWrapper) {
  const button = wrapper.findAll('button')
    .find((candidate) => candidate.text() === '保存新密码')
  if (!button) throw new Error('Save password button was not rendered.')
  return button
}

describe('ChangePasswordDialog', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('shows a field error instead of silently submitting a short password', async () => {
    const wrapper = await mountDialog()
    const authStore = useAuthStore()
    const changePassword = vi.spyOn(authStore, 'changePassword').mockResolvedValue()
    const newPasswordInputs = wrapper.findAll('input[autocomplete="new-password"]')

    await wrapper.get('input[autocomplete="current-password"]')
      .setValue('CurrentPassword12!')
    await newPasswordInputs[0].setValue('short1')
    await newPasswordInputs[1].setValue('short1')
    await getSaveButton(wrapper).trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('新密码长度必须为 12 至 128 个字符。')
    expect(changePassword).not.toHaveBeenCalled()
  })

  it('submits a password that satisfies the documented policy', async () => {
    const wrapper = await mountDialog()
    const authStore = useAuthStore()
    const changePassword = vi.spyOn(authStore, 'changePassword').mockResolvedValue()
    const newPasswordInputs = wrapper.findAll('input[autocomplete="new-password"]')

    await wrapper.get('input[autocomplete="current-password"]')
      .setValue('CurrentPassword12!')
    await newPasswordInputs[0].setValue('ReplacementPassword12!')
    await newPasswordInputs[1].setValue('ReplacementPassword12!')
    await getSaveButton(wrapper).trigger('click')
    await flushPromises()

    expect(changePassword).toHaveBeenCalledWith(
      'CurrentPassword12!',
      'ReplacementPassword12!',
    )
    expect(wrapper.emitted('update:modelValue')).toContainEqual([false])
  })

  it('renders backend validation beside the matching password field', async () => {
    const wrapper = await mountDialog()
    const authStore = useAuthStore()
    vi.spyOn(authStore, 'changePassword').mockRejectedValue(new ApiError(
      'One or more validation errors occurred.',
      {
        status: 400,
        problem: {
          status: 400,
          title: '请求数据验证失败',
          errors: { currentPassword: ['当前密码不正确。'] },
        },
      },
    ))
    const newPasswordInputs = wrapper.findAll('input[autocomplete="new-password"]')

    await wrapper.get('input[autocomplete="current-password"]')
      .setValue('WrongCurrentPassword12!')
    await newPasswordInputs[0].setValue('ReplacementPassword12!')
    await newPasswordInputs[1].setValue('ReplacementPassword12!')
    await getSaveButton(wrapper).trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('当前密码不正确。')
  })
})
