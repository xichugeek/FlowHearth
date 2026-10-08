import { describe, expect, it } from 'vitest'

import type { ShipmentEquipmentCandidate } from '../types/finance'
import { appendShipmentEquipmentItems, canArchiveShipment, canEditShipment, filterShipmentEquipmentCandidates, removeShipmentItem, resolveShipmentProject, shipmentActions, shipmentStatusLabels, shipmentStatusType, validateShipmentItems } from './shipment'

describe('shipment helpers', () => {
  it('provides stable Chinese status labels and permissions', () => {
    expect(shipmentStatusLabels.Preparing).toBe('待出货')
    expect(shipmentStatusLabels.Received).toBe('已签收')
    expect(canEditShipment('Received')).toBe(true)
    expect(canEditShipment('Cancelled')).toBe(false)
    expect(canArchiveShipment('Preparing')).toBe(true)
    expect(canArchiveShipment('Shipped')).toBe(false)
  })

  it('validates empty, incomplete and duplicate equipment items', () => {
    expect(validateShipmentItems([])).toContain('至少')
    expect(validateShipmentItems([{ itemName: '', quantity: 1, unit: '件' }])).toContain('名称')
    expect(validateShipmentItems([
      { equipmentId: 8, itemName: '设备', quantity: 1, unit: '台' },
      { equipmentId: 8, itemName: '设备', quantity: 1, unit: '台' },
    ])).toContain('重复')
  })

  it('accepts mixed equipment and ordinary material', () => {
    expect(validateShipmentItems([
      { equipmentId: 8, itemName: '控制柜', quantity: 1, unit: '台' },
      { itemName: '电缆', quantity: 30, unit: '米' },
    ])).toBeNull()
  })

  it('maps each visible status to the existing tag language', () => {
    expect(shipmentStatusLabels).toEqual({ Preparing: '待出货', Shipped: '已出货', InTransit: '运输中', Received: '已签收', Cancelled: '已取消' })
    expect(shipmentStatusType('Received')).toBe('success')
    expect(shipmentStatusType('Cancelled')).toBe('info')
  })

  it('only exposes valid shipment actions for the current status', () => {
    expect(shipmentActions('Preparing')).toEqual(['ship', 'cancel'])
    expect(shipmentActions('Shipped')).toEqual(['in-transit', 'receive'])
    expect(shipmentActions('InTransit')).toEqual(['receive'])
    expect(shipmentActions('Received')).toEqual([])
  })

  it('filters delivered and already selected project equipment', () => {
    const candidates: ShipmentEquipmentCandidate[] = [
      { id: 1, code: 'EQ-1', name: '控制柜', isDelivered: false },
      { id: 2, code: 'EQ-2', name: '主机', isDelivered: true },
      { id: 3, code: 'EQ-3', name: '平台', isDelivered: false },
    ]
    expect(filterShipmentEquipmentCandidates(candidates, [
      { equipmentId: 1, itemName: '控制柜', quantity: 1, unit: '台' },
    ]).map(item => item.id)).toEqual([3])
  })

  it('adds selected project equipment with copied master data', () => {
    const candidates: ShipmentEquipmentCandidate[] = [
      { id: 3, code: 'EQ-3', name: '平台', manufacturer: 'FlowHearth', model: 'TN-X', isDelivered: false },
    ]
    expect(appendShipmentEquipmentItems([], candidates, [3])).toEqual([
      { equipmentId: 3, itemName: '平台', manufacturer: 'FlowHearth', model: 'TN-X', quantity: 1, unit: '台' },
    ])
  })

  it('does not add a delivered or duplicate equipment item', () => {
    const candidates: ShipmentEquipmentCandidate[] = [
      { id: 1, code: 'EQ-1', name: '控制柜', isDelivered: false },
      { id: 2, code: 'EQ-2', name: '主机', isDelivered: true },
    ]
    const existing = [{ equipmentId: 1, itemName: '控制柜', quantity: 1, unit: '台' }]
    expect(appendShipmentEquipmentItems(existing, candidates, [1, 2])).toEqual(existing)
  })

  it('removes exactly the selected shipment line', () => {
    const items = [
      { itemName: '控制柜', quantity: 1, unit: '台' },
      { itemName: '电缆', quantity: 30, unit: '米' },
    ]
    expect(removeShipmentItem(items, 0)).toEqual([items[1]])
  })

  it('rejects non-positive quantity and blank unit', () => {
    expect(validateShipmentItems([{ itemName: '电缆', quantity: 0, unit: '米' }])).toContain('正数')
    expect(validateShipmentItems([{ itemName: '电缆', quantity: 1, unit: ' ' }])).toContain('单位')
  })

  it('binds the shipment customer to the selected project owner', () => {
    expect(resolveShipmentProject([
      { id: 10, customerId: 100 },
      { id: 20, customerId: 200 },
    ], 20)).toEqual({ projectId: 20, customerId: 200 })
    expect(resolveShipmentProject([{ id: 10, customerId: 100 }], 99)).toBeNull()
  })
})
