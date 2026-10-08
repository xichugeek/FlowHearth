import type { PagedResult } from './security'

export type CustomerFollowUpMethod =
  | 'Phone'
  | 'Visit'
  | 'Email'
  | 'WeChat'
  | 'Other'

export type CustomerStatus = 'Prospect' | 'Active' | 'Dormant' | 'Lost'
export type CustomerLevel = 'Unrated' | 'A' | 'B' | 'C'

export interface CustomerSummary {
  id: number
  code: string
  name: string
  shortName?: string | null
  industry?: string | null
  status: CustomerStatus
  level: CustomerLevel
  phone?: string | null
  email?: string | null
  isArchived: boolean
  nextFollowUpAtUtc?: string | null
  contactCount: number
  version: number
  updatedAtUtc: string
  primaryContactName?: string | null
  primaryContactMethod?: string | null
}

export interface ContactDetails {
  id: number
  customerId: number
  name: string
  title?: string | null
  department?: string | null
  mobile?: string | null
  phone?: string | null
  email?: string | null
  weChat?: string | null
  isPrimary: boolean
  notes?: string | null
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface CustomerFollowUpDetails {
  id: number
  customerId: number
  contactId?: number | null
  contactName?: string | null
  method: CustomerFollowUpMethod
  occurredAtUtc: string
  summary: string
  details?: string | null
  nextFollowUpAtUtc?: string | null
  createdByDisplayName?: string | null
  createdAtUtc: string
}

export interface CustomerDetails {
  id: number
  code: string
  name: string
  shortName?: string | null
  industry?: string | null
  status: CustomerStatus
  level: CustomerLevel
  phone?: string | null
  email?: string | null
  website?: string | null
  provinceCode?: string | null
  cityCode?: string | null
  districtCode?: string | null
  address?: string | null
  notes?: string | null
  isArchived: boolean
  archivedAtUtc?: string | null
  version: number
  createdAtUtc: string
  updatedAtUtc: string
  contacts: ContactDetails[]
  followUps: CustomerFollowUpDetails[]
}

export interface CustomerInput {
  name: string
  shortName?: string | null
  industry?: string | null
  status: CustomerStatus
  level: CustomerLevel
  phone?: string | null
  email?: string | null
  website?: string | null
  provinceCode?: string | null
  cityCode?: string | null
  districtCode?: string | null
  address?: string | null
  notes?: string | null
}

export interface UpdateCustomerInput extends CustomerInput {
  version: number
  clearRegion?: boolean
}

export interface ContactInput {
  name: string
  title?: string | null
  department?: string | null
  mobile?: string | null
  phone?: string | null
  email?: string | null
  weChat?: string | null
  isPrimary: boolean
  notes?: string | null
}

export interface UpdateContactInput extends ContactInput {
  version: number
}

export interface CustomerFollowUpInput {
  contactId?: number | null
  method: CustomerFollowUpMethod
  occurredAtUtc: string
  summary: string
  details?: string | null
  nextFollowUpAtUtc?: string | null
}

export interface CustomerListParameters {
  page: number
  pageSize: number
  search?: string
  archive?: 'active' | 'archived' | 'all'
  status?: CustomerStatus
  level?: CustomerLevel
  provinceCode?: string
  cityCode?: string
  districtCode?: string
  sortBy?: 'code' | 'name' | 'updatedAt' | 'nextFollowUpAt'
  sortDescending?: boolean
  nextFollowUpBeforeUtc?: string
}

export type CustomerPage = PagedResult<CustomerSummary>
