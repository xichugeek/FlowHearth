import type { EChartsOption } from 'echarts'

import type {
  FinanceAgingOverview,
  FinanceCashFlowPoint,
  FinanceProjectRankingMetric,
  FinanceProjectRankingRow,
} from '../types/finance'
import { formatFinanceAmount, formatFinancePercentage } from './finance-format'

export const agingBucketLabels: Record<string, string> = {
  NotDue: '未到期',
  '1-30': '逾期 1–30 天',
  '31-60': '逾期 31–60 天',
  '61-90': '逾期 61–90 天',
  '90+': '逾期 90 天以上',
}

export const projectRankingMetricLabels: Record<FinanceProjectRankingMetric, string> = {
  contractAmount: '合同金额',
  grossProfit: '预计毛利',
  grossMargin: '预计毛利率',
  outstandingAmount: '未收金额',
  overdueAmount: '逾期应收',
}

function axisAmount(value: number) {
  if (Math.abs(value) >= 10_000) return `${(value / 10_000).toFixed(0)}万`
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 0 }).format(value)
}

export function buildCashFlowOption(points: FinanceCashFlowPoint[]): EChartsOption {
  return {
    animationDuration: 350,
    color: ['#2f6e9d', '#e0a02b', '#173a5e'],
    tooltip: {
      trigger: 'axis',
      valueFormatter: value => formatFinanceAmount(Number(value)),
    },
    legend: { bottom: 0, data: ['收款', '付款', '净现金流'] },
    grid: { top: 24, right: 18, bottom: 56, left: 68 },
    xAxis: {
      type: 'category',
      data: points.map(item => item.month.slice(2).replace('-', '/')),
      axisTick: { alignWithLabel: true },
    },
    yAxis: {
      type: 'value',
      axisLabel: { formatter: axisAmount },
      splitLine: { lineStyle: { color: '#edf1f4' } },
    },
    series: [
      { name: '收款', type: 'bar', barMaxWidth: 24, data: points.map(item => item.receivedAmount) },
      { name: '付款', type: 'bar', barMaxWidth: 24, data: points.map(item => item.paidAmount) },
      { name: '净现金流', type: 'line', smooth: true, symbolSize: 7, data: points.map(item => item.netAmount) },
    ],
  }
}

export function buildAgingOption(aging: FinanceAgingOverview): EChartsOption {
  return {
    animationDuration: 350,
    color: ['#2f6e9d'],
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      valueFormatter: value => formatFinanceAmount(Number(value)),
    },
    grid: { top: 8, right: 22, bottom: 24, left: 112 },
    xAxis: {
      type: 'value',
      axisLabel: { formatter: axisAmount },
      splitLine: { lineStyle: { color: '#edf1f4' } },
    },
    yAxis: {
      type: 'category',
      data: aging.buckets.map(item => agingBucketLabels[item.bucket] ?? item.bucket),
      axisTick: { show: false },
    },
    series: [{
      name: '余额',
      type: 'bar',
      barMaxWidth: 20,
      data: aging.buckets.map((item, index) => ({
        value: item.amount,
        itemStyle: { color: index === 0 ? '#7a9cb6' : index < 3 ? '#e0a02b' : '#c45656' },
      })),
    }],
  }
}

export function projectRankingValue(
  row: FinanceProjectRankingRow,
  metric: FinanceProjectRankingMetric,
) {
  if (metric === 'grossProfit') return formatFinanceAmount(row.grossProfit)
  if (metric === 'grossMargin') return formatFinancePercentage(row.grossMargin)
  if (metric === 'outstandingAmount') return formatFinanceAmount(row.receivableOutstandingAmount)
  if (metric === 'overdueAmount') return formatFinanceAmount(row.receivableOverdueAmount)
  return formatFinanceAmount(row.contractAmount)
}
