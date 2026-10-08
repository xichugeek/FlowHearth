import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import FinanceChart from './FinanceChart.vue'

const resize = vi.fn()
const setOption = vi.fn()
const dispose = vi.fn()
const observe = vi.fn()
const disconnect = vi.fn()
let resizeCallback: (() => void) | undefined

vi.mock('echarts/core', () => ({
  init: vi.fn(() => ({ resize, setOption, dispose })),
  use: vi.fn(),
}))

describe('FinanceChart', () => {
  beforeEach(() => {
    resize.mockReset()
    setOption.mockReset()
    dispose.mockReset()
    observe.mockReset()
    disconnect.mockReset()
    resizeCallback = undefined
    vi.stubGlobal('ResizeObserver', class {
      constructor(callback: () => void) { resizeCallback = callback }
      observe = observe
      disconnect = disconnect
    })
  })

  it('renders options and responds to container resizing', async () => {
    const wrapper = mount(FinanceChart, {
      props: {
        option: { series: [{ type: 'bar', data: [1] }] },
        description: '测试图表',
      },
    })

    await vi.waitFor(() => expect(setOption).toHaveBeenCalled())
    resizeCallback?.()
    expect(observe).toHaveBeenCalled()
    expect(resize).toHaveBeenCalled()
    expect(wrapper.attributes('aria-label')).toBe('测试图表')

    wrapper.unmount()
    expect(disconnect).toHaveBeenCalled()
    expect(dispose).toHaveBeenCalled()
  })
})
