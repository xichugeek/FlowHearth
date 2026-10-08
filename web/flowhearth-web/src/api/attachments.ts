import type { AttachmentEntityType, AttachmentSummary } from '../types/attachments'
import { apiClient } from './client'

export async function listAttachments(entityType: AttachmentEntityType, entityId: number) {
  const response = await apiClient.get<AttachmentSummary[]>('/files', {
    params: { entityType, entityId },
  })
  return response.data
}

export async function uploadAttachment(
  entityType: AttachmentEntityType,
  entityId: number,
  file: File,
  description?: string,
) {
  const body = new FormData()
  body.append('file', file, file.name)
  if (description) body.append('description', description)
  const response = await apiClient.post<AttachmentSummary>('/files', body, {
    params: { entityType, entityId },
    timeout: 60_000,
  })
  return response.data
}

export async function downloadAttachment(attachmentId: number) {
  const response = await apiClient.get<Blob>(`/files/${attachmentId}/download`, {
    responseType: 'blob',
    timeout: 60_000,
  })
  return response.data
}

export async function deleteAttachment(attachmentId: number, version: number) {
  await apiClient.delete(`/files/${attachmentId}`, { params: { version } })
}
