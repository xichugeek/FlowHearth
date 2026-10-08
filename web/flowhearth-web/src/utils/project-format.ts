import type { ProjectStatus } from '../types/projects'

export const projectStatuses: ProjectStatus[] = ['Planning', 'Active', 'OnHold', 'Completed', 'Cancelled']
const labels: Record<ProjectStatus, string> = { Planning: '规划中', Active: '执行中', OnHold: '已暂停', Completed: '已结项', Cancelled: '已取消' }
export function projectStatusLabel(status: ProjectStatus) { return labels[status] }
export function projectStatusTagType(status: ProjectStatus) {
  if (status === 'Completed') return 'success'
  if (status === 'Cancelled') return 'danger'
  if (status === 'OnHold') return 'warning'
  return 'primary'
}
export function isTerminalProjectStatus(status: ProjectStatus) { return status === 'Completed' || status === 'Cancelled' }
export function projectStatusTargets(status: ProjectStatus): ProjectStatus[] {
  if (status === 'Planning') return ['Active', 'Cancelled']
  if (status === 'Active') return ['OnHold', 'Completed', 'Cancelled']
  if (status === 'OnHold') return ['Active', 'Cancelled']
  return []
}
