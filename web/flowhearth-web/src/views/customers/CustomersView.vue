<script setup lang="ts">
import { Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import dayjs from 'dayjs'
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

import * as customerApi from '../../api/customers'
import { ApiError } from '../../api/problem-details'
import ContactFormDialog from '../../components/customers/ContactFormDialog.vue'
import CustomerDetailDrawer from '../../components/customers/CustomerDetailDrawer.vue'
import CustomerFormDialog from '../../components/customers/CustomerFormDialog.vue'
import FollowUpDialog from '../../components/customers/FollowUpDialog.vue'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type {
  ContactDetails,
  ContactInput,
  CustomerDetails,
  CustomerFollowUpInput,
  CustomerInput,
  CustomerLevel,
  CustomerStatus,
  CustomerSummary,
  UpdateContactInput,
  UpdateCustomerInput,
} from '../../types/customers'
import {
  chinaRegionCascaderProps,
  chinaRegionOptions,
  customerRegionParameters,
} from '../../utils/china-regions'
import {
  customerContactMethod,
  customerLevelLabel,
  customerLevelOptions,
  customerStatusLabel,
  customerStatusOptions,
  customerStatusTagType,
  formatChinaDateTime,
} from '../../utils/customer-format'

const authStore = useAuthStore()
const settingsStore = useSettingsStore()
const route = useRoute()
const canManage = computed(() => authStore.canAny([permissions.customersManage]))

const loading = ref(false)
const saving = ref(false)
const customers = ref<CustomerSummary[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(settingsStore.defaultPageSize)
const search = ref('')
const archive = ref<'active' | 'archived' | 'all'>('active')
const status = ref<CustomerStatus | ''>('')
const level = ref<CustomerLevel | ''>('')
const region = ref<string[]>([])
const dueBefore = ref('')
const sortBy = ref<'code' | 'name' | 'updatedAt' | 'nextFollowUpAt'>('updatedAt')
const sortDescending = ref(true)
const selectedCustomer = ref<CustomerDetails | null>(null)
const editingCustomer = ref<CustomerDetails | null>(null)
const editingContact = ref<ContactDetails | null>(null)
const detailVisible = ref(false)
const customerDialogVisible = ref(false)
const contactDialogVisible = ref(false)
const followUpDialogVisible = ref(false)

onMounted(async () => {
  await loadCustomers()
  await openCustomerFromRoute(route.query.entityId)
})

watch(() => route.query.entityId, openCustomerFromRoute)

async function loadCustomers() {
  loading.value = true
  try {
    const result = await customerApi.listCustomers({
      page: page.value,
      pageSize: pageSize.value,
      search: search.value.trim() || undefined,
      archive: archive.value,
      status: status.value || undefined,
      level: level.value || undefined,
      ...customerRegionParameters(region.value),
      sortBy: sortBy.value,
      sortDescending: sortDescending.value,
      nextFollowUpBeforeUtc: dueBefore.value
        ? dayjs(dueBefore.value).endOf('day').toISOString()
        : undefined,
    })
    customers.value = result.items
    total.value = result.total
  } catch (error) {
    showError(error, '客户列表加载失败。')
  } finally {
    loading.value = false
  }
}

async function openCustomer(row: CustomerSummary) {
  await openCustomerById(row.id)
}

async function openFollowUp(value: unknown) {
  const row = value as CustomerSummary
  loading.value = true
  try {
    selectedCustomer.value = await customerApi.getCustomer(row.id)
    followUpDialogVisible.value = true
  } catch (error) {
    showError(error, '客户信息加载失败，暂时无法记录跟进。')
  } finally {
    loading.value = false
  }
}

async function openCustomerFromRoute(value: unknown) {
  const raw = Array.isArray(value) ? value[0] : value
  const id = Number(raw)
  if (!Number.isSafeInteger(id) || id <= 0 || selectedCustomer.value?.id === id) {
    return
  }
  await openCustomerById(id)
}

async function openCustomerById(customerId: number) {
  loading.value = true
  try {
    selectedCustomer.value = await customerApi.getCustomer(customerId)
    detailVisible.value = true
  } catch (error) {
    showError(error, '客户详情加载失败。')
  } finally {
    loading.value = false
  }
}

async function refreshSelectedCustomer() {
  if (!selectedCustomer.value) return
  selectedCustomer.value = await customerApi.getCustomer(selectedCustomer.value.id)
}

function openCreateCustomer() {
  editingCustomer.value = null
  customerDialogVisible.value = true
}

function openEditCustomer() {
  editingCustomer.value = selectedCustomer.value
  customerDialogVisible.value = true
}

async function saveCustomer(input: CustomerInput | UpdateCustomerInput) {
  saving.value = true
  try {
    const saved =
      editingCustomer.value && 'version' in input
        ? await customerApi.updateCustomer(editingCustomer.value.id, input)
        : await customerApi.createCustomer(input)
    selectedCustomer.value = saved
    customerDialogVisible.value = false
    detailVisible.value = true
    await loadCustomers()
    ElMessage.success(editingCustomer.value ? '客户已更新。' : '客户已创建。')
  } catch (error) {
    showError(error, '客户保存失败。')
  } finally {
    saving.value = false
  }
}

function openCreateContact() {
  editingContact.value = null
  contactDialogVisible.value = true
}

function openEditContact(contact: ContactDetails) {
  editingContact.value = contact
  contactDialogVisible.value = true
}

async function saveContact(input: ContactInput | UpdateContactInput) {
  if (!selectedCustomer.value) return
  saving.value = true
  try {
    if (editingContact.value && 'version' in input) {
      await customerApi.updateContact(
        selectedCustomer.value.id,
        editingContact.value.id,
        input,
      )
    } else {
      await customerApi.createContact(selectedCustomer.value.id, input)
    }
    contactDialogVisible.value = false
    await Promise.all([refreshSelectedCustomer(), loadCustomers()])
    ElMessage.success(editingContact.value ? '联系人已更新。' : '联系人已添加。')
  } catch (error) {
    showError(error, '联系人保存失败。')
  } finally {
    saving.value = false
  }
}

async function removeContact(contact: ContactDetails) {
  if (!selectedCustomer.value) return
  try {
    await ElMessageBox.confirm(
      `确定删除联系人“${contact.name}”吗？历史跟进记录仍会保留。`,
      '删除联系人',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
    await customerApi.deleteContact(
      selectedCustomer.value.id,
      contact.id,
      contact.version,
    )
    await Promise.all([refreshSelectedCustomer(), loadCustomers()])
    ElMessage.success('联系人已删除。')
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    showError(error, '联系人删除失败。')
  }
}

async function saveFollowUp(input: CustomerFollowUpInput) {
  if (!selectedCustomer.value) return
  saving.value = true
  try {
    await customerApi.createFollowUp(selectedCustomer.value.id, input)
    followUpDialogVisible.value = false
    await Promise.all([refreshSelectedCustomer(), loadCustomers()])
    ElMessage.success('跟进记录已保存。')
  } catch (error) {
    showError(error, '跟进记录保存失败。')
  } finally {
    saving.value = false
  }
}

async function toggleArchive(value: unknown) {
  const row = value as CustomerSummary
  const action = row.isArchived ? '恢复' : '归档'
  try {
    await ElMessageBox.confirm(
      `${action}客户“${row.name}”？${row.isArchived ? '' : '归档后将不能继续编辑或新增跟进。'}`,
      `${action}客户`,
      { type: 'warning', confirmButtonText: action, cancelButtonText: '取消' },
    )
    const updated = await customerApi.setCustomerArchived(
      row.id,
      !row.isArchived,
      row.version,
    )
    if (selectedCustomer.value?.id === row.id) {
      selectedCustomer.value = updated
    }
    await loadCustomers()
    ElMessage.success(`客户已${action}。`)
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    showError(error, `客户${action}失败。`)
  }
}

async function runSearch() {
  page.value = 1
  await loadCustomers()
}

async function changeSort(value: { prop?: string | null; order?: string | null }) {
  if (!value.prop) return
  const mapping: Record<string, typeof sortBy.value> = {
    code: 'code',
    name: 'name',
    updatedAtUtc: 'updatedAt',
    nextFollowUpAtUtc: 'nextFollowUpAt',
  }
  const nextSort = mapping[value.prop]
  if (!nextSort) return
  sortBy.value = nextSort
  sortDescending.value = value.order !== 'ascending'
  await loadCustomers()
}

function showError(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    const validation = Object.values(error.problem?.errors ?? {}).flat()[0]
    ElMessage.error(
      validation ?? error.problem?.detail ?? error.problem?.title ?? fallback,
    )
    return
  }
  ElMessage.error(fallback)
}
</script>

<template>
  <section class="customer-page">
    <div class="customer-toolbar">
      <el-input
        v-model="search"
        class="customer-search"
        clearable
        placeholder="搜索客户编号、名称或电话"
        :prefix-icon="Search"
        @keyup.enter="runSearch"
        @clear="runSearch"
      />
      <div class="customer-filter-row">
        <el-select
          v-model="archive"
          class="customer-filter"
          aria-label="记录状态"
          @change="runSearch"
        >
          <el-option
            label="在用记录"
            value="active"
          />
          <el-option
            label="已归档"
            value="archived"
          />
          <el-option
            label="全部记录"
            value="all"
          />
        </el-select>
        <el-select
          v-model="status"
          class="customer-filter"
          clearable
          placeholder="客户状态"
          aria-label="客户状态"
          @change="runSearch"
        >
          <el-option
            v-for="item in customerStatusOptions"
            :key="item.value"
            :label="item.label"
            :value="item.value"
          />
        </el-select>
        <el-select
          v-model="level"
          class="customer-filter"
          clearable
          placeholder="客户等级"
          aria-label="客户等级"
          @change="runSearch"
        >
          <el-option
            v-for="item in customerLevelOptions"
            :key="item.value"
            :label="item.label"
            :value="item.value"
          />
        </el-select>
        <el-date-picker
          v-model="dueBefore"
          type="date"
          value-format="YYYY-MM-DD"
          format="YYYY-MM-DD"
          placeholder="下次跟进截止"
          clearable
          @change="runSearch"
        />
        <el-cascader
          v-model="region"
          class="customer-region-filter"
          :options="chinaRegionOptions"
          :props="chinaRegionCascaderProps"
          clearable
          filterable
          placeholder="省 / 市 / 区县"
          aria-label="客户所在地区"
          @change="runSearch"
        />
        <el-button
          :icon="Refresh"
          @click="loadCustomers"
        >
          刷新
        </el-button>
        <el-button
          v-if="canManage"
          class="toolbar-primary-action"
          type="primary"
          :icon="Plus"
          @click="openCreateCustomer"
        >
          新建客户
        </el-button>
      </div>
    </div>

    <el-table
      v-loading="loading"
      class="flowhearth-data-table"
      :data="customers"
      border
      stripe
      row-class-name="customer-table-row"
      @row-click="openCustomer"
      @sort-change="changeSort"
    >
      <el-table-column
        prop="code"
        label="客户编号"
        width="150"
        sortable="custom"
      />
      <el-table-column
        prop="name"
        label="客户名称"
        min-width="460"
        sortable="custom"
      >
        <template #default="scope">
          <strong>{{ scope.row.name }}</strong>
          <p class="cell-secondary">
            {{ scope.row.shortName || scope.row.industry || '—' }}
          </p>
        </template>
      </el-table-column>
      <el-table-column
        label="联系人"
        min-width="150"
      >
        <template #default="scope">
          <span>{{ scope.row.primaryContactName || '—' }}</span>
          <p class="cell-secondary">
            {{ scope.row.contactCount }} 位联系人
          </p>
        </template>
      </el-table-column>
      <el-table-column
        label="联系方式"
        min-width="180"
      >
        <template #default="scope">
          <span>{{ customerContactMethod(scope.row) }}</span>
        </template>
      </el-table-column>
      <el-table-column
        prop="nextFollowUpAtUtc"
        label="下次跟进"
        width="175"
        sortable="custom"
      >
        <template #default="scope">
          {{ formatChinaDateTime(scope.row.nextFollowUpAtUtc) }}
        </template>
      </el-table-column>
      <el-table-column
        prop="updatedAtUtc"
        label="最近更新"
        width="175"
        sortable="custom"
      >
        <template #default="scope">
          {{ formatChinaDateTime(scope.row.updatedAtUtc) }}
        </template>
      </el-table-column>
      <el-table-column
        label="客户状态"
        width="112"
      >
        <template #default="scope">
          <el-tag :type="customerStatusTagType(scope.row.status)">
            {{ customerStatusLabel(scope.row.status) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column
        label="客户等级"
        width="116"
      >
        <template #default="scope">
          {{ customerLevelLabel(scope.row.level) }}
        </template>
      </el-table-column>
      <el-table-column
        v-if="archive !== 'active'"
        label="记录状态"
        width="100"
      >
        <template #default="scope">
          <el-tag :type="scope.row.isArchived ? 'info' : 'success'">
            {{ scope.row.isArchived ? '已归档' : '在用' }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column
        v-if="canManage"
        label="操作"
        width="170"
        fixed="right"
      >
        <template #default="scope">
          <el-button
            v-if="!scope.row.isArchived"
            link
            type="primary"
            @click.stop="openFollowUp(scope.row)"
          >
            跟进
          </el-button>
          <el-button
            link
            :type="scope.row.isArchived ? 'primary' : 'danger'"
            @click.stop="toggleArchive(scope.row)"
          >
            {{ scope.row.isArchived ? '恢复' : '归档' }}
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-empty
      v-if="!loading && customers.length === 0"
      description="暂无符合条件的客户"
    />

    <el-pagination
      v-if="total > 0"
      v-model:current-page="page"
      v-model:page-size="pageSize"
      class="table-pagination"
      layout="total, sizes, prev, pager, next"
      :page-sizes="[10, 20, 50, 100]"
      :total="total"
      @change="loadCustomers"
    />
  </section>

  <CustomerDetailDrawer
    v-model="detailVisible"
    :customer="selectedCustomer"
    :can-manage="canManage"
    :initial-tab="route.query.tab === 'finance' ? 'finance' : undefined"
    @edit-customer="openEditCustomer"
    @add-contact="openCreateContact"
    @edit-contact="openEditContact"
    @delete-contact="removeContact"
    @add-follow-up="followUpDialogVisible = true"
  />
  <CustomerFormDialog
    v-model="customerDialogVisible"
    :customer="editingCustomer"
    :saving="saving"
    @save="saveCustomer"
  />
  <ContactFormDialog
    v-model="contactDialogVisible"
    :contact="editingContact"
    :saving="saving"
    @save="saveContact"
  />
  <FollowUpDialog
    v-model="followUpDialogVisible"
    :contacts="selectedCustomer?.contacts ?? []"
    :customer-status="selectedCustomer?.status"
    :saving="saving"
    @save="saveFollowUp"
  />
</template>
