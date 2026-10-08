export const lookupDictionaryCodes = {
  customerIndustry: 'customer.industry',
  projectMemberRole: 'project.member_role',
} as const

export const systemSettingKeys = {
  defaultPageSize: 'ui.default_page_size',
  businessTimeZone: 'business.time_zone',
  customerNumberPrefix: 'number.customer_prefix',
  opportunityNumberPrefix: 'number.opportunity_prefix',
  projectNumberPrefix: 'number.project_prefix',
  equipmentNumberPrefix: 'number.equipment_prefix',
  serviceNumberPrefix: 'number.service_prefix',
} as const

export type SystemSettingValueType = 'WholeNumber' | 'TimeZone' | 'Prefix'

export interface LookupItem {
  id: number
  dictionaryCode: string
  value: string
  label: string
  description: string | null
  sortOrder: number
  isActive: boolean
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface LookupDictionary {
  code: string
  name: string
  description: string
  items: LookupItem[]
}

export interface SystemSetting {
  key: string
  name: string
  value: string
  valueType: SystemSettingValueType
  description: string | null
  isPublic: boolean
  version: number
  updatedAtUtc: string
}

export interface NumberPrefixSettings {
  customer: string
  opportunity: string
  project: string
  equipment: string
  service: string
}

export interface RuntimeSettings {
  defaultPageSize: number
  businessTimeZone: string
  numberPrefixes: NumberPrefixSettings
  lookups: Record<string, LookupItem[]>
}

export interface SettingsAdministrationSnapshot {
  dictionaries: LookupDictionary[]
  settings: SystemSetting[]
}

export interface CreateLookupItemInput {
  dictionaryCode: string
  value: string
  label: string
  description: string | null
  sortOrder: number
}

export interface UpdateLookupItemInput {
  label: string
  description: string | null
  sortOrder: number
  isActive: boolean
  version: number
}

export interface UpdateSystemSettingInput {
  value: string
  version: number
}
