<script setup lang="ts">
import { Delete, Download, UploadFilled } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ElMessageBox } from '../../utils/flowhearth-message-box'
import { computed, ref, watch } from 'vue'

import {
  deleteAttachment,
  downloadAttachment,
  listAttachments,
  uploadAttachment,
} from '../../api/attachments'
import { permissions } from '../../security/permissions'
import { useAuthStore } from '../../stores/auth'
import type { AttachmentEntityType, AttachmentSummary } from '../../types/attachments'
import { formatChinaDateTime } from '../../utils/customer-format'
import { formatFileSize } from '../../utils/attachment-format'

const props = defineProps<{
  entityType: AttachmentEntityType
  entityId: number
}>()

const authStore = useAuthStore()
const attachments = ref<AttachmentSummary[]>([])
const loading = ref(false)
const uploading = ref(false)
const deletingId = ref<number>()
const fileInput = ref<HTMLInputElement>()
const canView = computed(() => authStore.canAny([permissions.attachmentsView]))
const canManage = computed(() => authStore.canAny([permissions.attachmentsManage]))

async function load() {
  if (!canView.value || !props.entityId) {
    attachments.value = []
    return
  }

  loading.value = true
  try {
    attachments.value = await listAttachments(props.entityType, props.entityId)
  } catch {
    ElMessage.error('附件列表加载失败。')
  } finally {
    loading.value = false
  }
}

function chooseFile() {
  fileInput.value?.click()
}

async function handleFileSelected(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (file.size === 0) {
    ElMessage.warning('不能上传空文件。')
    return
  }
  if (file.size > 20 * 1024 * 1024) {
    ElMessage.warning('文件不能超过 20 MiB。')
    return
  }

  uploading.value = true
  try {
    await uploadAttachment(props.entityType, props.entityId, file)
    ElMessage.success('附件已上传。')
    await load()
  } catch {
    ElMessage.error('附件上传失败，请检查文件名和大小后重试。')
  } finally {
    uploading.value = false
  }
}

async function handleDownload(attachment: AttachmentSummary) {
  try {
    const blob = await downloadAttachment(attachment.id)
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = attachment.originalFileName
    anchor.style.display = 'none'
    document.body.append(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)
  } catch {
    ElMessage.error('附件下载失败或无权访问。')
  }
}

async function handleDelete(attachment: AttachmentSummary) {
  await ElMessageBox.confirm(
    `确定移除附件“${attachment.originalFileName}”吗？`,
    '移除附件',
    { type: 'warning', confirmButtonText: '移除', cancelButtonText: '取消' },
  )
  deletingId.value = attachment.id
  try {
    await deleteAttachment(attachment.id, attachment.version)
    ElMessage.success('附件已移除。')
    await load()
  } catch {
    ElMessage.error('附件移除失败，请刷新后重试。')
  } finally {
    deletingId.value = undefined
  }
}

watch(
  () => [props.entityType, props.entityId, canView.value] as const,
  () => void load(),
  { immediate: true },
)
</script>

<template>
  <section
    v-if="canView"
    class="entity-attachments"
  >
    <div class="detail-tab-toolbar">
      <div>
        <h4>附件（{{ attachments.length }}）</h4>
        <p>单个文件最大 20 MiB，文件通过授权接口下载。</p>
      </div>
      <div v-if="canManage">
        <input
          ref="fileInput"
          class="visually-hidden"
          type="file"
          @change="handleFileSelected"
        >
        <el-button
          type="primary"
          plain
          :icon="UploadFilled"
          :loading="uploading"
          @click="chooseFile"
        >
          上传附件
        </el-button>
      </div>
    </div>
    <el-table
      v-loading="loading"
      :data="attachments"
      size="small"
      empty-text="暂无附件"
    >
      <el-table-column
        label="文件"
        min-width="220"
      >
        <template #default="scope">
          <strong>{{ scope.row.originalFileName }}</strong>
          <p class="cell-secondary">
            {{ scope.row.contentType }} · {{ formatFileSize(scope.row.sizeBytes) }}
          </p>
        </template>
      </el-table-column>
      <el-table-column
        label="上传信息"
        min-width="180"
      >
        <template #default="scope">
          {{ scope.row.uploadedByDisplayName || '未知用户' }}
          <p class="cell-secondary">
            {{ formatChinaDateTime(scope.row.uploadedAtUtc) }}
          </p>
        </template>
      </el-table-column>
      <el-table-column
        label="操作"
        width="150"
        fixed="right"
      >
        <template #default="scope">
          <el-button
            link
            :icon="Download"
            @click="handleDownload(scope.row as AttachmentSummary)"
          >
            下载
          </el-button>
          <el-button
            v-if="canManage"
            link
            type="danger"
            :icon="Delete"
            :loading="deletingId === scope.row.id"
            @click="handleDelete(scope.row as AttachmentSummary)"
          >
            移除
          </el-button>
        </template>
      </el-table-column>
    </el-table>
  </section>
</template>
