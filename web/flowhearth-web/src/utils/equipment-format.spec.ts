import { describe, expect, it } from 'vitest'
import { equipmentCategories, equipmentCategoryLabel, equipmentModelLabel } from './equipment-format'

describe('equipment formatting', () => {
  it('contains every required industrial category', () => {
    expect(equipmentCategories).toEqual(['PLC', 'HMI', 'Servo', 'VFD', 'IPC', 'Sensor', 'Robot', 'Camera', 'Network', 'Other'])
    expect(equipmentCategoryLabel('Servo')).toBe('伺服')
  })

  it('formats manufacturer and model without empty separators', () => {
    expect(equipmentModelLabel('Siemens', 'S7-1515')).toBe('Siemens · S7-1515')
    expect(equipmentModelLabel(null, null)).toBe('未填写品牌/型号')
  })
})
