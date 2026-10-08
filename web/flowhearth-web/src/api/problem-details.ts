import axios from 'axios'

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  correlationId?: string
  errors?: Record<string, string[]>
}

interface ApiErrorOptions {
  status?: number
  code?: string
  correlationId?: string
  problem?: ProblemDetails
}

export class ApiError extends Error {
  readonly status?: number
  readonly code?: string
  readonly correlationId?: string
  readonly problem?: ProblemDetails

  constructor(message: string, options: ApiErrorOptions = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = options.status
    this.code = options.code
    this.correlationId = options.correlationId
    this.problem = options.problem
  }
}

export function toApiError(error: unknown): ApiError {
  if (axios.isAxiosError<ProblemDetails>(error)) {
    const problem = error.response?.data
    const message =
      problem?.detail ??
      problem?.title ??
      (error.response ? '请求未能完成。' : '无法连接到服务器。')

    return new ApiError(message, {
      status: problem?.status ?? error.response?.status,
      code: error.code,
      correlationId: problem?.correlationId,
      problem,
    })
  }

  if (error instanceof Error) {
    return new ApiError(error.message)
  }

  return new ApiError('发生未知错误。')
}
