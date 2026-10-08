import { describe, expect, it } from 'vitest'

import { ApiError, toApiError } from './problem-details'

describe('toApiError', () => {
  it('maps RFC ProblemDetails fields from an Axios response', () => {
    const result = toApiError({
      isAxiosError: true,
      code: 'ERR_BAD_REQUEST',
      response: {
        status: 409,
        data: {
          status: 409,
          title: 'Concurrency conflict',
          detail: 'The record was changed by another user.',
          correlationId: 'test-correlation-id',
        },
      },
    })

    expect(result).toBeInstanceOf(ApiError)
    expect(result.status).toBe(409)
    expect(result.message).toBe('The record was changed by another user.')
    expect(result.correlationId).toBe('test-correlation-id')
  })

  it('returns a stable message for a network failure', () => {
    const result = toApiError({
      isAxiosError: true,
      code: 'ERR_NETWORK',
      message: 'Network Error',
    })

    expect(result.message).toBe('无法连接到服务器。')
    expect(result.code).toBe('ERR_NETWORK')
  })
})
