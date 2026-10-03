type ProblemDetails = {
  status?: number
  title?: string
  detail?: string
  code?: string
  traceId?: string
  instance?: string
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly title: string,
    detail: string,
    readonly traceId?: string,
    readonly instance?: string,
  ) {
    super(detail)
    this.name = 'ApiError'
  }
}

export async function readApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails | undefined
  try {
    const value: unknown = await response.json()
    if (value && typeof value === 'object') problem = value as ProblemDetails
  } catch {
    // A non-JSON failure still needs a controlled error in the UI.
  }

  return new ApiError(
    response.status,
    typeof problem?.code === 'string' ? problem.code : 'request_failed',
    typeof problem?.title === 'string' ? problem.title : 'İstek başarısız',
    typeof problem?.detail === 'string' ? problem.detail : 'İstek tamamlanamadı.',
    typeof problem?.traceId === 'string' ? problem.traceId : undefined,
    typeof problem?.instance === 'string' ? problem.instance : undefined,
  )
}
