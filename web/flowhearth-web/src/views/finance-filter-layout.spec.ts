/// <reference types="node" />

import { readFileSync } from 'node:fs'

import { describe, expect, it } from 'vitest'

import PaymentsViewSource from './finance/PaymentsView.vue?raw'
import PurchasesViewSource from './finance/PurchasesView.vue?raw'
import ShipmentsViewSource from './finance/ShipmentsView.vue?raw'

const globalStyle = readFileSync('src/style.css', 'utf8')

describe('finance filter layout', () => {
  it('keeps finance date ranges at a readable desktop width', () => {
    expect(globalStyle).toMatch(
      /\.finance-filter-grid\s*>\s*\.el-date-editor\s*\{[^}]*min-width:\s*280px;[^}]*flex:\s*0 0 280px;/s,
    )
    expect(PaymentsViewSource).toContain('class="finance-filter-grid"')
    expect(PurchasesViewSource).toContain(
      'class="finance-filter-grid purchase-toolbar"',
    )
  })

  it('wraps finance filters instead of shrinking their contents', () => {
    expect(globalStyle).toMatch(
      /\.finance-filter-grid\s*\{[^}]*display:\s*flex;[^}]*flex-wrap:\s*wrap;/s,
    )
    expect(globalStyle).toMatch(
      /\.finance-filter-grid\s*>\s*\.el-select\s*\{[^}]*min-width:\s*140px;/s,
    )
  })

  it('gives the shipment search enough room for its complete placeholder', () => {
    expect(ShipmentsViewSource).toContain(
      '.search-input { width: min(420px, 100%); min-width: 360px; }',
    )
    expect(ShipmentsViewSource).toContain(
      '.search-input, .equipment-picker { width: 100%; min-width: 0; }',
    )
  })
})
