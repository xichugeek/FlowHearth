import {
  codeToText,
  regionData,
  type DataItem,
} from 'element-china-area-data'
import type { CascaderOption } from 'element-plus'

export const chinaRegionOptions: CascaderOption[] = toCascaderOptions(regionData)

export const chinaRegionCascaderProps = {
  checkStrictly: true,
  emitPath: true,
}

function toCascaderOptions(items: DataItem[]): CascaderOption[] {
  return items.map((item) => ({
    value: item.value,
    label: item.label,
    children: item.children ? toCascaderOptions(item.children) : undefined,
  }))
}

export function customerRegionPath(region: {
  provinceCode?: string | null
  cityCode?: string | null
  districtCode?: string | null
}) {
  return [region.provinceCode, region.cityCode, region.districtCode].filter(
    (code): code is string => Boolean(code),
  )
}

export function customerRegionParameters(value: string[]) {
  return {
    provinceCode: value[0] || undefined,
    cityCode: value[1] || undefined,
    districtCode: value[2] || undefined,
  }
}

export function customerRegionLabel(region: {
  provinceCode?: string | null
  cityCode?: string | null
  districtCode?: string | null
}) {
  const labels = customerRegionPath(region)
    .map((code) => codeToText[code])
    .filter((label): label is string => Boolean(label))
  return labels.length > 0 ? labels.join(' / ') : '—'
}
