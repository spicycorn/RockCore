/**
 * 钻孔附件对话框：岩心照片等文件 base64 内嵌入库（rockcore.db），
 * 列表 / 添加 / 另存 / 删除；图片双击或右键可放大预览（点击任意处或 Esc 关闭）。
 * 附件只做留档查看，不与数据行绑定。
 */

import { useCallback, useEffect, useState } from 'react';
import { AppDialog, Button, EmptyState, showContextMenu } from './ui';
import {
  addAttachment, deleteAttachment, getAttachment, listAttachments,
  type AttachmentRow, type BoreholeRow,
} from '../db/repos';
import { useApp } from '../store/app';
import { pickAttachmentFile, saveFileAs } from '../lib/dialogs';
import { readFileBytes, writeFileBytes } from '../lib/tauri';

/** 单文件上限 30MB（SQLite 单行 TEXT 足够，但整库文件别膨胀失控） */
const MAX_BYTES = 30 * 1024 * 1024;

const EXT_MIME: Record<string, string> = {
  jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif',
  webp: 'image/webp', bmp: 'image/bmp', pdf: 'application/pdf',
};

const extOf = (name: string): string => name.split('.').pop()?.toLowerCase() ?? '';
const isImage = (mime: string): boolean => mime.startsWith('image/');

function bytesToB64(bytes: Uint8Array): string {
  let bin = '';
  const CHUNK = 0x8000; // 32KB 分块，避免 String.fromCharCode 实参超限
  for (let i = 0; i < bytes.length; i += CHUNK) {
    bin += String.fromCharCode(...bytes.subarray(i, i + CHUNK));
  }
  return btoa(bin);
}

function b64ToBytes(b64: string): Uint8Array {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

function fmtSize(n: number): string {
  return n >= 1048576 ? `${(n / 1048576).toFixed(1)} MB`
    : n >= 1024 ? `${Math.round(n / 1024)} KB` : `${n} B`;
}

interface Preview { name: string; mime: string; dataUrl: string }

export function AttachmentDialog({
  open, borehole, onClose,
}: { open: boolean; borehole: BoreholeRow | null; onClose: () => void }) {
  const db = useApp((s) => s.db);
  const toast = useApp((s) => s.toast);
  const [items, setItems] = useState<AttachmentRow[]>([]);
  const [loading, setLoading] = useState(false);
  const [preview, setPreview] = useState<Preview | null>(null);

  const reload = useCallback(async () => {
    if (db == null || borehole == null) return;
    setLoading(true);
    try {
      setItems(await listAttachments(db, borehole.id));
    } finally {
      setLoading(false);
    }
  }, [db, borehole]);

  useEffect(() => { if (open) void reload(); }, [open, reload]);

  // Esc 关闭预览（预览打开时 Esc 先关预览，再按才关对话框）
  useEffect(() => {
    if (preview == null) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.stopPropagation(); setPreview(null); } };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [preview]);

  async function addOne(): Promise<void> {
    if (db == null || borehole == null) return;
    const path = await pickAttachmentFile();
    if (path == null) return;
    try {
      const buf = await readFileBytes(path);
      if (buf.byteLength === 0) { toast('error', '文件为空，未添加'); return; }
      if (buf.byteLength > MAX_BYTES) {
        toast('error', `文件 ${fmtSize(buf.byteLength)} 超过 30MB 上限，未添加`);
        return;
      }
      const fileName = path.replace(/^.*[\\/]/, '');
      const mime = EXT_MIME[extOf(fileName)] ?? 'application/octet-stream';
      await addAttachment(db, borehole.id, {
        fileName, mime, sizeBytes: buf.byteLength,
        dataB64: bytesToB64(new Uint8Array(buf)),
      });
      toast('success', `已添加附件「${fileName}」`);
      await reload();
    } catch (ex) {
      toast('error', `添加附件失败：${ex instanceof Error ? ex.message : String(ex)}`);
    }
  }

  async function openPreview(a: AttachmentRow): Promise<void> {
    if (db == null) return;
    if (!isImage(a.mime)) { toast('info', '仅支持图片预览；PDF 可右键「另存…」导出后查看'); return; }
    const full = await getAttachment(db, a.id);
    if (full == null) { void reload(); return; }
    setPreview({ name: a.file_name, mime: a.mime, dataUrl: `data:${a.mime};base64,${full.data_b64}` });
  }

  async function saveOne(a: AttachmentRow): Promise<void> {
    if (db == null) return;
    const ext = extOf(a.file_name);
    const path = await saveFileAs(a.file_name, [ext]);
    if (path == null) return;
    const full = await getAttachment(db, a.id);
    if (full == null) { toast('error', '附件已不存在，请刷新'); return; }
    await writeFileBytes(path, b64ToBytes(full.data_b64));
    toast('success', `已导出到 ${path}`);
  }

  function removeOne(a: AttachmentRow): void {
    if (db == null) return;
    if (!window.confirm(`删除附件「${a.file_name}」？删除后不可恢复。`)) return;
    void (async () => {
      await deleteAttachment(db!, a.id);
      toast('success', '附件已删除');
      await reload();
    })();
  }

  const rowMenu = (e: React.MouseEvent, a: AttachmentRow) => {
    e.preventDefault();
    showContextMenu(e.clientX, e.clientY, [
      ...(isImage(a.mime)
        ? [{ label: '预览', action: () => { void openPreview(a); } }]
        : []),
      { label: '另存为…', action: () => { void saveOne(a); } },
      { sep: true },
      { label: '删除', danger: true, action: () => removeOne(a) },
    ]);
  };

  return (
    <AppDialog
      open={open}
      onOpenChange={(o) => { if (!o && preview == null) onClose(); }}
      title={`钻孔 ${borehole?.number ?? ''} 的附件（岩心照片等）`}
      footer={
        <>
          <span className="mr-auto text-xs text-slate-400">
            附件保存在本机数据库内，仅用于留档查看；删除钻孔会一并删除。
          </span>
          <Button variant="primary" onClick={() => void addOne()}>添加附件…</Button>
          <Button onClick={onClose}>关闭</Button>
        </>
      }
    >
      <div className="min-h-40 max-h-80 overflow-auto -mx-1">
        {items.length === 0 ? (
          <EmptyState title="还没有附件" hint="点右下方「添加附件」选择岩心照片（jpg/png/gif/webp/bmp/pdf，≤30MB）" />
        ) : (
          <table className="w-full text-[13px]">
            <thead>
              <tr className="text-left text-xs text-slate-500 border-b border-line">
                <th className="py-1.5 pr-2 font-medium">文件名</th>
                <th className="py-1.5 pr-2 font-medium w-20">大小</th>
                <th className="py-1.5 pr-2 font-medium w-36">添加时间</th>
                <th className="py-1.5 w-24 text-right font-medium">操作</th>
              </tr>
            </thead>
            <tbody>
              {items.map((a) => (
                <tr
                  key={a.id}
                  className="border-b border-line/60 hover:bg-slate-50 cursor-default"
                  onContextMenu={(e) => rowMenu(e, a)}
                  onDoubleClick={() => { void openPreview(a); }}
                  title="双击预览；右键更多操作"
                >
                  <td className="py-1.5 pr-2 text-slate-700 truncate max-w-64">{a.file_name}</td>
                  <td className="py-1.5 pr-2 text-slate-500">{fmtSize(a.size_bytes)}</td>
                  <td className="py-1.5 pr-2 text-slate-500">{a.created_at}</td>
                  <td className="py-1.5 text-right whitespace-nowrap">
                    {isImage(a.mime) && (
                      <button type="button" className="text-primary-600 hover:underline text-xs mr-2"
                        onClick={() => { void openPreview(a); }}>预览</button>
                    )}
                    <button type="button" className="text-red-500 hover:underline text-xs"
                      onClick={() => removeOne(a)}>删除</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {loading && <div className="text-xs text-slate-400 py-2">加载中…</div>}
      </div>

      {preview != null && (
        <div
          className="fixed inset-0 z-[60] bg-black/75 flex flex-col items-center justify-center cursor-zoom-out"
          onClick={() => setPreview(null)}
        >
          <img src={preview.dataUrl} alt={preview.name}
            className="max-h-[85vh] max-w-[90vw] rounded shadow-2xl" />
          <div className="mt-3 text-white/85 text-sm">{preview.name}（点击任意处或 Esc 关闭）</div>
        </div>
      )}
    </AppDialog>
  );
}
