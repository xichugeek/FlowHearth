import { describe, expect, it } from 'vitest'

import {
  expectedWeightedValue,
  formatCurrency,
  isTerminalOpportunityStage,
  opportunityStageLabel,
} from './opportunity-format'

describe('opportunity formatting', () => {
  it('labels stages and identifies terminal states', () => {
    expect(opportunityStageLabel('Negotiation')).toBe('商务谈判')
    expect(isTerminalOpportunityStage('Won')).toBe(true)
    expect(isTerminalOpportunityStage('Lead')).toBe(false)
  })

  it('calculates weighted values', () => {
    expect(expectedWeightedValue(250000, 40)).toBe(100000)
    expect(formatCurrency(100000)).toContain('100,000.00')
  })
})
