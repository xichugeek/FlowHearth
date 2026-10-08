export function formatFinanceAmount(value: number | null | undefined) {
  if (value == null) return '—'
  return new Intl.NumberFormat('zh-CN', {
    style: 'currency',
    currency: 'CNY',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)
}

export function formatFinancePercentage(value: number | null | undefined) {
  if (value == null || !Number.isFinite(value)) return '—'
  return `${value.toFixed(2)}%`
}

export function formatFinanceDate(value: string | null | undefined) {
  return value?.slice(0, 10) || '—'
}

export function calculatePercentagePlan(contractAmount: number, percentages: number[]) {
  if (contractAmount <= 0 || percentages.length === 0 || percentages.some(value => value <= 0)) {
    throw new RangeError('Invalid plan values.')
  }

  const totalPercent = percentages.reduce((sum, value) => sum + value, 0)
  if (Math.abs(totalPercent - 100) > 0.000001) {
    throw new RangeError('Percentages must total 100.')
  }

  const cents = Math.round(contractAmount * 100)
  let assigned = 0
  return percentages.map((percentage, index) => {
    const itemCents = index === percentages.length - 1
      ? cents - assigned
      : Math.round(cents * percentage / 100)
    assigned += itemCents
    return itemCents / 100
  })
}

export function allocationDraftTotals(availableAmount: number, amounts: number[]) {
  const allocationAmount = amounts.reduce((sum, value) => sum + Math.max(0, value || 0), 0)
  return {
    allocationAmount: Math.round(allocationAmount * 100) / 100,
    remainingAmount: Math.round((availableAmount - allocationAmount) * 100) / 100,
    valid: allocationAmount > 0 && allocationAmount <= availableAmount,
  }
}

export interface ReceivablePlanDraft {
  receivableType: string
  title: string
  amount: number
  dueDate: string
}

export function getReceivablePlanValidationError(
  availableContractAmount: number,
  rows: ReceivablePlanDraft[],
) {
  if (rows.length === 0 || rows.some(row => (
    !row.receivableType
    || !row.title.trim()
    || !row.dueDate
    || !Number.isFinite(row.amount)
    || row.amount <= 0
  ))) {
    return '请完整填写每个阶段的类型、标题、金额和到期日。'
  }

  const planCents = rows.reduce((sum, row) => sum + Math.round(row.amount * 100), 0)
  if (planCents > Math.round(availableContractAmount * 100)) {
    return '应收计划总额不能超过合同金额。'
  }

  return null
}
