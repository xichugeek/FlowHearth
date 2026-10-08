<script setup lang="ts">
import { ElMessage } from 'element-plus'
import { reactive, watch } from 'vue'
import type { ServiceRecordDetails, ServiceRecordInput, UpdateServiceRecordInput } from '../../types/service'
const props = defineProps<{ modelValue: boolean; record?: ServiceRecordDetails | null; saving?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; save: [ServiceRecordInput | UpdateServiceRecordInput] }>()
const form = reactive({ recordType: 'Note' as 'Note'|'Diagnosis'|'Action', content: '', durationMinutes: undefined as number|undefined, occurredAtUtc: '' })
watch(() => [props.modelValue, props.record] as const, ([visible,item]) => { if (visible) Object.assign(form,{ recordType: item?.recordType === 'Diagnosis'||item?.recordType === 'Action' ? item.recordType : 'Note', content:item?.content??'', durationMinutes:item?.durationMinutes??undefined, occurredAtUtc:item?.occurredAtUtc?.slice(0,16)??new Date().toISOString().slice(0,16) }) }, { immediate:true })
function submit(){if(!form.content.trim()){ElMessage.warning('请填写服务记录内容。');return} const base:ServiceRecordInput={recordType:form.recordType,content:form.content.trim(),durationMinutes:form.durationMinutes??null,occurredAtUtc:new Date(form.occurredAtUtc).toISOString()};emit('save',props.record?{...base,occurredAtUtc:base.occurredAtUtc!,version:props.record.version}:base)}
</script>
<template>
  <el-dialog
    draggable
    :model-value="modelValue"
    :title="record?'编辑服务记录':'添加服务记录'"
    width="min(660px,94vw)"
    @update:model-value="emit('update:modelValue',$event)"
  >
    <el-form label-position="top">
      <div class="two-column-form">
        <el-form-item label="类型">
          <el-select
            v-model="form.recordType"
            style="width:100%"
          >
            <el-option
              label="备注"
              value="Note"
            /><el-option
              label="诊断"
              value="Diagnosis"
            /><el-option
              label="处理"
              value="Action"
            />
          </el-select>
        </el-form-item><el-form-item label="发生时间">
          <el-date-picker
            v-model="form.occurredAtUtc"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm"
            style="width:100%"
          />
        </el-form-item><el-form-item label="服务时长（分钟）">
          <el-input-number
            v-model="form.durationMinutes"
            :min="0"
            :max="100000"
            style="width:100%"
          />
        </el-form-item>
      </div><el-form-item
        label="内容"
        required
      >
        <el-input
          v-model="form.content"
          type="textarea"
          :rows="4"
          maxlength="8000"
        />
      </el-form-item>
    </el-form><template #footer>
      <el-button @click="emit('update:modelValue',false)">
        取消
      </el-button><el-button
        type="primary"
        :loading="saving"
        @click="submit"
      >
        保存
      </el-button>
    </template>
  </el-dialog>
</template>
