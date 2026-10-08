<script setup lang="ts">
defineProps<{ title: string }>()
</script>

<template>
  <el-empty :description="`${title}当前未启用。`" />
</template>
