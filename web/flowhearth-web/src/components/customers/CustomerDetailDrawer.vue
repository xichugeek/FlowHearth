<script setup lang="ts">
import { Edit, Plus } from '@element-plus/icons-vue'
import { computed, ref, watch } from 'vue'

import EntityAttachmentsPanel from '../attachments/EntityAttachmentsPanel.vue'
import CustomerFinancePanel from '../finance/CustomerFinancePanel.vue'
import CustomerShipmentPanel from '../shipments/CustomerShipmentPanel.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import type { ContactDetails, CustomerDetails } from '../../types/customers'
import { customerRegionLabel } from '../../utils/china-regions'
import {
  customerLevelLabel,
  customerStatusLabel,
  customerStatusTagType,
  followUpMethodLabel,
  formatChinaDateTime,
} from '../../utils/customer-format'

const props = defineProps<{
  modelValue: boolean
  customer?: CustomerDetails | null
  canManage: boolean
  initialTab?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  editCustomer: []
  addContact: []
  editContact: [contact: ContactDetails]
  deleteContact: [contact: ContactDetails]
  addFollowUp: []
}>()
const auth = useAuthStore()
const canViewFinance = computed(() => auth.canAny([permissions.receivablesView]))
const canViewShipments = computed(() => auth.canAny([permissions.shipmentsView]))
const activeTab = ref('overview')
watch(
  () => [props.modelValue, props.initialTab] as const,
  ([visible, tab]) => {
    if (visible) activeTab.value = tab === 'finance' && canViewFinance.value ? 'finance' : 'overview'
  },
  { immediate: true },
)
</script>

<template>
  <el-drawer
    :model-value="modelValue"
    size="min(860px, 96vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <template #header>
      <div
        v-if="customer"
        class="customer-drawer-title"
      >
        <div>
          <span class="customer-code">{{ customer.code }}</span>
          <h2>{{ customer.name }}</h2>
        </div>
        <el-tag
          :type="customer.isArchived ? 'info' : customerStatusTagType(customer.status)"
        >
          {{ customer.isArchived ? '已归档' : customerStatusLabel(customer.status) }}
        </el-tag>
      </div>
    </template>

    <template v-if="customer">
      <div class="customer-detail-actions">
        <el-button
          v-if="canManage && !customer.isArchived"
          :icon="Edit"
          @click="emit('editCustomer')"
        >
          编辑客户
        </el-button>
        <el-button
          v-if="canManage && !customer.isArchived"
          type="primary"
          :icon="Plus"
          @click="emit('addFollowUp')"
        >
          记录跟进
        </el-button>
      </div>

      <el-tabs
        v-model="activeTab"
        class="customer-detail-tabs"
      >
        <el-tab-pane
          label="基本信息"
          name="overview"
        >
          <el-descriptions
            :column="2"
            border
          >
            <el-descriptions-item label="客户简称">
              {{ customer.shortName || '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="所属行业">
              {{ customer.industry || '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="客户状态">
              <el-tag :type="customerStatusTagType(customer.status)">
                {{ customerStatusLabel(customer.status) }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item label="客户等级">
              {{ customerLevelLabel(customer.level) }}
            </el-descriptions-item>
            <el-descriptions-item label="联系电话">
              {{ customer.phone || '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="邮箱">
              {{ customer.email || '—' }}
            </el-descriptions-item>
            <el-descriptions-item
              label="网站"
              :span="2"
            >
              <a
                v-if="customer.website"
                :href="customer.website"
                target="_blank"
                rel="noopener noreferrer"
              >
                {{ customer.website }}
              </a>
              <span v-else>—</span>
            </el-descriptions-item>
            <el-descriptions-item
              label="公司所在地区"
              :span="2"
            >
              {{ customerRegionLabel(customer) }}
            </el-descriptions-item>
            <el-descriptions-item
              label="地址"
              :span="2"
            >
              {{ customer.address || '—' }}
            </el-descriptions-item>
            <el-descriptions-item
              label="备注"
              :span="2"
            >
              <span class="pre-wrap">{{ customer.notes || '—' }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="创建时间">
              {{ formatChinaDateTime(customer.createdAtUtc) }}
            </el-descriptions-item>
            <el-descriptions-item label="更新时间">
              {{ formatChinaDateTime(customer.updatedAtUtc) }}
            </el-descriptions-item>
          </el-descriptions>
        </el-tab-pane>

        <el-tab-pane
          :label="`联系人 (${customer.contacts.length})`"
          name="contacts"
        >
          <div class="detail-tab-toolbar">
            <el-button
              v-if="canManage && !customer.isArchived"
              type="primary"
              plain
              :icon="Plus"
              @click="emit('addContact')"
            >
              添加联系人
            </el-button>
          </div>
          <el-empty
            v-if="customer.contacts.length === 0"
            description="暂无联系人"
          />
          <div
            v-else
            class="contact-card-grid"
          >
            <article
              v-for="contact in customer.contacts"
              :key="contact.id"
              class="contact-card"
            >
              <header>
                <div>
                  <strong>{{ contact.name }}</strong>
                  <el-tag
                    v-if="contact.isPrimary"
                    size="small"
                  >
                    主要联系人
                  </el-tag>
                </div>
                <div v-if="canManage && !customer.isArchived">
                  <el-button
                    link
                    type="primary"
                    @click="emit('editContact', contact)"
                  >
                    编辑
                  </el-button>
                  <el-button
                    link
                    type="danger"
                    @click="emit('deleteContact', contact)"
                  >
                    删除
                  </el-button>
                </div>
              </header>
              <p>{{ [contact.department, contact.title].filter(Boolean).join(' · ') || '职务未填写' }}</p>
              <dl>
                <dt>手机</dt><dd>{{ contact.mobile || '—' }}</dd>
                <dt>电话</dt><dd>{{ contact.phone || '—' }}</dd>
                <dt>邮箱</dt><dd>{{ contact.email || '—' }}</dd>
                <dt>微信</dt><dd>{{ contact.weChat || '—' }}</dd>
              </dl>
            </article>
          </div>
        </el-tab-pane>

        <el-tab-pane
          :label="`跟进记录 (${customer.followUps.length})`"
          name="follow-ups"
        >
          <div class="detail-tab-toolbar">
            <el-button
              v-if="canManage && !customer.isArchived"
              type="primary"
              plain
              :icon="Plus"
              @click="emit('addFollowUp')"
            >
              记录跟进
            </el-button>
          </div>
          <el-empty
            v-if="customer.followUps.length === 0"
            description="暂无跟进记录"
          />
          <el-timeline v-else>
            <el-timeline-item
              v-for="followUp in customer.followUps"
              :key="followUp.id"
              :timestamp="formatChinaDateTime(followUp.occurredAtUtc)"
              placement="top"
            >
              <article class="followup-card">
                <header>
                  <strong>{{ followUp.summary }}</strong>
                  <el-tag
                    size="small"
                    type="success"
                  >
                    {{ followUpMethodLabel(followUp.method) }}
                  </el-tag>
                </header>
                <p
                  v-if="followUp.details"
                  class="pre-wrap"
                >
                  {{ followUp.details }}
                </p>
                <footer>
                  <span v-if="followUp.contactName">联系人：{{ followUp.contactName }}</span>
                  <span>记录人：{{ followUp.createdByDisplayName || '未知用户' }}</span>
                  <span v-if="followUp.nextFollowUpAtUtc">
                    下次跟进：{{ formatChinaDateTime(followUp.nextFollowUpAtUtc) }}
                  </span>
                </footer>
              </article>
            </el-timeline-item>
          </el-timeline>
        </el-tab-pane>
        <el-tab-pane
          label="附件"
          name="attachments"
        >
          <EntityAttachmentsPanel
            entity-type="Customer"
            :entity-id="customer.id"
          />
        </el-tab-pane>
        <el-tab-pane
          v-if="canViewShipments"
          label="出货记录"
          name="shipments"
        >
          <CustomerShipmentPanel :customer-id="customer.id" />
        </el-tab-pane>
        <el-tab-pane
          v-if="canViewFinance"
          label="财务"
          name="finance"
        >
          <CustomerFinancePanel :customer-id="customer.id" />
        </el-tab-pane>
      </el-tabs>
    </template>
  </el-drawer>
</template>
