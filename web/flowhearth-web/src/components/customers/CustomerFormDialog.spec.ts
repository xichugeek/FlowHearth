import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'

import type { CustomerDetails } from '../../types/customers'
import CustomerFormDialog from './CustomerFormDialog.vue'

const customer: CustomerDetails = {
  id: 1,
  code: 'CU-2026-0001',
  name: '测试客户',
  shortName: null,
  industry: '工业自动化',
  status: 'Prospect',
  level: 'A',
  phone: null,
  email: null,
  website: null,
  provinceCode: '42',
  cityCode: '4201',
  districtCode: '420106',
  address: null,
  notes: null,
  isArchived: false,
  archivedAtUtc: null,
  version: 3,
  createdAtUtc: '2026-09-01T00:00:00Z',
  updatedAtUtc: '2026-09-01T00:00:00Z',
  contacts: [],
  followUps: [],
}

describe('CustomerFormDialog classification', () => {
  it('loads and submits editable customer status and level', async () => {
    const wrapper = mount(CustomerFormDialog, {
      props: { modelValue: true, customer },
      global: {
        plugins: [createPinia()],
        stubs: {
          teleport: true,
          ElSelect: true,
          ElOption: true,
          ElCascader: true,
        },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('客户状态')
    expect(wrapper.text()).toContain('客户等级')
    const selects = wrapper.findAllComponents({ name: 'ElSelect' })
    expect(selects).toHaveLength(3)
    expect(selects[1]!.props('modelValue')).toBe('Prospect')
    expect(selects[2]!.props('modelValue')).toBe('A')
    const region = wrapper.getComponent({ name: 'ElCascader' })
    expect(region.props('modelValue')).toEqual(['42', '4201', '420106'])

    selects[1]!.vm.$emit('update:modelValue', 'Dormant')
    selects[2]!.vm.$emit('update:modelValue', 'B')
    region.vm.$emit('update:modelValue', ['44', '4403', '440305'])
    await flushPromises()
    const saveButton = wrapper.findAll('button')
      .find((button) => button.text() === '保存')
    if (!saveButton) throw new Error('Save customer button was not rendered.')
    await saveButton.trigger('click')

    expect(wrapper.emitted('save')).toContainEqual([
      expect.objectContaining({
        name: '测试客户',
        status: 'Dormant',
        level: 'B',
        provinceCode: '44',
        cityCode: '4403',
        districtCode: '440305',
        version: 3,
      }),
    ])
    wrapper.unmount()
  })
})
