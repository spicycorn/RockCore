/**
 * 配色（沿用 1.x ColorMapper，围岩五色带是行业习惯色）。
 */

import { IntegrityLevel, RockClass } from '../engine/enums';

export function rgb(r: number, g: number, b: number): string {
  return `rgb(${r},${g},${b})`;
}

const rockClassColors: Record<RockClass, string> = {
  [RockClass.I]: rgb(70, 130, 180),
  [RockClass.II]: rgb(60, 179, 113),
  [RockClass.III]: rgb(238, 238, 0),
  [RockClass.IV]: rgb(255, 165, 0),
  [RockClass.V]: rgb(220, 20, 60),
};

const integrityColors: Partial<Record<IntegrityLevel, string>> = {
  [IntegrityLevel.Intact]: rgb(34, 139, 34),
  [IntegrityLevel.RelativelyIntact]: rgb(60, 179, 113),
  [IntegrityLevel.Poor]: rgb(144, 238, 144),
  [IntegrityLevel.RelativelyBroken]: rgb(255, 200, 0),
  [IntegrityLevel.Broken]: rgb(255, 100, 0),
};

export function rockClassColor(c: RockClass | null | undefined): string {
  return c == null ? '#9ca3af' : rockClassColors[c];
}

export function integrityColor(level: IntegrityLevel): string {
  return integrityColors[level] ?? '#9ca3af';
}

/** Ⅲ 类黄底上的可读文字色 */
export function textOn(bg: string): string {
  return bg === rgb(238, 238, 0) || bg === rgb(255, 200, 0) || bg === rgb(144, 238, 144)
    ? '#1f2937'
    : '#ffffff';
}
