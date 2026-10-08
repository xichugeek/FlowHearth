import { describe, expect, it } from 'vitest'

import { hasAnyPermission } from './permissions'

describe('hasAnyPermission', () => {
  it('allows a route when one required permission is granted', () => {
    expect(
      hasAnyPermission(
        ['dashboard.view', 'security.users.view'],
        ['security.roles.view', 'security.users.view'],
      ),
    ).toBe(true)
  })

  it('denies a route when no required permission is granted', () => {
    expect(
      hasAnyPermission(['dashboard.view'], ['security.users.view']),
    ).toBe(false)
  })

  it('allows routes without a permission requirement', () => {
    expect(hasAnyPermission([], undefined)).toBe(true)
  })
})
