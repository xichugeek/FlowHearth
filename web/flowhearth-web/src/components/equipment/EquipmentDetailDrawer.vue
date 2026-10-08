<script setup lang="ts">
import { Plus } from '@element-plus/icons-vue'
import EntityAttachmentsPanel from '../attachments/EntityAttachmentsPanel.vue'
import EquipmentShipmentPanel from '../shipments/EquipmentShipmentPanel.vue'
import { computed } from 'vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import type { EquipmentComponentDetails, EquipmentDetails, EquipmentParameterDetails, EquipmentVersionDetails } from '../../types/equipment'
import { formatChinaDateTime } from '../../utils/customer-format'
import { equipmentCategoryLabel, equipmentModelLabel } from '../../utils/equipment-format'

defineProps<{ modelValue: boolean; equipment?: EquipmentDetails | null; canManage?: boolean; busy?: boolean }>()
const emit = defineEmits<{
  'update:modelValue': [boolean]
  edit: []
  archive: []
  'add-component': []
  'edit-component': [EquipmentComponentDetails]
  'delete-component': [EquipmentComponentDetails]
  'add-parameter': []
  'edit-parameter': [EquipmentParameterDetails]
  'delete-parameter': [EquipmentParameterDetails]
  'add-version': []
  'edit-version': [EquipmentVersionDetails]
  'delete-version': [EquipmentVersionDetails]
}>()
const auth = useAuthStore()
const canViewShipments = computed(() => auth.canAny([permissions.shipmentsView]))
</script>

<template>
  <el-drawer
    :model-value="modelValue"
    size="min(880px, 97vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <template #header>
      <div
        v-if="equipment"
        class="equipment-drawer-heading"
      >
        <div>
          <p class="section-kicker">
            {{ equipment.code }}
          </p><h3>{{ equipment.name }}</h3><p>{{ equipment.customerName }} · {{ equipment.projectCode || '未关联项目' }}</p>
        </div>
        <el-tag :type="equipment.isArchived ? 'info' : 'primary'">
          {{ equipmentCategoryLabel(equipment.category) }}{{ equipment.isArchived ? ' · 已归档' : '' }}
        </el-tag>
      </div>
    </template>
    <template v-if="equipment">
      <div
        v-if="canManage"
        class="detail-action-row"
      >
        <el-button
          :disabled="equipment.isArchived"
          @click="emit('edit')"
        >
          编辑设备
        </el-button>
        <el-button
          :type="equipment.isArchived ? 'primary' : 'danger'"
          plain
          :loading="busy"
          @click="emit('archive')"
        >
          {{ equipment.isArchived ? '恢复' : '归档' }}
        </el-button>
      </div>
      <div class="equipment-overview-grid">
        <div><span>品牌 / 型号</span><strong>{{ equipmentModelLabel(equipment.manufacturer, equipment.model) }}</strong></div>
        <div><span>序列号</span><strong>{{ equipment.serialNumber || '—' }}</strong></div>
        <div><span>投产日期</span><strong>{{ equipment.commissionedDate?.slice(0, 10) || '—' }}</strong></div>
        <div><span>安装位置</span><strong>{{ equipment.installLocation || '—' }}</strong></div>
      </div>
      <p class="equipment-notes">
        {{ equipment.notes || '暂无设备说明。' }}
      </p>
      <el-tabs class="equipment-detail-tabs">
        <el-tab-pane :label="`组件 (${equipment.components.length})`">
          <div class="detail-tab-toolbar">
            <el-button
              v-if="canManage && !equipment.isArchived"
              type="primary"
              plain
              :icon="Plus"
              @click="emit('add-component')"
            >
              添加组件
            </el-button>
          </div>
          <el-table
            :data="equipment.components"
            size="small"
          >
            <el-table-column
              label="组件"
              min-width="180"
            >
              <template #default="scope">
                <strong>{{ scope.row.name }}</strong><p class="cell-secondary">
                  {{ equipmentCategoryLabel(scope.row.category) }} · 数量 {{ scope.row.quantity }}
                </p>
              </template>
            </el-table-column>
            <el-table-column
              label="品牌 / 型号"
              min-width="180"
            >
              <template #default="scope">
                {{ equipmentModelLabel(scope.row.manufacturer, scope.row.model) }}
              </template>
            </el-table-column>
            <el-table-column
              prop="firmwareVersion"
              label="固件"
              min-width="120"
            />
            <el-table-column
              prop="installLocation"
              label="位置"
              min-width="130"
            />
            <el-table-column
              v-if="canManage && !equipment.isArchived"
              label="操作"
              width="120"
            >
              <template #default="scope">
                <el-button
                  link
                  @click="emit('edit-component', scope.row as EquipmentComponentDetails)"
                >
                  编辑
                </el-button><el-button
                  link
                  type="danger"
                  @click="emit('delete-component', scope.row as EquipmentComponentDetails)"
                >
                  删除
                </el-button>
              </template>
            </el-table-column>
          </el-table><el-empty
            v-if="equipment.components.length === 0"
            description="暂无设备组件"
          />
        </el-tab-pane>
        <el-tab-pane :label="`参数 (${equipment.parameters.length})`">
          <div class="detail-tab-toolbar">
            <el-button
              v-if="canManage && !equipment.isArchived"
              type="primary"
              plain
              :icon="Plus"
              @click="emit('add-parameter')"
            >
              添加参数
            </el-button>
          </div>
          <div class="equipment-parameter-grid">
            <article
              v-for="item in equipment.parameters"
              :key="item.id"
            >
              <span>{{ item.parameterGroup || '通用' }}</span><strong>{{ item.name }}</strong><p>{{ item.value }}{{ item.unit ? ` ${item.unit}` : '' }}</p><small>{{ item.notes || '暂无说明' }}</small><footer v-if="canManage && !equipment.isArchived">
                <el-button
                  link
                  @click="emit('edit-parameter', item)"
                >
                  编辑
                </el-button><el-button
                  link
                  type="danger"
                  @click="emit('delete-parameter', item)"
                >
                  删除
                </el-button>
              </footer>
            </article>
          </div><el-empty
            v-if="equipment.parameters.length === 0"
            description="暂无设备参数"
          />
        </el-tab-pane>
        <el-tab-pane :label="`版本历史 (${equipment.versions.length})`">
          <div class="detail-tab-toolbar">
            <el-button
              v-if="canManage && !equipment.isArchived"
              type="primary"
              plain
              :icon="Plus"
              @click="emit('add-version')"
            >
              添加版本
            </el-button>
          </div>
          <el-timeline>
            <el-timeline-item
              v-for="item in equipment.versions"
              :key="item.id"
              type="primary"
              :timestamp="item.releasedDate?.slice(0, 10) || formatChinaDateTime(item.createdAtUtc)"
              placement="top"
            >
              <div class="equipment-version-card">
                <div>
                  <strong>{{ item.versionLabel }}</strong><el-tag size="small">
                    {{ item.versionType }}
                  </el-tag>
                </div><code v-if="item.gitCommit">{{ item.gitCommit }}</code><p>{{ item.changelog || '暂无变更记录' }}</p><small>{{ item.notes || '' }}</small><footer v-if="canManage && !equipment.isArchived">
                  <el-button
                    link
                    @click="emit('edit-version', item)"
                  >
                    编辑
                  </el-button><el-button
                    link
                    type="danger"
                    @click="emit('delete-version', item)"
                  >
                    删除
                  </el-button>
                </footer>
              </div>
            </el-timeline-item>
          </el-timeline><el-empty
            v-if="equipment.versions.length === 0"
            description="暂无版本历史"
          />
        </el-tab-pane>
        <el-tab-pane label="附件">
          <EntityAttachmentsPanel
            entity-type="Equipment"
            :entity-id="equipment.id"
          />
        </el-tab-pane>
        <el-tab-pane
          v-if="canViewShipments"
          label="交付记录"
        >
          <EquipmentShipmentPanel :equipment-id="equipment.id" />
        </el-tab-pane>
      </el-tabs>
    </template>
  </el-drawer>
</template>
