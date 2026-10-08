import type { PagedResult } from './security'

export type ProjectStatus =
  | 'Planning'
  | 'Active'
  | 'OnHold'
  | 'Completed'
  | 'Cancelled'

export interface ProjectSummary {
  id: number
  code: string
  customerId: number
  customerCode: string
  customerName: string
  name: string
  status: ProjectStatus
  contractAmount: number
  progressPercent: number
  plannedEndDate?: string | null
  isArchived: boolean
  sourceOpportunityCode?: string | null
  memberCount: number
  milestoneCount: number
  version: number
  updatedAtUtc: string
}

export interface ProjectMemberDetails {
  id: number
  projectId: number
  userId: number
  username: string
  displayName: string
  roleName: string
  responsibility?: string | null
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ProjectMilestoneDetails {
  id: number
  projectId: number
  name: string
  dueDate?: string | null
  completedAtUtc?: string | null
  notes?: string | null
  sortOrder: number
  version: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ProjectMemberCandidate {
  userId: number
  username: string
  displayName: string
  email?: string | null
}

export interface ProjectDetails extends ProjectSummary {
  sourceOpportunityId?: number | null
  description?: string | null
  plannedStartDate?: string | null
  actualStartDate?: string | null
  actualEndDate?: string | null
  archivedAtUtc?: string | null
  createdAtUtc: string
  members: ProjectMemberDetails[]
  milestones: ProjectMilestoneDetails[]
}

export interface ProjectInput {
  customerId: number
  name: string
  contractAmount: number
  progressPercent: number
  description?: string | null
  plannedStartDate?: string | null
  plannedEndDate?: string | null
}

export interface UpdateProjectInput extends ProjectInput { version: number }
export interface ProjectMemberInput { userId: number; roleName: string; responsibility?: string | null }
export interface UpdateProjectMemberInput extends Omit<ProjectMemberInput, 'userId'> { version: number }
export interface ProjectMilestoneInput { name: string; dueDate?: string | null; isCompleted: boolean; notes?: string | null; sortOrder: number }
export interface UpdateProjectMilestoneInput extends ProjectMilestoneInput { version: number }

export interface ProjectListParameters {
  page: number
  pageSize: number
  search?: string
  archive?: 'active' | 'archived' | 'all'
  status?: ProjectStatus
  customerId?: number
  sortBy?: 'code' | 'name' | 'status' | 'contractAmount' | 'progress' | 'plannedEndDate' | 'updatedAt'
  sortDescending?: boolean
}

export type ProjectPage = PagedResult<ProjectSummary>
