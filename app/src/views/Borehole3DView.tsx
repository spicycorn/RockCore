/**
 * 三维钻孔模型（v1.1.2 起替代 2D 柱状剖面图）：
 * Three.js 矢量场景，多孔按孔口坐标空间排布，可聚焦单孔；
 * 色带 = 围岩类别，灰色半透明 = 未记录段（绝不插值伪造）；
 * 导出 PNG 与 DXF R2007（3D 模型 + 2D 剖面同文件分图层）。
 * 平面布置图模式保留（复用 buildPlanOption）。
 */

import { useEffect, useMemo, useRef, useState } from 'react';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { useApp } from '../store/app';
import { finalIntegrityLevel, listSegments, type BoreholeRow, type SegmentRow } from '../db/repos';
import { integrityLevelLabel, RockClass, rockClassNumeral, rockClassText } from '../engine/enums';
import { inclinationToZenith, buildDepthBands } from '../engine/boreholeGeometry';
import { buildDxf, type DxfBorehole, type DxfSegment } from '../engine/dxfExport';
import { rockClassColor } from '../lib/colors';
import { useChart, dataUrlToBytes } from '../lib/chart';
import { saveFileAs } from '../lib/dialogs';
import { writeFileBytes } from '../lib/tauri';
import { Button, EmptyState } from '../components/ui';
import { buildPlanOption } from './PlanView';

const GAP_COLOR = '#9ca3af';
const SCENE_SIZE = 200;

interface Band {
  d0: number;
  d1: number;
  color: string;
  gap: boolean;
  seg: SegmentRow | null;
}

interface HoleModel {
  b: BoreholeRow;
  zenith: number;
  azimuth: number;
  total: number;
  bands: Band[];
  /** 场景坐标（已中心化缩放） */
  collar: THREE.Vector3;
  /** 场景单位 / 米 */
  scale: number;
}

function segLayer(s: SegmentRow): string {
  if (s.rock_class == null) return 'SEG_UNK';
  return `SEG_${(['I', 'II', 'III', 'IV', 'V'] as const)[s.rock_class - 1] ?? 'UNK'}`;
}

/** 递归释放几何体/材质/精灵纹理（重建与卸载时防泄漏） */
function disposeObject(root: THREE.Object3D): void {
  root.traverse((x) => {
    const m = x as THREE.Mesh;
    m.geometry?.dispose?.();
    const mat = m.material as THREE.Material | THREE.Material[] | undefined;
    for (const mm of Array.isArray(mat) ? mat : mat != null ? [mat] : []) {
      (mm as THREE.SpriteMaterial).map?.dispose?.();
      mm.dispose();
    }
  });
}

function bandTooltip(h: BoreholeRow, band: Band): string[] {
  const s = band.seg;
  if (s == null) {
    return [`${h.number}　${band.d0.toFixed(1)} – ${band.d1.toFixed(1)} m`, '未记录段（无回次数据）'];
  }
  const cls = s.rock_class == null ? '未分类' : rockClassText(s.rock_class as RockClass);
  return [
    `${h.number}　段 ${s.seq}　${s.depth_start.toFixed(1)} – ${s.depth_end.toFixed(1)} m`,
    `RQD=${s.rqd.toFixed(1)}%　完整性：${integrityLevelLabel[finalIntegrityLevel(s)]}`,
    `围岩类别：${cls}`,
  ];
}

export function Borehole3DView() {
  const db = useApp((s) => s.db);
  const boreholes = useApp((s) => s.boreholes);
  const boreholeId = useApp((s) => s.boreholeId);
  const segments = useApp((s) => s.segments);
  const project = useApp((s) => s.projects.find((p) => p.id === s.projectId) ?? null);
  const selectBorehole = useApp((s) => s.selectBorehole);
  const toast = useApp((s) => s.toast);

  const [mode, setMode] = useState<'model' | 'plan'>('model');
  const [focusId, setFocusId] = useState<number | 'all'>('all');
  const [hover, setHover] = useState<{ x: number; y: number; lines: string[] } | null>(null);
  const [allSegs, setAllSegs] = useState<Record<number, SegmentRow[]>>({});

  // 当前孔的段以 store 为准（编辑后实时刷新），其余孔按需加载
  useEffect(() => {
    if (db == null) return;
    let cancelled = false;
    void (async () => {
      const entries = await Promise.all(
        boreholes.filter((b) => b.id !== boreholeId).map(async (b) => [b.id, await listSegments(db, b.id)] as const),
      );
      if (!cancelled) setAllSegs(Object.fromEntries(entries));
    })();
    return () => { cancelled = true; };
  }, [db, boreholes, boreholeId]);

  const models = useMemo<HoleModel[]>(() => {
    const raw = boreholes.map((b) => {
      const segs = (b.id === boreholeId ? segments : allSegs[b.id] ?? []).slice();
      const total = b.total_depth ?? (segs.length > 0 ? Math.max(...segs.map((s) => s.depth_end)) : 0);
      const bands: Band[] = buildDepthBands(segs, total).map((x) => ({
        d0: x.d0,
        d1: x.d1,
        color: x.seg == null ? GAP_COLOR : rockClassColor(x.seg.rock_class == null ? undefined : (x.seg.rock_class as RockClass)),
        gap: x.seg == null,
        seg: x.seg,
      }));
      return {
        b,
        zenith: b.inclination_angle == null ? 0 : inclinationToZenith(b.inclination_angle),
        azimuth: b.azimuth ?? 0,
        total,
        bands,
      };
    });

    // 孔口坐标：全缺时沿 X 轴排开（合成布局）
    const hasCoord = raw.some(({ b }) => b.orifice_x != null && b.orifice_y != null);
    const pts = raw.map(({ b }, i) => {
      const ex = hasCoord ? (b.orifice_x ?? 0) : i * 25;
      const ey = hasCoord ? (b.orifice_y ?? 0) : 0;
      const ez = b.orifice_elevation ?? 0;
      return { ex, ey, ez };
    });

    // 孔底点（东/北/高程）
    const bottomOf = (m: { zenith: number; azimuth: number; total: number }, i: number) => {
      const z = m.zenith * (Math.PI / 180), a = m.azimuth * (Math.PI / 180);
      const d = m.total;
      return {
        x: pts[i].ex + d * Math.sin(z) * Math.sin(a),
        y: pts[i].ey + d * Math.sin(z) * Math.cos(a),
        z: pts[i].ez - d * Math.cos(z),
      };
    };
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity, minZ = Infinity, maxZ = -Infinity;
    raw.forEach((m, i) => {
      for (const p of [pts[i], bottomOf(m, i)]) {
        minX = Math.min(minX, p.x); maxX = Math.max(maxX, p.x);
        minY = Math.min(minY, p.y); maxY = Math.max(maxY, p.y);
        minZ = Math.min(minZ, p.z); maxZ = Math.max(maxZ, p.z);
      }
    });
    if (!Number.isFinite(minX)) { minX = maxX = minY = maxY = minZ = maxZ = 0; }
    const cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, cz = (minZ + maxZ) / 2;
    const size = Math.max(maxX - minX, maxY - minY, maxZ - minZ, 10);
    const s = SCENE_SIZE / size;
    const toScene = (x: number, y: number, z: number) => new THREE.Vector3((x - cx) * s, (z - cz) * s, -(y - cy) * s);

    return raw.map((m, i) => ({ ...m, collar: toScene(pts[i].ex, pts[i].ey, pts[i].ez), scale: s }));
  }, [boreholes, boreholeId, segments, allSegs, project]);

  const selectRef = useRef(selectBorehole);
  selectRef.current = selectBorehole;

  const containerRef = useRef<HTMLDivElement | null>(null);
  const rendererRef = useRef<THREE.WebGLRenderer | null>(null);
  const contentRef = useRef<THREE.Group | null>(null);
  const holeGroupsRef = useRef<Map<number, THREE.Group>>(new Map());
  const cameraRef = useRef<THREE.PerspectiveCamera | null>(null);
  const controlsRef = useRef<OrbitControls | null>(null);
  const fitKeyRef = useRef('');

  // —— 渲染器 / 相机 / 控制器（挂载一次） ——
  useEffect(() => {
    const el = containerRef.current;
    if (el == null) return;
    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: true });
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    el.appendChild(renderer.domElement);
    rendererRef.current = renderer;

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 5000);
    camera.position.set(SCENE_SIZE * 1.1, SCENE_SIZE * 0.7, SCENE_SIZE * 1.3);
    cameraRef.current = camera;
    const controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controlsRef.current = controls;

    scene.add(new THREE.AmbientLight(0xffffff, 0.85));
    const dir = new THREE.DirectionalLight(0xffffff, 1.2);
    dir.position.set(1, 2, 1.5);
    scene.add(dir);

    const content = new THREE.Group();
    scene.add(content);
    contentRef.current = content;

    const raycaster = new THREE.Raycaster();
    const ndc = new THREE.Vector2();
    const pick = (ev: PointerEvent | MouseEvent): { hole: HoleModel; band: Band } | null => {
      const rect = renderer.domElement.getBoundingClientRect();
      ndc.set(((ev.clientX - rect.left) / rect.width) * 2 - 1, -((ev.clientY - rect.top) / rect.height) * 2 + 1);
      raycaster.setFromCamera(ndc, camera);
      const hit = raycaster.intersectObjects(content.children, true).find((h) => h.object.userData.band != null);
      if (hit == null) return null;
      const ud = hit.object.userData as { hole: HoleModel; band: Band };
      return { hole: ud.hole, band: ud.band };
    };
    const onMove = (ev: PointerEvent) => {
      const p = pick(ev);
      if (p == null) { setHover(null); renderer.domElement.style.cursor = 'default'; return; }
      renderer.domElement.style.cursor = 'pointer';
      const rect = el.getBoundingClientRect();
      setHover({ x: ev.clientX - rect.left + 12, y: ev.clientY - rect.top + 12, lines: bandTooltip(p.hole.b, p.band) });
    };
    const onLeave = () => setHover(null);
    const onClick = (ev: MouseEvent) => {
      const p = pick(ev);
      if (p != null) void selectRef.current(p.hole.b.id);
    };
    renderer.domElement.addEventListener('pointermove', onMove);
    renderer.domElement.addEventListener('pointerleave', onLeave);
    renderer.domElement.addEventListener('click', onClick);

    let raf = 0;
    const loop = () => {
      controls.update();
      renderer.render(scene, camera);
      raf = requestAnimationFrame(loop);
    };
    loop();

    const ro = new ResizeObserver(() => {
      const w = el.clientWidth, h = el.clientHeight;
      if (w > 0 && h > 0) {
        renderer.setSize(w, h);
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
      }
    });
    ro.observe(el);

    return () => {
      cancelAnimationFrame(raf);
      ro.disconnect();
      renderer.domElement.removeEventListener('pointermove', onMove);
      renderer.domElement.removeEventListener('pointerleave', onLeave);
      renderer.domElement.removeEventListener('click', onClick);
      controls.dispose();
      if (contentRef.current != null) disposeObject(contentRef.current);
      renderer.dispose();
      el.removeChild(renderer.domElement);
      rendererRef.current = null;
      contentRef.current = null;
    };
  }, []);

  // —— 场景内容（数据变化时重建） ——
  useEffect(() => {
    const content = contentRef.current, camera = cameraRef.current, controls = controlsRef.current;
    if (content == null || camera == null || controls == null) return;

    for (const child of content.children) disposeObject(child);
    content.clear();
    holeGroupsRef.current.clear();

    const grid = new THREE.GridHelper(SCENE_SIZE * 1.3, 26, 0xcbd5e1, 0xe8edf3);
    grid.position.y = SCENE_SIZE / 2 + 4;
    content.add(grid);

    const radius = Math.max(1.6, SCENE_SIZE * 0.008);
    /** 孔轴场景方向单位向量（场景系 x=东、y=上、z=−北；天顶角 0=竖直向下） */
    const dirOf = (m: HoleModel) => {
      const z = m.zenith * (Math.PI / 180), a = m.azimuth * (Math.PI / 180);
      return new THREE.Vector3(Math.sin(z) * Math.sin(a), -Math.cos(z), -Math.sin(z) * Math.cos(a)).normalize();
    };
    /** 沿孔轴深度 d（米）的场景坐标 */
    const at = (m: HoleModel, d: number) => m.collar.clone().addScaledVector(dirOf(m), d * m.scale);
    for (const m of models) {
      const g = new THREE.Group();
      g.userData.holeId = m.b.id;
      holeGroupsRef.current.set(m.b.id, g);
      content.add(g);
      if (m.total <= 0) continue;

      const bottom = at(m, m.total);
      const scale = m.scale; // 场景单位 / m
      const axis = bottom.clone().sub(m.collar).normalize();

      // 孔轴线
      const axisGeo = new THREE.BufferGeometry().setFromPoints([m.collar, bottom]);
      g.add(new THREE.Line(axisGeo, new THREE.LineBasicMaterial({ color: 0x334155 })));

      // 色带
      const q = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), axis);
      for (const band of m.bands) {
        const len = (band.d1 - band.d0) * scale;
        if (len <= 1e-4) continue;
        const geo = new THREE.CylinderGeometry(radius, radius, len, 10, 1, false);
        const mat = new THREE.MeshLambertMaterial({
          color: new THREE.Color(band.color),
          transparent: band.gap,
          opacity: band.gap ? 0.35 : 1,
        });
        const mesh = new THREE.Mesh(geo, mat);
        mesh.quaternion.copy(q);
        mesh.position.copy(m.collar.clone().addScaledVector(axis, (band.d0 + band.d1) / 2 * scale));
        mesh.userData = { hole: m, band };
        g.add(mesh);
      }

      // 水位标记
      const gw = m.b.groundwater_depth;
      if (gw != null && gw > 0 && gw <= m.total) {
        const p = m.collar.clone().addScaledVector(axis, gw * scale);
        const marker = new THREE.Mesh(
          new THREE.SphereGeometry(radius * 1.8, 12, 12),
          new THREE.MeshLambertMaterial({ color: 0x2563eb }),
        );
        marker.position.copy(p);
        g.add(marker);
      }

      // 孔号标签（canvas 精灵）
      const canvas = document.createElement('canvas');
      canvas.width = 256; canvas.height = 64;
      const ctx = canvas.getContext('2d')!;
      ctx.font = 'bold 40px sans-serif';
      ctx.fillStyle = '#334155';
      ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      ctx.fillText(m.b.number, 128, 32);
      const tex = new THREE.CanvasTexture(canvas);
      const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false }));
      sprite.scale.set(28, 7, 1);
      sprite.position.copy(m.collar).add(new THREE.Vector3(0, radius * 4, 0));
      g.add(sprite);
    }

    // 洞轴线（过孔口质心，水平，青色）
    const caveAz = project?.cave_axis_azimuth;
    if (caveAz != null && models.length > 0) {
      const c = new THREE.Vector3();
      for (const m of models) c.add(m.collar);
      c.multiplyScalar(1 / models.length);
      const a = caveAz * (Math.PI / 180);
      const dirV = new THREE.Vector3(Math.sin(a), 0, -Math.cos(a)).normalize();
      const L = SCENE_SIZE * 1.4;
      const geo = new THREE.BufferGeometry().setFromPoints([
        c.clone().addScaledVector(dirV, -L / 2), c.clone().addScaledVector(dirV, L / 2),
      ]);
      content.add(new THREE.Line(geo, new THREE.LineBasicMaterial({ color: 0x06b6d4 })));
    }

    controls.target.set(0, 0, 0);
    // 仅在孔集合变化时复位视角，编辑段数据不打乱当前观察角度
    const fitKey = models.map((m) => m.b.id).join(',');
    if (fitKeyRef.current !== fitKey) {
      fitKeyRef.current = fitKey;
      camera.position.set(SCENE_SIZE * 1.1, SCENE_SIZE * 0.7, SCENE_SIZE * 1.3);
      camera.lookAt(0, 0, 0);
    }
    controls.update();
  }, [models, project]);

  // 聚焦切换（不重建场景）
  useEffect(() => {
    for (const [id, g] of holeGroupsRef.current) g.visible = focusId === 'all' || id === focusId;
  }, [focusId, models]);

  // 平面布置图模式
  const planOption = useMemo(
    () => (mode === 'plan' ? buildPlanOption(boreholes, project?.cave_axis_azimuth ?? null, boreholeId) : null),
    [mode, boreholes, project, boreholeId],
  );
  const { ref: planRef, getChart } = useChart<HTMLDivElement>(planOption);

  const exportPng = async () => {
    let bytes: Uint8Array | null = null;
    if (mode === 'plan') {
      const chart = getChart();
      if (chart == null) return;
      bytes = dataUrlToBytes(chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#ffffff' }));
    } else {
      const renderer = rendererRef.current;
      if (renderer == null) return;
      // preserveDrawingBuffer: true —— 直接取当前帧
      bytes = dataUrlToBytes(renderer.domElement.toDataURL('image/png'));
    }
    if (bytes == null) return;
    const path = await saveFileAs(`三维模型_${mode === 'plan' ? '平面' : boreholes.length > 1 ? '多孔' : boreholes[0]?.number ?? ''}.png`, ['png']);
    if (path == null) return;
    await writeFileBytes(path, bytes);
    toast('success', `已导出 ${path}`);
  };

  const exportDxf = async () => {
    const dxfHoles: DxfBorehole[] = models.map((m) => ({
      number: m.b.number,
      orificeX: m.b.orifice_x,
      orificeY: m.b.orifice_y,
      orificeElevation: m.b.orifice_elevation,
      azimuth: m.b.azimuth,
      zenith: m.zenith,
      totalDepth: m.total > 0 ? m.total : null,
      segments: m.bands.map<DxfSegment>((band) => ({
        depthStart: band.d0,
        depthEnd: band.d1,
        colorHex: band.color,
        layer: band.gap ? 'GAP' : band.seg != null ? segLayer(band.seg) : 'SEG_UNK',
        label: band.gap ? '未记录' : band.seg?.rock_class == null ? '未分类' : `${rockClassNumeral(band.seg.rock_class as RockClass)}类`,
        gap: band.gap,
      })),
    }));
    const dxf = buildDxf(dxfHoles, { caveAxisAzimuth: project?.cave_axis_azimuth ?? null });
    const path = await saveFileAs(`钻孔模型_${project?.name ?? '项目'}.dxf`, ['dxf']);
    if (path == null) return;
    await writeFileBytes(path, new TextEncoder().encode(dxf));
    toast('success', `已导出 ${path}（DXF R2007，含 3D 模型 + 2D 剖面）`);
  };

  return (
    <div className="h-full flex flex-col">
      <div className="flex items-center gap-2 px-3 py-2 border-b border-line bg-surface flex-wrap">
        <Button variant={mode === 'model' ? 'primary' : 'default'} onClick={() => setMode('model')}>三维模型</Button>
        <Button variant={mode === 'plan' ? 'primary' : 'default'} onClick={() => setMode('plan')}>平面布置图</Button>
        {mode === 'model' && (
          <select
            className="h-7 rounded border border-line bg-white px-1.5 text-xs text-slate-700"
            value={focusId === 'all' ? 'all' : String(focusId)}
            onChange={(e) => setFocusId(e.target.value === 'all' ? 'all' : Number(e.target.value))}
          >
            <option value="all">全部钻孔（{boreholes.length}）</option>
            {boreholes.map((b) => <option key={b.id} value={b.id}>聚焦 {b.number}</option>)}
          </select>
        )}
        <span className="mx-1 h-5 w-px bg-line" />
        <span className="flex items-center gap-1.5 text-xs text-slate-500">
          {([RockClass.I, RockClass.II, RockClass.III, RockClass.IV, RockClass.V] as RockClass[]).map((c) => (
            <span key={c} className="inline-flex items-center gap-0.5">
              <i className="inline-block h-3 w-3 rounded-sm" style={{ background: rockClassColor(c) }} />
              {rockClassNumeral(c)}
            </span>
          ))}
          <span className="inline-flex items-center gap-0.5"><i className="inline-block h-3 w-3 rounded-sm opacity-50" style={{ background: GAP_COLOR }} />未记录</span>
          <span className="inline-flex items-center gap-0.5"><i className="inline-block h-3 w-3 rounded-full bg-[#2563eb]" />水位</span>
          {project?.cave_axis_azimuth != null && <span className="inline-flex items-center gap-0.5"><i className="inline-block h-0.5 w-4 bg-[#06b6d4]" />洞轴线</span>}
        </span>
        <span className="ml-auto flex items-center gap-2">
          <Button onClick={() => void exportPng()}>导出 PNG</Button>
          <Button variant="primary" onClick={() => void exportDxf()}>导出 DXF…</Button>
        </span>
      </div>
      <div className="flex-1 min-h-0 relative bg-surface">
        <div ref={containerRef} className={`w-full h-full ${mode === 'model' ? '' : 'hidden'}`} />
        <div ref={planRef} className={`w-full h-full ${mode === 'plan' ? '' : 'hidden'}`} />
        {boreholes.length === 0 && (
          <div className="absolute inset-0 bg-surface z-10"><EmptyState title="三维钻孔模型" hint="请先新建项目与钻孔，或导入 Excel 数据。" /></div>
        )}
        {mode === 'plan' && planOption == null && (
          <div className="absolute inset-0 bg-surface"><EmptyState title="暂无孔口坐标" hint="在钻孔属性里填写 X/Y 坐标后即可查看平面布置图。" /></div>
        )}
        {hover != null && mode === 'model' && (
          <div
            className="absolute z-10 pointer-events-none rounded border border-line bg-white/95 px-2.5 py-1.5 text-[11px] leading-5 text-slate-700 shadow-md"
            style={{ left: hover.x, top: hover.y }}
          >
            {hover.lines.map((l, i) => <div key={i} className={i === 0 ? 'font-semibold text-slate-800' : ''}>{l}</div>)}
          </div>
        )}
        {mode === 'model' && (
          <div className="absolute bottom-2 left-3 text-[11px] text-slate-400 pointer-events-none">
            拖动旋转 · 滚轮缩放 · 右键平移 · 悬停查看分段 · 点击选中钻孔
          </div>
        )}
      </div>
    </div>
  );
}
