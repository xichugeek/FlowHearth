<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { computed, reactive, watch } from 'vue'

import { useSettingsStore } from '../../stores/settings'
import { lookupDictionaryCodes } from '../../types/settings'
import type {
  CustomerDetails,
  CustomerInput,
  UpdateCustomerInput,
} from '../../types/customers'
import type { CustomerLevel, CustomerStatus } from '../../types/customers'
import {
  customerLevelOptions,
  customerStatusOptions,
} from '../../utils/customer-format'
import {
  chinaRegionCascaderProps,
  chinaRegionOptions,
  customerRegionParameters,
  customerRegionPath,
} from '../../utils/china-regions'

const props = defineProps<{
  modelValue: boolean
  customer?: CustomerDetails | null
  saving?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  save: [value: CustomerInput | UpdateCustomerInput]
}>()

const settingsStore = useSettingsStore()
const industryOptions = computed(() =>
  settingsStore.lookupItems(lookupDictionaryCodes.customerIndustry),
)

const form = reactive({
  name: '',
  shortName: '',
  industry: '',
  status: 'Active' as CustomerStatus,
  level: 'Unrated' as CustomerLevel,
  phone: '',
  email: '',
  website: '',
  region: [] as string[],
  address: '',
  notes: '',
})

watch(
  () => [props.modelValue, props.customer] as const,
  ([visible, customer]) => {
    if (!visible) return
    Object.assign(form, {
      name: customer?.name ?? '',
      shortName: customer?.shortName ?? '',
      industry: customer?.industry ?? '',
      status: customer?.status ?? 'Active',
      level: customer?.level ?? 'Unrated',
      phone: customer?.phone ?? '',
      email: customer?.email ?? '',
      website: customer?.website ?? '',
      region: customerRegionPath(customer ?? {}),
      address: customer?.address ?? '',
      notes: customer?.notes ?? '',
    })
  },
  { immediate: true },
)

function submit() {
  if (!form.name.trim()) {
    ElMessage.warning('请填写客户名称。')
    return
  }

  const input: CustomerInput = {
    name: form.name.trim(),
    shortName: nullable(form.shortName),
    industry: nullable(form.industry),
    status: form.status,
    level: form.level,
    phone: nullable(form.phone),
    email: nullable(form.email),
    website: nullable(form.website),
    ...customerRegionParameters(form.region),
    address: nullable(form.address),
    notes: nullable(form.notes),
  }
  emit(
    'save',
    props.customer
      ? {
          ...input,
          version: props.customer.version,
          clearRegion:
            customerRegionPath(props.customer).length > 0 && form.region.length === 0,
        }
      : input,
  )
}

function nullable(value: string) {
  return value.trim() || null
}
</script>

<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="customer ? '编辑客户' : '新建客户'"
    width="min(720px, 94vw)"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <div class="two-column-form">
        <el-form-item
          label="客户名称"
          required
        >
          <el-input
            v-model="form.name"
            maxlength="200"
          />
        </el-form-item>
        <el-form-item label="客户简称">
          <el-input
            v-model="form.shortName"
            maxlength="100"
          />
        </el-form-item>
        <el-form-item label="所属行业">
          <el-select
            v-model="form.industry"
            filterable
            allow-create
            default-first-option
            clearable
            style="width: 100%"
          >
            <el-option
              v-for="item in industryOptions"
              :key="item.id"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="客户状态">
          <el-select
            v-model="form.status"
            style="width: 100%"
          >
            <el-option
              v-for="item in customerStatusOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="客户等级">
          <el-select
            v-model="form.level"
            style="width: 100%"
          >
            <el-option
              v-for="item in customerLevelOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="联系电话">
          <el-input
            v-model="form.phone"
            maxlength="50"
          />
        </el-form-item>
        <el-form-item label="邮箱">
          <el-input
            v-model="form.email"
            maxlength="254"
          />
        </el-form-item>
        <el-form-item label="网站">
          <el-input
            v-model="form.website"
            maxlength="500"
            placeholder="https://example.com"
          />
        </el-form-item>
      </div>
      <el-form-item label="公司所在地区">
        <el-cascader
          v-model="form.region"
          :options="chinaRegionOptions"
          :props="chinaRegionCascaderProps"
          clearable
          filterable
          placeholder="请选择省、市、区或县"
          style="width: 100%"
        />
      </el-form-item>
      <el-form-item label="地址">
        <el-input
          v-model="form.address"
          maxlength="500"
        />
      </el-form-item>
      <el-form-item label="备注">
        <el-input
          v-model="form.notes"
          type="textarea"
          :rows="4"
          maxlength="4000"
        />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="emit('update:modelValue', false)">
        取消
      </el-button>
      <el-button
        type="primary"
        :loading="saving"
        @click="submit"
      >
        保存
      </el-button>
    </template>
  </el-dialog>
</template>
