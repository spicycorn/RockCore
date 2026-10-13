/**
 * 轻量 UI 套件：按钮/输入/下拉/对话框/右键菜单/标签页/提示条。
 * 统一浅色工程风；所有可交互元素都有 title 提示（悬停即说明）。
 */

import * as Dialog from '@radix-ui/react-dialog';
import clsx from 'clsx';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useApp } from '../store/app';

// ---------------------------------------------------------------------------
// 按钮
// ---------------------------------------------------------------------------

type ButtonVariant = 'primary' | 'default' | 'ghost' | 'danger';

const buttonBase =
  'inline-flex items-center gap-1.5 rounded px-2.5 h-7 text-[13px] leading-none ' +
  'disabled:opacity-45 disabled:pointer-events-none transition-colors whitespace-nowrap';

const buttonVariants: Record<ButtonVariant, string> = {
  primary: 'bg-primary-500 text-white hover:bg-primary-600 active:bg-primary-700',
  default: 'bg-surface border border-line text-slate-700 hover:bg-primary-50 hover:border-primary-300',
  ghost: 'text-slate-600 hover:bg-primary-50 hover:text-primary-600',
  danger: 'bg-red-600 text-white hover:bg-red-700',
};

export function Button({
  variant = 'default', className, ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant }) {
  return <button type="button" className={clsx(buttonBase, buttonVariants[variant], className)} {...props} />;
}

// ---------------------------------------------------------------------------
// 输入
// ---------------------------------------------------------------------------

export function TextInput({ className, ...props }: React.InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      type="text"
      className={clsx(
        'h-7 rounded border border-line bg-surface px-2 text-[13px] outline-none',
        'focus:border-primary-400 placeholder:text-slate-400',
        className,
      )}
      {...props}
    />
  );
}

export function Select<T extends string | number>({
  className, options, ...props
}: React.SelectHTMLAttributes<HTMLSelectElement> & { options: { value: T; label: string }[] }) {
  return (
    <select
      className={clsx(
        'h-7 rounded border border-line bg-surface px-1.5 text-[13px] outline-none',
        'focus:border-primary-400',
        className,
      )}
      {...props}
    >
      {options.map((o) => (
        <option key={String(o.value)} value={String(o.value)}>{o.label}</option>
      ))}
    </select>
  );
}

export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return (
    <label className="flex flex-col gap-1" title={hint}>
      <span className="text-xs text-slate-500">{label}</span>
      {children}
    </label>
  );
}

// ---------------------------------------------------------------------------
// 对话框
// ---------------------------------------------------------------------------

export function AppDialog({
  open, onOpenChange, title, children, footer, wide,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 bg-slate-900/35 data-[state=open]:animate-in" />
        <Dialog.Content
          className={clsx(
            'fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rounded-lg bg-surface shadow-2xl',
            'border border-line flex flex-col max-h-[86vh]',
            wide ? 'w-[min(1080px,94vw)]' : 'w-[min(520px,94vw)]',
          )}
        >
          <Dialog.Title className="px-4 h-11 flex items-center border-b border-line text-[15px] font-semibold text-slate-800 shrink-0">
            {title}
          </Dialog.Title>
          <div className="p-4 overflow-y-auto min-h-0">{children}</div>
          {footer != null && (
            <div className="px-4 h-12 flex items-center justify-end gap-2 border-t border-line shrink-0">{footer}</div>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

// ---------------------------------------------------------------------------
// 右键菜单（全局单例：任何视图 showContextMenu(x, y, items)）
// ---------------------------------------------------------------------------

export interface MenuItem {
  label?: string;
  action?: () => void;
  sep?: boolean;
  disabled?: boolean;
  danger?: boolean;
  hint?: string;
}

type MenuState = { x: number; y: number; items: MenuItem[] } | null;
let menuSetter: ((s: MenuState) => void) | null = null;

export function showContextMenu(x: number, y: number, items: MenuItem[]): void {
  menuSetter?.({ x, y, items });
}

export function ContextMenuHost() {
  const [state, setState] = useState<MenuState>(null);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => { menuSetter = setState; return () => { menuSetter = null; }; }, []);
  useEffect(() => {
    if (state == null) return;
    const close = () => setState(null);
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') close(); };
    window.addEventListener('pointerdown', (e) => {
      if (ref.current != null && !ref.current.contains(e.target as Node)) close();
    });
    window.addEventListener('keydown', onKey);
    window.addEventListener('blur', close);
    return () => { window.removeEventListener('keydown', onKey); window.removeEventListener('blur', close); };
  }, [state]);
  if (state == null) return null;
  // 防溢出：贴近视口边缘时回拉
  const width = 200;
  const estH = state.items.length * 28 + 8;
  const x = Math.min(state.x, window.innerWidth - width - 4);
  const y = Math.min(state.y, window.innerHeight - estH - 4);
  return createPortal(
    <div
      ref={ref}
      style={{ left: x, top: y, width }}
      className="fixed z-[999] rounded-md border border-line bg-surface py-1 shadow-xl"
    >
      {state.items.map((item, i) =>
        item.sep ? (
          <div key={i} className="my-1 border-t border-line" />
        ) : (
          <button
            key={i}
            type="button"
            disabled={item.disabled}
            title={item.hint}
            className={clsx(
              'block w-full px-3 h-7 text-left text-[13px] leading-7 disabled:opacity-40',
              item.danger ? 'text-red-600 hover:bg-red-50' : 'text-slate-700 hover:bg-primary-50',
            )}
            onClick={() => { setState(null); item.action?.(); }}
          >
            {item.label}
          </button>
        ),
      )}
    </div>,
    document.body,
  );
}

// ---------------------------------------------------------------------------
// 标签页
// ---------------------------------------------------------------------------

export function Tabs<T extends string>({
  items, active, onChange,
}: { items: { key: T; label: string; disabled?: boolean }[]; active: T; onChange: (k: T) => void }) {
  return (
    <div className="flex items-end gap-0.5 px-2 pt-1.5 bg-surface-alt border-b border-line shrink-0">
      {items.map((it) => (
        <button
          key={it.key}
          type="button"
          disabled={it.disabled}
          onClick={() => onChange(it.key)}
          className={clsx(
            'px-3.5 h-8 rounded-t text-[13px] border border-b-0 transition-colors disabled:opacity-40',
            active === it.key
              ? 'bg-surface border-line text-primary-600 font-medium -mb-px z-10'
              : 'bg-transparent border-transparent text-slate-500 hover:text-slate-700 hover:bg-slate-200/50',
          )}
        >
          {it.label}
        </button>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------------------
// 徽标 / 空态 / 加载
// ---------------------------------------------------------------------------

export function Badge({ color, children }: { color: string; children: ReactNode }) {
  return (
    <span
      className="inline-flex items-center rounded px-1.5 h-[18px] text-[11px] font-medium text-white"
      style={{ backgroundColor: color }}
    >
      {children}
    </span>
  );
}

export function EmptyState({ title, hint, children }: { title: string; hint?: string; children?: ReactNode }) {
  return (
    <div className="h-full flex flex-col items-center justify-center gap-2 text-center p-8">
      <div className="size-12 rounded-full bg-primary-100 flex items-center justify-center text-primary-500 text-xl">⛰</div>
      <div className="text-slate-600 font-medium">{title}</div>
      {hint != null && <div className="text-xs text-slate-400 max-w-md leading-5">{hint}</div>}
      {children}
    </div>
  );
}

// ---------------------------------------------------------------------------
// 提示条（Toast）
// ---------------------------------------------------------------------------

const toastStyles: Record<string, string> = {
  info: 'border-primary-200 bg-primary-50 text-primary-700',
  success: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  warn: 'border-amber-300 bg-amber-50 text-amber-800',
  error: 'border-red-300 bg-red-50 text-red-700',
};

export function ToastHost() {
  const toasts = useApp((s) => s.toasts);
  const dismiss = useApp((s) => s.dismissToast);
  if (toasts.length === 0) return null;
  return createPortal(
    <div className="fixed bottom-4 right-4 z-[998] flex flex-col gap-2 w-[420px]">
      {toasts.map((t) => (
        <div
          key={t.id}
          className={clsx('rounded-md border px-3 py-2 text-[13px] shadow-lg cursor-pointer leading-5', toastStyles[t.kind])}
          onClick={() => dismiss(t.id)}
          title="点击关闭"
        >
          {t.text}
        </div>
      ))}
    </div>,
    document.body,
  );
}
