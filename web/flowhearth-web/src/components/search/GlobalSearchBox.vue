<script setup lang="ts">
import { Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { onBeforeUnmount, ref } from 'vue'
import { useRouter } from 'vue-router'

import { globalSearch } from '../../api/search'
import type { GlobalSearchResult } from '../../types/search'
import {
  globalSearchKindLabel,
  routeForSearchResult,
} from '../../utils/global-search'

const router = useRouter()
const selectedKey = ref('')
const results = ref<GlobalSearchResult[]>([])
const loading = ref(false)
let requestSequence = 0
let debounceTimer: ReturnType<typeof setTimeout> | undefined

function resultKey(result: GlobalSearchResult) {
  return `${result.kind}:${result.targetId}`
}

function remoteSearch(value: string) {
  if (debounceTimer) clearTimeout(debounceTimer)
  const query = value.trim()
  if (query.length < 2) {
    requestSequence += 1
    results.value = []
    loading.value = false
    return
  }

  debounceTimer = setTimeout(() => void performSearch(query), 250)
}

async function performSearch(query: string) {
  const sequence = ++requestSequence
  loading.value = true
  try {
    const response = await globalSearch(query)
    if (sequence === requestSequence) results.value = response
  } catch {
    if (sequence === requestSequence) {
      results.value = []
      ElMessage.error('全局搜索失败，请稍后重试。')
    }
  } finally {
    if (sequence === requestSequence) loading.value = false
  }
}

async function selectResult(value: string) {
  const result = results.value.find((item) => resultKey(item) === value)
  if (!result) return
  await router.push(routeForSearchResult(result))
  selectedKey.value = ''
  results.value = []
}

onBeforeUnmount(() => {
  if (debounceTimer) clearTimeout(debounceTimer)
  requestSequence += 1
})
</script>

<template>
  <el-select
    v-model="selectedKey"
    class="global-search"
    aria-label="全局搜索"
    filterable
    remote
    clearable
    reserve-keyword
    :remote-method="remoteSearch"
    :loading="loading"
    placeholder="搜索客户、项目、设备、服务、采购与出货"
    no-match-text="未找到匹配结果"
    @change="selectResult"
  >
    <template #prefix>
      <el-icon><Search /></el-icon>
    </template>
    <el-option
      v-for="item in results"
      :key="resultKey(item)"
      :value="resultKey(item)"
      :label="`${item.code} ${item.title}`"
    >
      <div class="global-search-option">
        <el-tag
          size="small"
          effect="plain"
        >
          {{ globalSearchKindLabel(item.kind) }}
        </el-tag>
        <span class="global-search-copy">
          <strong>{{ item.title }}</strong>
          <small>{{ item.code }}<template v-if="item.subtitle"> · {{ item.subtitle }}</template></small>
        </span>
        <el-tag
          v-if="item.isArchived"
          size="small"
          type="info"
        >
          已归档
        </el-tag>
      </div>
    </el-option>
  </el-select>
</template>
