import { describe, expect, it } from 'vitest'

import {
  customerRegionLabel,
  customerRegionParameters,
  customerRegionPath,
} from './china-regions'

describe('China customer regions', () => {
  it('converts a selected hierarchy into API parameters and a Chinese label', () => {
    const region = {
      provinceCode: '42',
      cityCode: '4201',
      districtCode: '420106',
    }

    expect(customerRegionPath(region)).toEqual(['42', '4201', '420106'])
    expect(customerRegionParameters(customerRegionPath(region))).toEqual(region)
    expect(customerRegionLabel(region)).toBe('湖北省 / 武汉市 / 武昌区')
  })

  it('keeps partial selections valid and omits unselected levels', () => {
    expect(customerRegionParameters(['42'])).toEqual({
      provinceCode: '42',
      cityCode: undefined,
      districtCode: undefined,
    })
    expect(customerRegionLabel({})).toBe('—')
  })
})
