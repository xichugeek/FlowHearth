import type {
  ProjectDetails,
  ProjectInput,
  ProjectListParameters,
  ProjectMemberCandidate,
  ProjectMemberDetails,
  ProjectMemberInput,
  ProjectMilestoneDetails,
  ProjectMilestoneInput,
  ProjectPage,
  ProjectStatus,
  UpdateProjectInput,
  UpdateProjectMemberInput,
  UpdateProjectMilestoneInput,
} from '../types/projects'
import { apiClient } from './client'
import { getCsrfHeaders } from './csrf'

export async function listProjects(parameters: ProjectListParameters) {
  return (await apiClient.get<ProjectPage>('/projects', { params: parameters })).data
}
export async function getProject(id: number) {
  return (await apiClient.get<ProjectDetails>(`/projects/${id}`)).data
}
export async function listMemberCandidates() {
  return (await apiClient.get<ProjectMemberCandidate[]>('/projects/member-candidates')).data
}
export async function createProject(input: ProjectInput) {
  return (await apiClient.post<ProjectDetails>('/projects', input, { headers: await getCsrfHeaders() })).data
}
export async function updateProject(id: number, input: UpdateProjectInput) {
  return (await apiClient.put<ProjectDetails>(`/projects/${id}`, input, { headers: await getCsrfHeaders() })).data
}
export async function transitionProject(id: number, status: ProjectStatus, version: number) {
  return (await apiClient.post<ProjectDetails>(`/projects/${id}/transition`, { status, version }, { headers: await getCsrfHeaders() })).data
}
export async function setProjectArchived(id: number, archived: boolean, version: number) {
  const action = archived ? 'archive' : 'restore'
  return (await apiClient.post<ProjectDetails>(`/projects/${id}/${action}`, { version }, { headers: await getCsrfHeaders() })).data
}
export async function createMember(projectId: number, input: ProjectMemberInput) {
  return (await apiClient.post<ProjectMemberDetails>(`/projects/${projectId}/members`, input, { headers: await getCsrfHeaders() })).data
}
export async function updateMember(projectId: number, memberId: number, input: UpdateProjectMemberInput) {
  return (await apiClient.put<ProjectMemberDetails>(`/projects/${projectId}/members/${memberId}`, input, { headers: await getCsrfHeaders() })).data
}
export async function deleteMember(projectId: number, memberId: number, version: number) {
  await apiClient.delete(`/projects/${projectId}/members/${memberId}`, { params: { version }, headers: await getCsrfHeaders() })
}
export async function createMilestone(projectId: number, input: ProjectMilestoneInput) {
  return (await apiClient.post<ProjectMilestoneDetails>(`/projects/${projectId}/milestones`, input, { headers: await getCsrfHeaders() })).data
}
export async function updateMilestone(projectId: number, milestoneId: number, input: UpdateProjectMilestoneInput) {
  return (await apiClient.put<ProjectMilestoneDetails>(`/projects/${projectId}/milestones/${milestoneId}`, input, { headers: await getCsrfHeaders() })).data
}
export async function deleteMilestone(projectId: number, milestoneId: number, version: number) {
  await apiClient.delete(`/projects/${projectId}/milestones/${milestoneId}`, { params: { version }, headers: await getCsrfHeaders() })
}
