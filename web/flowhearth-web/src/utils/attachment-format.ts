export function formatFileSize(sizeBytes: number) {
  if (!Number.isFinite(sizeBytes) || sizeBytes < 0) return '—'
  if (sizeBytes < 1024) return `${sizeBytes} B`
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KiB`
  return `${(sizeBytes / 1024 / 1024).toFixed(1)} MiB`
}

export function formatAuditJson(value?: string | null) {
  if (!value) return '无'
  try {
    return JSON.stringify(JSON.parse(value), null, 2)
  } catch {
    return value
  }
}
