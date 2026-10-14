/**
 * Tauri 命令封装：Rust 侧只有字节读写与内嵌模板三个命令，全部业务在 TS。
 */

import { invoke } from '@tauri-apps/api/core';

type Bytes = Uint8Array | number[];

function toBytes(data: Bytes): Uint8Array {
  return data instanceof Uint8Array ? data : Uint8Array.from(data);
}

/** 读任意本地文件字节（导入 Excel 用） */
export async function readFileBytes(path: string): Promise<ArrayBuffer> {
  const bytes = toBytes(await invoke<Bytes>('read_file_bytes', { path }));
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

/** 写字节到任意本地路径（导出报表用）；Uint8Array 走 Tauri IPC 原始字节通道，不逐元素装箱 */
export function writeFileBytes(path: string, bytes: Uint8Array): Promise<void> {
  return invoke<void>('write_file_bytes', { path, bytes });
}

/** 读取随安装包内嵌的导入模板字节 */
export async function bundledTemplate(): Promise<ArrayBuffer> {
  const bytes = toBytes(await invoke<Bytes>('bundled_template'));
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
}

/** 当前是否运行在 Tauri 桌面壳内（开发期浏览器里为 false） */
export function isTauri(): boolean {
  return '__TAURI_INTERNALS__' in window;
}
