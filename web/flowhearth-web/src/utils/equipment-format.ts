import type { EquipmentCategory } from '../types/equipment'

export const equipmentCategories: EquipmentCategory[] = [
  'PLC', 'HMI', 'Servo', 'VFD', 'IPC', 'Sensor', 'Robot', 'Camera', 'Network', 'Other',
]

const categoryLabels: Record<EquipmentCategory, string> = {
  PLC: 'PLC', HMI: 'HMI', Servo: '伺服', VFD: '变频器', IPC: '工控机',
  Sensor: '传感器', Robot: '机器人', Camera: '视觉相机', Network: '工业网络', Other: '其他',
}

export function equipmentCategoryLabel(category: EquipmentCategory) {
  return categoryLabels[category]
}

export function equipmentModelLabel(manufacturer?: string | null, model?: string | null) {
  return [manufacturer?.trim(), model?.trim()].filter(Boolean).join(' · ') || '未填写品牌/型号'
}
