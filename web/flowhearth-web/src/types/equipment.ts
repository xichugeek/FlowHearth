import type { PagedResult } from './security'

export type EquipmentCategory =
  | 'PLC'
  | 'HMI'
  | 'Servo'
  | 'VFD'
  | 'IPC'
  | 'Sensor'
  | 'Robot'
  | 'Camera'
  | 'Network'
  | 'Other'

export interface EquipmentSummary {
  id: number
  code: string
  customerId: number
  customerCode: string
  customerName: string
  projectId?: number | null
  projectCode?: string | null
  projectName?: string | null
  name: string
  category: EquipmentCategory
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  isArchived: boolean
  componentCount: number
  parameterCount: number
  versionCount: number
  version: number
  updatedAtUtc: string
}

export interface EquipmentComponentDetails {
  id: number
  equipmentId: number
  category: EquipmentCategory
  name: string
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  firmwareVersion?: string | null
  quantity: number
  installLocation?: string | null
  notes?: string | null
  sortOrder: number
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface EquipmentParameterDetails {
  id: number
  equipmentId: number
  parameterGroup?: string | null
  name: string
  value: string
  unit?: string | null
  notes?: string | null
  sortOrder: number
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface EquipmentVersionDetails {
  id: number
  equipmentId: number
  versionType: string
  versionLabel: string
  gitCommit?: string | null
  changelog?: string | null
  releasedDate?: string | null
  notes?: string | null
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface EquipmentDetails extends EquipmentSummary {
  installLocation?: string | null
  commissionedDate?: string | null
  notes?: string | null
  archivedAtUtc?: string | null
  createdAtUtc: string
  components: EquipmentComponentDetails[]
  parameters: EquipmentParameterDetails[]
  versions: EquipmentVersionDetails[]
}

export interface EquipmentInput {
  customerId: number
  projectId?: number | null
  name: string
  category: EquipmentCategory
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  installLocation?: string | null
  commissionedDate?: string | null
  notes?: string | null
}

export interface UpdateEquipmentInput extends EquipmentInput { version: number }

export interface EquipmentComponentInput {
  category: EquipmentCategory
  name: string
  manufacturer?: string | null
  model?: string | null
  serialNumber?: string | null
  firmwareVersion?: string | null
  quantity: number
  installLocation?: string | null
  notes?: string | null
  sortOrder: number
}

export interface UpdateEquipmentComponentInput extends EquipmentComponentInput { version: number }

export interface EquipmentParameterInput {
  parameterGroup?: string | null
  name: string
  value: string
  unit?: string | null
  notes?: string | null
  sortOrder: number
}

export interface UpdateEquipmentParameterInput extends EquipmentParameterInput { version: number }

export interface EquipmentVersionInput {
  versionType: string
  versionLabel: string
  gitCommit?: string | null
  changelog?: string | null
  releasedDate?: string | null
  notes?: string | null
}

export interface UpdateEquipmentVersionInput extends EquipmentVersionInput { version: number }

export interface EquipmentListParameters {
  page: number
  pageSize: number
  search?: string
  archive?: 'active' | 'archived' | 'all'
  category?: EquipmentCategory
  customerId?: number
  projectId?: number
  sortBy?: 'code' | 'name' | 'category' | 'customer' | 'project' | 'commissionedDate' | 'updatedAt'
  sortDescending?: boolean
}

export type EquipmentPage = PagedResult<EquipmentSummary>
