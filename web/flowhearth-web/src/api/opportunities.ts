import type {
  OpportunityDetails,
  OpportunityInput,
  OpportunityListParameters,
  OpportunityPage,
  OpportunityStage,
  ProjectReference,
  UpdateOpportunityInput,
} from '../types/opportunities'
import { apiClient } from './client'
import { getCsrfHeaders } from './csrf'

export async function listOpportunities(parameters: OpportunityListParameters) {
  const response = await apiClient.get<OpportunityPage>('/opportunities', {
    params: parameters,
  })
  return response.data
}

export async function getOpportunity(opportunityId: number) {
  const response = await apiClient.get<OpportunityDetails>(
    `/opportunities/${opportunityId}`,
  )
  return response.data
}

export async function createOpportunity(input: OpportunityInput) {
  const response = await apiClient.post<OpportunityDetails>(
    '/opportunities',
    input,
    { headers: await getCsrfHeaders() },
  )
  return response.data
}

export async function updateOpportunity(
  opportunityId: number,
  input: UpdateOpportunityInput,
) {
  const response = await apiClient.put<OpportunityDetails>(
    `/opportunities/${opportunityId}`,
    input,
    { headers: await getCsrfHeaders() },
  )
  return response.data
}

export async function transitionOpportunity(
  opportunityId: number,
  stage: OpportunityStage,
  lostReason: string | null,
  version: number,
) {
  const response = await apiClient.post<OpportunityDetails>(
    `/opportunities/${opportunityId}/transition`,
    { stage, lostReason, version },
    { headers: await getCsrfHeaders() },
  )
  return response.data
}

export async function setOpportunityArchived(
  opportunityId: number,
  archived: boolean,
  version: number,
) {
  const action = archived ? 'archive' : 'restore'
  const response = await apiClient.post<OpportunityDetails>(
    `/opportunities/${opportunityId}/${action}`,
    { version },
    { headers: await getCsrfHeaders() },
  )
  return response.data
}

export async function convertOpportunityToProject(
  opportunityId: number,
  version: number,
) {
  const response = await apiClient.post<ProjectReference>(
    `/opportunities/${opportunityId}/convert-to-project`,
    { version },
    { headers: await getCsrfHeaders() },
  )
  return response.data
}
