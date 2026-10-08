<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { getEquipmentShipment } from '../../api/finance'
import type { EquipmentShipmentLookup } from '../../types/finance'
import { shipmentStatusLabels, shipmentStatusType } from '../../utils/shipment'

const props = defineProps<{ equipmentId: number }>()
const router = useRouter()
const loading = ref(false)
const lookup = ref<EquipmentShipmentLookup | null>(null)

async function load() {
  loading.value = true
  try {
    lookup.value = await getEquipmentShipment(props.equipmentId)
  } catch {
    ElMessage.error('设备交付信息加载失败。')
  } finally {
    loading.value = false
  }
}

function openShipment() {
  if (lookup.value?.shipment) void router.push({ path: '/finance/shipments', query: { entityId: String(lookup.value.shipment.id) } })
}

onMounted(load)
watch(() => props.equipmentId, load)
</script>

<template>
  <div
    v-loading="loading"
    class="equipment-shipment-panel"
  >
    <template v-if="lookup?.shipment">
      <el-descriptions
        :column="2"
        border
      >
        <el-descriptions-item label="发货单">
          <el-button
            link
            type="primary"
            @click="openShipment"
          >
            {{ lookup.shipment.code }}
          </el-button>
        </el-descriptions-item>
        <el-descriptions-item label="状态">
          <el-tag :type="shipmentStatusType(lookup.shipment.status)">
            {{ shipmentStatusLabels[lookup.shipment.status] }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="出货日期">
          {{ lookup.shipment.shipmentDate }}
        </el-descriptions-item>
        <el-descriptions-item label="签收时间">
          {{ lookup.shipment.signedAtUtc || '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="物流公司">
          {{ lookup.shipment.logisticsCompany || '—' }}
        </el-descriptions-item>
        <el-descriptions-item label="物流单号">
          {{ lookup.shipment.trackingNumber || '—' }}
        </el-descriptions-item>
      </el-descriptions>
      <el-alert
        class="delivery-note"
        title="设备交付事实来自正式发货单；草稿或已取消发货单不计入已发货。"
        type="info"
        :closable="false"
      />
    </template>
    <el-empty
      v-else-if="!loading"
      description="该设备尚无正式发货记录"
    />
  </div>
</template>

<style scoped>
.delivery-note { margin-top: 16px; }
</style>
