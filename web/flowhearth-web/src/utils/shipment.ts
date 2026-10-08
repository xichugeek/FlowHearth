import type { ShipmentEquipmentCandidate, ShipmentItemInput, ShipmentStatus } from '../types/finance'

export const shipmentStatusLabels: Record<ShipmentStatus, string> = {
  Preparing: '待出货',
  Shipped: '已出货',
  InTransit: '运输中',
  Received: '已签收',
  Cancelled: '已取消',
}

export function shipmentStatusType(status: ShipmentStatus) {
  return status === 'Received' ? 'success' : status === 'Cancelled' ? 'info' : status === 'Preparing' ? 'warning' : 'primary'
}

export function canEditShipment(status: ShipmentStatus) {
  return status !== 'Cancelled'
}

export function canArchiveShipment(status: ShipmentStatus) {
  return status === 'Preparing' || status === 'Cancelled'
}

export function shipmentActions(status: ShipmentStatus) {
  if (status === 'Preparing') return ['ship', 'cancel'] as const
  if (status === 'Shipped') return ['in-transit', 'receive'] as const
  if (status === 'InTransit') return ['receive'] as const
  return [] as const
}

export function filterShipmentEquipmentCandidates(
  candidates: ShipmentEquipmentCandidate[],
  items: ShipmentItemInput[],
) {
  const selected = new Set(items.flatMap(item => item.equipmentId ? [item.equipmentId] : []))
  return candidates.filter(candidate => !candidate.isDelivered && !selected.has(candidate.id))
}

export function appendShipmentEquipmentItems(
  items: ShipmentItemInput[],
  candidates: ShipmentEquipmentCandidate[],
  selectedIds: number[],
) {
  const result = [...items]
  const existing = new Set(result.flatMap(item => item.equipmentId ? [item.equipmentId] : []))
  for (const id of selectedIds) {
    const candidate = candidates.find(item => item.id === id)
    if (!candidate || candidate.isDelivered || existing.has(id)) continue
    result.push({
      equipmentId: candidate.id,
      itemName: candidate.name,
      manufacturer: candidate.manufacturer,
      model: candidate.model,
      quantity: 1,
      unit: '台',
    })
    existing.add(id)
  }
  return result
}

export function removeShipmentItem(items: ShipmentItemInput[], index: number) {
  return items.filter((_, itemIndex) => itemIndex !== index)
}

export function resolveShipmentProject(
  projects: Array<{ id: number; customerId: number }>,
  projectId: number,
) {
  const project = projects.find(item => item.id === projectId)
  return project ? { projectId: project.id, customerId: project.customerId } : null
}

export function validateShipmentItems(items: ShipmentItemInput[]) {
  if (items.length === 0) return '至少添加一条出货明细。'
  const equipmentIds = items.flatMap((item) => item.equipmentId ? [item.equipmentId] : [])
  if (new Set(equipmentIds).size !== equipmentIds.length) return '同一台设备不能重复添加。'
  if (items.some((item) => !item.itemName.trim() || !item.unit.trim() || item.quantity <= 0)) return '每条明细都需要名称、正数数量和单位。'
  return null
}
