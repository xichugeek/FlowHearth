import { describe, expect, it } from 'vitest'
import { isTerminalProjectStatus, projectStatusLabel, projectStatusTargets } from './project-format'

describe('project lifecycle formatting', () => {
  it('exposes allowed user targets', () => {
    expect(projectStatusLabel('OnHold')).toBe('已暂停')
    expect(projectStatusTargets('Planning')).toEqual(['Active', 'Cancelled'])
    expect(isTerminalProjectStatus('Completed')).toBe(true)
  })
})
