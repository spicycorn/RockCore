/** dxf-writer (js-dxf) 无官方类型，此处声明本项目用到的最小 API */
declare module 'dxf-writer' {
  class Drawing {
    static readonly ACI: Record<string, number>;
    constructor();
    setUnits(unit: string): this;
    addLayer(name: string, colorNumber: number, lineTypeName?: string): this;
    setActiveLayer(name: string): this;
    drawLine(x1: number, y1: number, x2: number, y2: number): this;
    drawLine3d(x1: number, y1: number, z1: number, x2: number, y2: number, z2: number): this;
    drawFace(
      x1: number, y1: number, z1: number,
      x2: number, y2: number, z2: number,
      x3: number, y3: number, z3: number,
      x4: number, y4: number, z4: number,
    ): this;
    drawPolyline(points: Array<[number, number]>, closed?: boolean, startWidth?: number, endWidth?: number): this;
    drawText(x: number, y: number, height: number, rotation: number, value: string): this;
    toDxfString(): string;
  }
  export = Drawing;
}
