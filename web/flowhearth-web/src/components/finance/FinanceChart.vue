<script setup lang="ts">
import { BarChart, LineChart } from 'echarts/charts'
import { AriaComponent, GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { init, use } from 'echarts/core'
import type { ECharts, EChartsCoreOption as EChartsOption } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

const props = withDefaults(defineProps<{
  option: EChartsOption
  description: string
  height?: string
}>(), {
  height: '300px',
})

use([
  BarChart,
  LineChart,
  GridComponent,
  LegendComponent,
  TooltipComponent,
  AriaComponent,
  CanvasRenderer,
])

const chartRoot = ref<HTMLDivElement>()
let chart: ECharts | undefined
let resizeObserver: ResizeObserver | undefined

async function render() {
  await nextTick()
  if (!chartRoot.value) return
  chart ??= init(chartRoot.value, undefined, { renderer: 'canvas' })
  chart.setOption(props.option, { notMerge: true })
}

watch(() => props.option, render, { deep: true })

onMounted(() => {
  void render()
  resizeObserver = new ResizeObserver(() => chart?.resize())
  if (chartRoot.value) resizeObserver.observe(chartRoot.value)
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  chart?.dispose()
  chart = undefined
})
</script>

<template>
  <div
    ref="chartRoot"
    class="finance-chart"
    role="img"
    :aria-label="description"
    :style="{ height }"
  />
</template>

<style scoped>
.finance-chart {
  width: 100%;
  min-width: 0;
}
</style>
