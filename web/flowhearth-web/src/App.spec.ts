import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import App from './App.vue'

describe('App locale', () => {
  it('provides simplified Chinese pagination copy application-wide', () => {
    const wrapper = mount(App, {
      global: {
        stubs: {
          RouterView: true,
        },
      },
    })

    const locale = wrapper
      .findComponent({ name: 'ElConfigProvider' })
      .props('locale')

    expect(locale).toMatchObject({
      name: 'zh-cn',
      el: {
        pagination: {
          pagesize: '条/页',
          total: '共 {total} 条',
          prev: '上一页',
          next: '下一页',
        },
      },
    })
  })
})
