import { flushPromises, mount } from '@vue/test-utils'
import { ElDatePicker, ElSelect } from 'element-plus'
import { createPinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listAuditLogs } from '../../api/audit'
import AuditLogsView from './AuditLogsView.vue'

vi.mock('../../api/audit', () => ({
  listAuditLogs: vi.fn(),
  getAuditLog: vi.fn(),
}))

describe('AuditLogsView filters', () => {
  beforeEach(() => {
    vi.mocked(listAuditLogs).mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 10,
      total: 0,
    })
  })

  it('queries the selected filters and clears all filters on reset', async () => {
    const wrapper = mount(AuditLogsView, {
      global: {
        plugins: [createPinia()],
        directives: { loading: () => undefined },
        stubs: { ElDrawer: true },
      },
    })

    try {
      await flushPromises()
      expect(listAuditLogs).toHaveBeenCalledOnce()

      const searchInput = wrapper.get('input[placeholder="摘要、业务编号、操作人"]')
      const actionInput = wrapper.get('input[placeholder="操作代码，例如 Updated"]')
      await searchInput.setValue('筛选验收')
      await actionInput.setValue('customer.created')
      wrapper.findComponent(ElSelect).vm.$emit('update:modelValue', 'customer')
      wrapper.findComponent(ElDatePicker).vm.$emit('update:modelValue', [
        new Date('2026-09-01T00:00:00Z'),
        new Date('2026-09-02T00:00:00Z'),
      ])
      await flushPromises()

      const buttons = wrapper.findAll('.audit-filter-actions button')
      expect(buttons.map(button => button.text())).toEqual(['查询', '重置'])
      await buttons[0]!.trigger('click')
      await flushPromises()
      expect(listAuditLogs).toHaveBeenLastCalledWith(expect.objectContaining({
        page: 1,
        search: '筛选验收',
        action: 'customer.created',
        entityType: 'customer',
        occurredFromUtc: '2026-09-01T00:00:00.000Z',
        occurredToUtc: '2026-09-02T00:00:00.000Z',
      }))

      await buttons[1]!.trigger('click')
      await flushPromises()
      expect(listAuditLogs).toHaveBeenLastCalledWith({
        page: 1,
        pageSize: 10,
        search: undefined,
        action: undefined,
        entityType: undefined,
        occurredFromUtc: undefined,
        occurredToUtc: undefined,
        sortBy: 'occurredAt',
        sortDescending: true,
      })
      expect((searchInput.element as HTMLInputElement).value).toBe('')
      expect((actionInput.element as HTMLInputElement).value).toBe('')
    } finally {
      wrapper.unmount()
    }
  })
})
