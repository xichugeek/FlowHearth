import type { OpportunityStage } from '../types/opportunities'

export const opportunityStages: OpportunityStage[] = [
  'Lead',
  'Qualified',
  'Proposal',
  'Negotiation',
  'Won',
  'Lost',
]

export const activeOpportunityStages: OpportunityStage[] = [
  'Lead',
  'Qualified',
  'Proposal',
  'Negotiation',
]

const stageLabels: Record<OpportunityStage, string> = {
  Lead: '线索',
  Qualified: '已确认',
  Proposal: '方案/报价',
  Negotiation: '商务谈判',
  Won: '赢单',
  Lost: '丢单',
}

export function opportunityStageLabel(stage: OpportunityStage) {
  return stageLabels[stage]
}

export function opportunityStageTagType(stage: OpportunityStage) {
  if (stage === 'Won') return 'success'
  if (stage === 'Lost') return 'danger'
  if (stage === 'Negotiation') return 'warning'
  return 'primary'
}

export function isTerminalOpportunityStage(stage: OpportunityStage) {
  return stage === 'Won' || stage === 'Lost'
}

export function formatCurrency(value: number) {
  return new Intl.NumberFormat('zh-CN', {
    style: 'currency',
    currency: 'CNY',
    minimumFractionDigits: 2,
  }).format(value)
}

export function expectedWeightedValue(amount: number, probability: number) {
  return amount * (probability / 100)
}
