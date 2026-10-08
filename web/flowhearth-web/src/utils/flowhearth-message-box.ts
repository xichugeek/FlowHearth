import {
  ElMessageBox as ElementMessageBox,
  type ElMessageBoxOptions,
} from 'element-plus'

type MessageBoxMessage = ElMessageBoxOptions['message']

function withFlowHearthDefaults(
  options: ElMessageBoxOptions = {},
): ElMessageBoxOptions {
  return {
    ...options,
    draggable: true,
    overflow: false,
  }
}

export const ElMessageBox = {
  alert(
    message: MessageBoxMessage,
    title: string,
    options?: ElMessageBoxOptions,
  ) {
    return ElementMessageBox.alert(
      message,
      title,
      withFlowHearthDefaults(options),
    )
  },
  confirm(
    message: MessageBoxMessage,
    title: string,
    options?: ElMessageBoxOptions,
  ) {
    return ElementMessageBox.confirm(
      message,
      title,
      withFlowHearthDefaults(options),
    )
  },
  prompt(
    message: MessageBoxMessage,
    title: string,
    options?: ElMessageBoxOptions,
  ) {
    return ElementMessageBox.prompt(
      message,
      title,
      withFlowHearthDefaults(options),
    )
  },
  close() {
    ElementMessageBox.close()
  },
}
