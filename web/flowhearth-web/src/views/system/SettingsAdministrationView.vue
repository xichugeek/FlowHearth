<script setup lang="ts">
import { Edit, Plus, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { computed, onMounted, reactive, ref } from 'vue'

import { ApiError } from '../../api/problem-details'
import * as settingsApi from '../../api/settings'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import { useSettingsStore } from '../../stores/settings'
import type {
  LookupItem,
  SettingsAdministrationSnapshot,
  SystemSetting,
} from '../../types/settings'

const authStore = useAuthStore()
const settingsStore = useSettingsStore()
const canManage = computed(() => authStore.canAny([permissions.settingsManage]))
const loading = ref(false)
const saving = ref(false)
const activeTab = ref('dictionaries')
const snapshot = ref<SettingsAdministrationSnapshot>({ dictionaries: [], settings: [] })
const selectedDictionaryCode = ref('')
const lookupDialogVisible = ref(false)
const settingDialogVisible = ref(false)

const lookupForm = reactive({
  id: null as number | null,
  dictionaryCode: '',
  value: '',
  label: '',
  description: '',
  sortOrder: 0,
  isActive: true,
  version: 0,
})
const settingForm = reactive({
  key: '',
  name: '',
  value: '',
  valueType: '' as SystemSetting['valueType'] | '',
  version: 0,
})

const activeDictionary = computed(() =>
  snapshot.value.dictionaries.find(
    (dictionary) => dictionary.code === selectedDictionaryCode.value,
  ),
)

onMounted(load)

async function load() {
  loading.value = true
  try {
    snapshot.value = await settingsApi.getSettingsAdministration()
    if (
      !selectedDictionaryCode.value ||
      !snapshot.value.dictionaries.some(
        (dictionary) => dictionary.code === selectedDictionaryCode.value,
      )
    ) {
      selectedDictionaryCode.value = snapshot.value.dictionaries[0]?.code ?? ''
    }
  } catch (error) {
    showError(error, '字典与系统设置加载失败。')
  } finally {
    loading.value = false
  }
}

function openCreateLookup() {
  if (!activeDictionary.value) return
  Object.assign(lookupForm, {
    id: null,
    dictionaryCode: activeDictionary.value.code,
    value: '',
    label: '',
    description: '',
    sortOrder:
      Math.max(0, ...activeDictionary.value.items.map((item) => item.sortOrder)) + 10,
    isActive: true,
    version: 0,
  })
  lookupDialogVisible.value = true
}

function openEditLookup(value: unknown) {
  const item = value as LookupItem
  Object.assign(lookupForm, {
    id: item.id,
    dictionaryCode: item.dictionaryCode,
    value: item.value,
    label: item.label,
    description: item.description ?? '',
    sortOrder: item.sortOrder,
    isActive: item.isActive,
    version: item.version,
  })
  lookupDialogVisible.value = true
}

async function saveLookup() {
  if (!lookupForm.value.trim() || !lookupForm.label.trim()) {
    ElMessage.warning('请填写字典值和显示名称。')
    return
  }

  saving.value = true
  try {
    if (lookupForm.id === null) {
      await settingsApi.createLookupItem({
        dictionaryCode: lookupForm.dictionaryCode,
        value: lookupForm.value.trim(),
        label: lookupForm.label.trim(),
        description: lookupForm.description.trim() || null,
        sortOrder: lookupForm.sortOrder,
      })
    } else {
      await settingsApi.updateLookupItem(lookupForm.id, {
        label: lookupForm.label.trim(),
        description: lookupForm.description.trim() || null,
        sortOrder: lookupForm.sortOrder,
        isActive: lookupForm.isActive,
        version: lookupForm.version,
      })
    }
    lookupDialogVisible.value = false
    await refreshAfterMutation()
    ElMessage.success('字典项已保存。')
  } catch (error) {
    showError(error, '字典项保存失败。')
  } finally {
    saving.value = false
  }
}

function openSetting(setting: SystemSetting) {
  Object.assign(settingForm, {
    key: setting.key,
    name: setting.name,
    value: setting.value,
    valueType: setting.valueType,
    version: setting.version,
  })
  settingDialogVisible.value = true
}

async function saveSetting() {
  if (!settingForm.value.trim()) {
    ElMessage.warning('设置值不能为空。')
    return
  }

  saving.value = true
  try {
    await settingsApi.updateSystemSetting(settingForm.key, {
      value: settingForm.value.trim(),
      version: settingForm.version,
    })
    settingDialogVisible.value = false
    await refreshAfterMutation()
    ElMessage.success('系统设置已更新，新请求将立即使用该值。')
  } catch (error) {
    showError(error, '系统设置保存失败。')
  } finally {
    saving.value = false
  }
}

async function refreshAfterMutation() {
  await Promise.all([load(), settingsStore.initialize(true)])
}

function settingDisplay(setting: SystemSetting) {
  if (setting.key === 'ui.default_page_size') return `${setting.value} 条`
  return setting.value
}

function formatUpdatedAt(value: string) {
  return dayjs(value).format('YYYY-MM-DD HH:mm')
}

function showError(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    const validation = Object.values(error.problem?.errors ?? {}).flat()[0]
    ElMessage.error(validation ?? error.message ?? fallback)
    return
  }
  ElMessage.error(fallback)
}
</script>

<template>
  <section class="settings-page">
    <div class="compact-page-actions">
      <el-button
        :icon="Refresh"
        :loading="loading"
        @click="load"
      >
        刷新
      </el-button>
    </div>

    <el-alert
      class="settings-safety"
      type="info"
      :closable="false"
      show-icon
      title="编号前缀只影响之后创建的数据，既有业务编号不会重写；停用字典项也不会修改历史记录。"
    />

    <el-tabs v-model="activeTab">
      <el-tab-pane
        label="业务字典"
        name="dictionaries"
      >
        <div class="dictionary-layout">
          <el-card
            class="dictionary-nav"
            shadow="never"
          >
            <button
              v-for="dictionary in snapshot.dictionaries"
              :key="dictionary.code"
              type="button"
              :class="{ active: dictionary.code === selectedDictionaryCode }"
              @click="selectedDictionaryCode = dictionary.code"
            >
              <strong>{{ dictionary.name }}</strong>
              <span>{{ dictionary.items.filter((item) => item.isActive).length }} 个启用项</span>
            </button>
          </el-card>

          <el-card
            v-loading="loading"
            shadow="never"
          >
            <template #header>
              <div class="card-heading">
                <div>
                  <strong>{{ activeDictionary?.name ?? '业务字典' }}</strong>
                  <p>{{ activeDictionary?.description }}</p>
                </div>
                <el-button
                  v-if="canManage"
                  type="primary"
                  :icon="Plus"
                  @click="openCreateLookup"
                >
                  新增字典项
                </el-button>
              </div>
            </template>
            <el-table
              class="flowhearth-data-table"
              :data="activeDictionary?.items ?? []"
              border
              stripe
            >
              <el-table-column
                prop="label"
                label="显示名称"
                min-width="150"
              />
              <el-table-column
                prop="value"
                label="存储值"
                min-width="150"
              />
              <el-table-column
                prop="description"
                label="说明"
                min-width="180"
                show-overflow-tooltip
              />
              <el-table-column
                prop="sortOrder"
                label="排序"
                width="90"
              />
              <el-table-column
                label="状态"
                width="90"
              >
                <template #default="scope">
                  <el-tag :type="scope.row.isActive ? 'success' : 'info'">
                    {{ scope.row.isActive ? '启用' : '停用' }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column
                v-if="canManage"
                label="操作"
                width="90"
                fixed="right"
              >
                <template #default="scope">
                  <el-button
                    link
                    type="primary"
                    :icon="Edit"
                    @click="openEditLookup(scope.row)"
                  >
                    编辑
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane
        label="系统设置"
        name="settings"
      >
        <div
          v-loading="loading"
          class="setting-grid"
        >
          <el-card
            v-for="setting in snapshot.settings"
            :key="setting.key"
            shadow="never"
            class="setting-card"
          >
            <div class="setting-card-top">
              <div>
                <small>{{ setting.key }}</small>
                <h3>{{ setting.name }}</h3>
              </div>
              <el-button
                v-if="canManage"
                link
                type="primary"
                :icon="Edit"
                @click="openSetting(setting)"
              >
                修改
              </el-button>
            </div>
            <strong class="setting-value">{{ settingDisplay(setting) }}</strong>
            <p>{{ setting.description }}</p>
            <footer>更新于 {{ formatUpdatedAt(setting.updatedAtUtc) }}</footer>
          </el-card>
        </div>
      </el-tab-pane>
    </el-tabs>

    <el-dialog
      v-model="lookupDialogVisible"
      draggable
      :title="lookupForm.id === null ? '新增字典项' : '编辑字典项'"
      width="min(600px, 94vw)"
    >
      <el-form label-position="top">
        <div class="two-column-form">
          <el-form-item
            label="字典值"
            required
          >
            <el-input
              v-model="lookupForm.value"
              maxlength="100"
              :disabled="lookupForm.id !== null"
            />
          </el-form-item>
          <el-form-item
            label="显示名称"
            required
          >
            <el-input
              v-model="lookupForm.label"
              maxlength="100"
            />
          </el-form-item>
        </div>
        <el-form-item label="说明">
          <el-input
            v-model="lookupForm.description"
            type="textarea"
            :rows="2"
            maxlength="500"
          />
        </el-form-item>
        <div class="two-column-form">
          <el-form-item label="排序值">
            <el-input-number
              v-model="lookupForm.sortOrder"
              :min="-100000"
              :max="100000"
            />
          </el-form-item>
          <el-form-item
            v-if="lookupForm.id !== null"
            label="状态"
          >
            <el-switch
              v-model="lookupForm.isActive"
              active-text="启用"
              inactive-text="停用"
            />
          </el-form-item>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="lookupDialogVisible = false">
          取消
        </el-button>
        <el-button
          type="primary"
          :loading="saving"
          @click="saveLookup"
        >
          保存
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="settingDialogVisible"
      draggable
      :title="`修改${settingForm.name}`"
      width="min(520px, 94vw)"
    >
      <el-form label-position="top">
        <el-form-item
          label="设置值"
          required
        >
          <el-select
            v-if="settingForm.valueType === 'WholeNumber'"
            v-model="settingForm.value"
            style="width: 100%"
          >
            <el-option
              v-for="size in [10, 20, 50, 100]"
              :key="size"
              :label="`${size} 条`"
              :value="String(size)"
            />
          </el-select>
          <el-select
            v-else-if="settingForm.valueType === 'TimeZone'"
            v-model="settingForm.value"
            filterable
            allow-create
            default-first-option
            style="width: 100%"
          >
            <el-option
              label="中国标准时间 · Asia/Shanghai"
              value="Asia/Shanghai"
            />
            <el-option
              label="协调世界时 · UTC"
              value="UTC"
            />
          </el-select>
          <el-input
            v-else
            v-model="settingForm.value"
            maxlength="8"
            placeholder="2-8 位大写字母或数字"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="settingDialogVisible = false">
          取消
        </el-button>
        <el-button
          type="primary"
          :loading="saving"
          @click="saveSetting"
        >
          保存
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.settings-page { display: grid; gap: 18px; }
.settings-safety { margin-top: -4px; }
.dictionary-layout { display: grid; grid-template-columns: 240px minmax(0, 1fr); gap: 16px; }
.dictionary-nav :deep(.el-card__body) { display: grid; gap: 8px; padding: 10px; }
.dictionary-nav button { border: 0; border-radius: 10px; background: transparent; color: #64748b; cursor: pointer; display: grid; gap: 4px; padding: 12px; text-align: left; }
.dictionary-nav button:hover, .dictionary-nav button.active { background: rgba(23, 58, 94, .08); color: var(--flowhearth-ink); }
.dictionary-nav button span { font-size: 12px; opacity: .8; }
.card-heading, .setting-card-top { align-items: flex-start; display: flex; justify-content: space-between; gap: 16px; }
.card-heading p, .setting-card p { color: #64748b; margin: 6px 0 0; }
.setting-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 14px; }
.setting-card small { color: #94a3b8; font-family: ui-monospace, monospace; }
.setting-card h3 { font-size: 16px; margin: 6px 0 0; }
.setting-value { color: var(--flowhearth-ink); display: block; font-size: 24px; margin-top: 20px; }
.setting-card footer { color: #94a3b8; font-size: 12px; margin-top: 18px; }
@media (max-width: 820px) { .dictionary-layout { grid-template-columns: 1fr; } .dictionary-nav :deep(.el-card__body) { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
@media (max-width: 520px) { .dictionary-nav :deep(.el-card__body) { grid-template-columns: 1fr; } }
</style>
