<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { listShipments } from '../../api/finance'
import type { ShipmentStatus, ShipmentSummary } from '../../types/finance'
import { shipmentStatusLabels, shipmentStatusType } from '../../utils/shipment'

const props = defineProps<{ customerId: number }>()
const router = useRouter()
const loading = ref(false)
const shipments = ref<ShipmentSummary[]>([])

async function load() {
  loading.value = true
  try {
    shipments.value = (await listShipments({
      customerId: props.customerId,
      page: 1,
      pageSize: 10,
      sortBy: 'shipmentDate',
      sortDescending: true,
    })).items
  } catch {
    ElMessage.error('客户出货记录加载失败。')
  } finally {
    loading.value = false
  }
}

function openShipment(id?: number) {
  void router.push({
    path: '/finance/shipments',
    query: id ? { entityId: String(id) } : { customerId: String(props.customerId) },
  })
}

onMounted(load)
watch(() => props.customerId, load)
</script>

<template>
  <div v-loading="loading">
    <div class="detail-tab-toolbar">
      <el-button @click="openShipment()">
        查看全部出货记录
      </el-button>
    </div>
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
          prop="projectName"
          label="项目"
          min-width="180"
          show-overflow-tooltip
        />
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
          prop="trackingNumber"
          label="物流单号"
          min-width="150"
          show-overflow-tooltip
        />
        <el-table-column
          prop="signedAtUtc"
          label="签收时间"
          min-width="170"
        />
      </el-table>
    </div>
    <el-empty
      v-if="!loading && shipments.length === 0"
      description="暂无出货记录"
    />
  </div>
</template>

<style scoped>
.shipment-table-shell { width: 100%; overflow-x: auto; }
.shipment-table-shell :deep(.el-table) { min-width: 860px; }
</style>
