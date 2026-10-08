import { ElMessageBox as ElementMessageBox } from 'element-plus'
import { describe, expect, it, vi } from 'vitest'

import { ElMessageBox } from './flowhearth-message-box'

const vueSources = import.meta.glob<string>('../**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
})

describe('FlowHearth dialog conventions', () => {
  it('enables constrained dragging for message boxes', () => {
    const confirm = vi
      .spyOn(ElementMessageBox, 'confirm')
      .mockReturnValue(new Promise<never>(() => undefined))

    ElMessageBox.confirm('确认内容', '确认操作', { type: 'warning' })

    expect(confirm).toHaveBeenCalledWith('确认内容', '确认操作', {
      type: 'warning',
      draggable: true,
      overflow: false,
    })
  })

  it('keeps every standard dialog draggable', () => {
    const dialogTags = Object.values(vueSources).flatMap(
      (source) => source.match(/<el-dialog\b[\s\S]*?>/g) ?? [],
    )

    expect(dialogTags.length).toBeGreaterThan(0)
    expect(dialogTags.every((tag) => /\bdraggable\b/.test(tag))).toBe(true)
  })
})
