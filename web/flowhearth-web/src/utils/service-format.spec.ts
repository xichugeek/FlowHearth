import { describe, expect, it } from 'vitest'
import { formatDowntime, serviceStatusTargets } from './service-format'

describe('service formatting', () => {
  it('keeps closed tickets terminal and resolved tickets reopenable', () => { expect(serviceStatusTargets('Closed')).toEqual([]); expect(serviceStatusTargets('Resolved')).toContain('InProgress') })
  it('formats downtime without losing minutes', () => { expect(formatDowntime(90)).toBe('1 小时 30 分钟') })
})
