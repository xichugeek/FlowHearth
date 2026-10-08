import { describe, expect, it } from 'vitest'

import { formatAuditJson, formatFileSize } from './attachment-format'

describe('attachment formatting', () => {
  it('formats byte sizes', () => {
    expect(formatFileSize(17)).toBe('17 B')
    expect(formatFileSize(1536)).toBe('1.5 KiB')
    expect(formatFileSize(2 * 1024 * 1024)).toBe('2.0 MiB')
  })

  it('pretty prints valid audit JSON and preserves legacy text', () => {
    expect(formatAuditJson('{"name":"测试"}')).toContain('\n')
    expect(formatAuditJson('legacy')).toBe('legacy')
    expect(formatAuditJson(null)).toBe('无')
  })
})
