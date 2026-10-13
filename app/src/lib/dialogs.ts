/**
 * 系统对话框封装（打开/另存为）。
 */

import { open, save } from '@tauri-apps/plugin-dialog';

/** 选择要导入的 xlsx，返回路径或 null */
export function pickExcelFile(): Promise<string | null> {
  return open({
    multiple: false,
    filters: [{ name: 'Excel 工作簿', extensions: ['xlsx'] }],
  });
}

/** 选择要附加到钻孔的文件（岩心照片等），返回路径或 null */
export function pickAttachmentFile(): Promise<string | null> {
  return open({
    multiple: false,
    filters: [{
      name: '岩心照片 / 附件',
      extensions: ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'pdf'],
    }],
  });
}

/** 另存为对话框，返回路径或 null */
export function saveFileAs(defaultName: string, extensions: string[]): Promise<string | null> {
  return save({
    defaultPath: defaultName,
    filters: [{ name: extensions.join('/').toUpperCase(), extensions }],
  });
}
