<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getProjectShipmentMetrics, listShipments } from '../../api/finance'
import type { ProjectShipmentMetrics, ShipmentStatus, ShipmentSummary } from '../../types/finance'
import { shipmentStatusLabels, shipmentStatusType } from '../../utils/shipment'

const props = defineProps<{ projectId: number; canManage: boolean }>()
const router = useRouter()
const loading = ref(false)
const metrics = ref<ProjectShipmentMetrics | null>(null)
const shipments = ref<ShipmentSummary[]>([])
const receivedRate = computed(() => {
  if (!metrics.value?.shipmentCount) return 0
  return Math.round((metrics.value.receivedShipmentCount / metrics.value.shipmentCount) * 100)
})

async function load() {
  loading.value = true
  try {
    const [summary, page] = await Promise.all([
      getProjectShipmentMetrics(props.projectId),
      listShipments({ projectId: props.projectId, page: 1, pageSize: 10, sortBy: 'shipmentDate', sortDescending: true }),
    ])
    metrics.value = summary
    shipments.value = page.items
  } catch {
    ElMessage.error('项目交付信息加载失败。')
  } finally {
    loading.value = false
  }
}

function openShipment(id?: number) {
  void router.push({ path: '/finance/shipments', query: id ? { entityId: String(id) } : { projectId: String(props.projectId) } })
}

function createShipment() {
  void router.push({ path: '/finance/shipments', query: { create: '1', projectId: String(props.projectId) } })
}

onMounted(load)
watch(() => props.projectId, load)
</script>

<template>
  <div
    v-loading="loading"
    class="project-shipment-panel"
  >
    <div class="detail-tab-toolbar">
      <el-button
        v-if="canManage"
        type="primary"
        plain
        @click="createShipment"
      >
        新建发货单
      </el-button>
      <el-button @click="openShipment()">
        查看全部发货单
      </el-button>
    </div>
    <div
      v-if="metrics"
      class="shipment-metrics"
    >
      <div><span>正式发货</span><strong>{{ metrics.shipmentCount }}</strong></div>
      <div><span>已签收</span><strong>{{ metrics.receivedShipmentCount }}</strong></div>
      <div><span>设备交付</span><strong>{{ metrics.equipmentDeliveryCount }} / {{ metrics.totalEquipmentCount }}</strong></div>
      <div><span>最近发货</span><strong>{{ metrics.lastShipmentDate || '—' }}</strong></div>
    </div>
    <el-alert
      v-if="metrics?.totalEquipmentCount"
      :title="metrics.allEquipmentDelivered ? '项目有效设备已全部进入正式交付记录。' : '项目仍有设备尚未进入正式交付记录。'"
      :type="metrics.allEquipmentDelivered ? 'success' : 'warning'"
      :closable="false"
      class="delivery-summary"
    />
    <el-progress
      v-if="metrics?.shipmentCount"
      :percentage="receivedRate"
      status="success"
    />
    <div class="shipment-table-shell">
      <el-table
        :data="shipments"
        size="small"
      >
        <el-table-column
          label="发货单"
          min-width="145"
        >
          <template #default="scope">
            <el-button
              link
              type="primary"
              @click="openShipment(scope.row.id)"
            >
              {{ scope.row.code }}
            </el-button>
          </template>
        </el-table-column>
        <el-table-column
          prop="shipmentDate"
          label="出货日期"
          min-width="110"
        />
        <el-table-column
          label="状态"
          min-width="100"
        >
          <template #default="scope">
            <el-tag :type="shipmentStatusType(scope.row.status)">
              {{ shipmentStatusLabels[scope.row.status as ShipmentStatus] }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column
          label="明细 / 设备"
          min-width="110"
        >
          <template #default="scope">
            {{ scope.row.itemCount }} / {{ scope.row.equipmentCount }}
          </template>
        </el-table-column>
        <el-table-column
          prop="trackingNumber"
          label="物流单号"
          min-width="150"
          show-overflow-tooltip
        />
      </el-table>
    </div>
    <el-empty
      v-if="!loading && shipments.length === 0"
      description="暂无发货单"
    />
  </div>
</template>

<style scoped>
.shipment-metrics { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 16px; }
.shipment-metrics div { padding: 14px; border: 1px solid var(--el-border-color-lighter); border-radius: 10px; background: var(--el-fill-color-extra-light); }
.shipment-metrics span, .shipment-metrics strong { display: block; }
.shipment-metrics span { color: var(--el-text-color-secondary); font-size: 12px; margin-bottom: 6px; }
.shipment-metrics strong { font-size: 18px; }
.shipment-table-shell { width: 100%; overflow-x: auto; margin-top: 16px; }
.delivery-summary { margin-top: 16px; }
.shipment-table-shell :deep(.el-table) { min-width: 700px; }
@media (max-width: 640px) { .shipment-metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
