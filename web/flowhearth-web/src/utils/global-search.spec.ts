import { describe, expect, it } from 'vitest'

import type { GlobalSearchResult } from '../types/search'
import {
  globalSearchKindLabel,
  routeForSearchResult,
} from './global-search'

describe('global search navigation', () => {
  it.each([
    ['Customer', 'Customer', '/customers'],
    ['Contact', 'Customer', '/customers'],
    ['Project', 'Project', '/projects'],
    ['Equipment', 'Equipment', '/equipment'],
    ['ServiceTicket', 'ServiceTicket', '/service'],
    ['PurchaseOrder', 'PurchaseOrder', '/finance/purchases'],
    ['Payable', 'Payable', '/finance/payables'],
    ['Payment', 'Payment', '/finance/payments'],
  ] as const)('routes %s results to the owning entity', (kind, targetType, path) => {
    const result: GlobalSearchResult = {
      kind,
      targetType,
      targetId: 42,
      code: 'CODE-42',
      title: '测试对象',
      isArchived: false,
    }

    expect(routeForSearchResult(result)).toEqual({
      path,
      query: { entityId: '42' },
    })
  })

  it('uses a distinct label for contact matches', () => {
    expect(globalSearchKindLabel('Contact')).toBe('联系人')
  })
})
