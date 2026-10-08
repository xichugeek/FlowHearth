import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import FollowUpDialog from './FollowUpDialog.vue'

describe('FollowUpDialog reactivation guidance', () => {
  it('guides dormant-customer follow-up without changing other customers', async () => {
    const wrapper = mount(FollowUpDialog, {
      props: {
        modelValue: true,
        contacts: [],
        customerStatus: 'Dormant',
      },
      global: {
        stubs: {
          teleport: true,
          ElSelect: true,
          ElOption: true,
          ElDatePicker: true,
        },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('沉睡客户重新激活')
    expect(wrapper.text()).toContain('当前负责人、原设备现状和潜在需求')
    expect(wrapper.text()).toContain('下次联系/拜访时间')
    expect(wrapper.find('input').attributes('placeholder')).toBe(
      '例如：历史客户电话重联、预约登门拜访',
    )
    expect(wrapper.find('textarea').attributes('placeholder')).toContain(
      '采购计划以及下一步动作',
    )

    await wrapper.setProps({ customerStatus: 'Active' })
    expect(wrapper.text()).not.toContain('沉睡客户重新激活')
    wrapper.unmount()
  })
})
