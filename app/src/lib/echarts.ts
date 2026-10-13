/**
 * ECharts 按需引入（只装用到的组件，控制包体）。
 */

import * as echarts from 'echarts/core';
import { BarChart, CustomChart, PieChart, ScatterChart } from 'echarts/charts';
import { GridComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';

echarts.use([BarChart, CustomChart, PieChart, ScatterChart, GridComponent, TooltipComponent, CanvasRenderer]);

export { echarts };
