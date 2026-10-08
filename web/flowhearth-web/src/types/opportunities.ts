import type { PagedResult } from './security'

export type OpportunityStage =
  | 'Lead'
  | 'Qualified'
  | 'Proposal'
  | 'Negotiation'
  | 'Won'
  | 'Lost'

export interface ProjectReference {
  id: number
  code: string
  name: string
  status: string
}

export interface OpportunitySummary {
  id: number
  code: string
  customerId: number
  customerCode: string
  customerName: string
  title: string
  stage: OpportunityStage
  expectedAmount: number
  probabilityPercent: number
  expectedCloseDate?: string | null
  isArchived: boolean
  convertedProject?: ProjectReference | null
  version: number
  updatedAtUtc: string
}

export interface OpportunityDetails extends OpportunitySummary {
  description?: string | null
  lostReason?: string | null
  archivedAtUtc?: string | null
  createdAtUtc: string
}

export interface OpportunityInput {
  customerId: number
  title: string
  stage?: OpportunityStage | null
  expectedAmount: number
  probabilityPercent: number
  expectedCloseDate?: string | null
  description?: string | null
}

export interface UpdateOpportunityInput extends Omit<OpportunityInput, 'stage'> {
  version: number
}

export interface OpportunityListParameters {
  page: number
  pageSize: number
  search?: string
  archive?: 'active' | 'archived' | 'all'
  stage?: OpportunityStage
  customerId?: number
  sortBy?:
    | 'code'
    | 'title'
    | 'stage'
    | 'expectedAmount'
    | 'probability'
    | 'expectedCloseDate'
    | 'updatedAt'
  sortDescending?: boolean
}

export type OpportunityPage = PagedResult<OpportunitySummary>
