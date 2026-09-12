using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Wpf.ThreeDim;

public class SceneBuilder
{
    private readonly double _coreRadius;
    private readonly double _gridOpacity;
    private readonly double _gridSpacing;
    private readonly bool _showGroundGridOnly;

    public SceneBuilder(double coreRadius = 0.5, double gridOpacity = 0.22, double gridSpacing = 0, bool showGroundGridOnly = false)
    {
        _coreRadius = coreRadius;
        _gridOpacity = gridOpacity;
        _gridSpacing = gridSpacing;
        _showGroundGridOnly = showGroundGridOnly;
    }

    private static readonly Color AxisXColor = Color.FromRgb(220, 60, 60);
    private static readonly Color AxisYColor = Color.FromRgb(60, 180, 60);
    private static readonly Color AxisZColor = Color.FromRgb(60, 120, 220);
    private static readonly Color TickColor = Color.FromRgb(80, 80, 80);
    private static readonly Color GroundwaterColor = Color.FromRgb(30, 144, 255);
    private static readonly Color OriginColor = Color.FromRgb(100, 100, 100);
    private static readonly Color GroundGridColor = Color.FromRgb(100, 120, 140);
    private static readonly Color WallGridColor = Color.FromRgb(140, 150, 165);

    public ModelVisual3D BuildScene(
        Borehole borehole,
        IEnumerable<ClassificationSegment> classificationSegments,
        IEnumerable<StructuralPlane> structuralPlanes,
        ColorScheme colorScheme,
        double? tickInterval = null,
        bool showGroundwater = true,
        bool showDepthLabels = true)
    {
        var root = new ModelVisual3D();

        if (borehole == null) return root;

        var segments = classificationSegments.OrderBy(s => s.DepthStart).ToList();
        if (segments.Count == 0) return root;

        double displayDepth = segments.Max(s => s.DepthEnd);
        double actualTickInterval = tickInterval ?? CalculateTickInterval(displayDepth);
        double gridSpace = _gridSpacing > 0 ? _gridSpacing : CalculateTickInterval(displayDepth);
        double sceneExtent = Math.Max(30, displayDepth * 1.4);

        root.Children.Add(CreateLights());
        root.Children.Add(CreateWorldAxes(sceneExtent, borehole));
        root.Children.Add(Create3DGridSpace(sceneExtent, displayDepth, gridSpace));
        root.Children.Add(CreateOriginMark(borehole));
        root.Children.Add(CreateNorthArrow(sceneExtent));
        root.Children.Add(CreateGroundGridLabels(sceneExtent, gridSpace, borehole));

        var boreholeGroup = new ModelVisual3D();
        double currentY = 0;

        foreach (var segment in segments)
        {
            double depthLen = segment.DepthEnd - segment.DepthStart;
            if (depthLen <= 0) continue;

            var color = GetSegmentColor(segment, colorScheme);
            double centerY = currentY - depthLen / 2.0;

            boreholeGroup.Children.Add(CreateCoreColumn(centerY, depthLen, color));

            currentY -= depthLen;
        }

        if (showDepthLabels)
        {
            for (double d = 0; d <= displayDepth + 0.001; d += actualTickInterval)
            {
                double y = -d;
                boreholeGroup.Children.Add(CreateDepthTick(0, y, _coreRadius + 0.4, d));
                boreholeGroup.Children.Add(CreateDepthLabel(0, y, _coreRadius + 1.2, d));
            }
        }

        if (showGroundwater && borehole.GroundwaterDepth > 0 && borehole.GroundwaterDepth <= displayDepth)
        {
            boreholeGroup.Children.Add(CreateGroundwaterPlane(-borehole.GroundwaterDepth, _coreRadius * 8));
        }

        foreach (var plane in structuralPlanes.Where(p => p.GlobalDepth.HasValue && p.Strike.HasValue && p.Dip.HasValue))
        {
            double y = -(plane.GlobalDepth ?? 0);
            if (y > 0 || y < -displayDepth) continue;
            boreholeGroup.Children.Add(CreateStructuralPlane(0, y, plane.Strike ?? 0, plane.Dip ?? 0));
        }

        ApplyBoreholeTransform(boreholeGroup, borehole);

        root.Children.Add(boreholeGroup);
        return root;
    }

    private static double CalculateTickInterval(double totalDepth)
    {
        if (totalDepth <= 10) return 1;
        if (totalDepth <= 50) return 5;
        if (totalDepth <= 100) return 10;
        if (totalDepth <= 200) return 20;
        return 50;
    }

    private static ModelVisual3D CreateLights()
    {
        var lightsGroup = new Model3DGroup();
        lightsGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(0.5, -1, -0.5)));
        lightsGroup.Children.Add(new DirectionalLight(Color.FromRgb(220, 230, 240), new Vector3D(-0.8, -0.3, -0.6)));
        lightsGroup.Children.Add(new AmbientLight(Color.FromRgb(80, 80, 85)));
        return new ModelVisual3D { Content = lightsGroup };
    }

    private static ModelVisual3D Create3DGridSpace(double sceneExtent, double displayDepth, double gridSpacing)
    {
        var group = new ModelVisual3D();
        var groundBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * 0.3), GroundGridColor.R, GroundGridColor.G, GroundGridColor.B));
        var wallBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * 0.15), WallGridColor.R, WallGridColor.G, WallGridColor.B));
        var majorGroundBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * 0.5), GroundGridColor.R, GroundGridColor.G, GroundGridColor.B));
        var majorWallBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * 0.25), WallGridColor.R, WallGridColor.G, WallGridColor.B));

        group.Children.Add(CreateGridPlane(
            center: new Point3D(0, 0, 0),
            width: sceneExtent * 2,
            length: sceneExtent * 2,
            normal: new Vector3D(0, 1, 0),
            minorDistance: gridSpacing,
            majorDistance: gridSpacing * 5,
            lineBrush: groundBrush,
            majorLineBrush: majorGroundBrush));

        group.Children.Add(CreateGridPlane(
            center: new Point3D(0, -displayDepth / 2.0, -sceneExtent),
            width: sceneExtent * 2,
            length: displayDepth + sceneExtent * 0.2,
            normal: new Vector3D(0, 0, 1),
            minorDistance: gridSpacing,
            majorDistance: gridSpacing * 5,
            lineBrush: wallBrush,
            majorLineBrush: majorWallBrush));

        group.Children.Add(CreateGridPlane(
            center: new Point3D(-sceneExtent, -displayDepth / 2.0, 0),
            width: sceneExtent * 2,
            length: displayDepth + sceneExtent * 0.2,
            normal: new Vector3D(1, 0, 0),
            minorDistance: gridSpacing,
            majorDistance: gridSpacing * 5,
            lineBrush: wallBrush,
            majorLineBrush: majorWallBrush));

        return group;
    }

    private static ModelVisual3D CreateGridPlane(Point3D center, double width, double length, Vector3D normal,
        double minorDistance, double majorDistance, Brush lineBrush, Brush majorLineBrush)
    {
        var group = new ModelVisual3D();
        var xAxis = normal.Y != 0 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
        var zAxis = Vector3D.CrossProduct(normal, xAxis);
        xAxis.Normalize();
        zAxis.Normalize();

        double halfWidth = width / 2;
        double halfLength = length / 2;

        for (double d = -halfWidth; d <= halfWidth + 0.001; d += minorDistance)
        {
            bool isMajor = Math.Abs(d % majorDistance) < 0.001;
            var points = new Point3DCollection();
            points.Add(center + d * xAxis - halfLength * zAxis);
            points.Add(center + d * xAxis + halfLength * zAxis);
            group.Children.Add(new LinesVisual3D
            {
                Points = points,
                Color = isMajor ? ((SolidColorBrush)majorLineBrush).Color : ((SolidColorBrush)lineBrush).Color,
                Thickness = isMajor ? 0.05 : 0.025
            });
        }

        for (double d = -halfLength; d <= halfLength + 0.001; d += minorDistance)
        {
            bool isMajor = Math.Abs(d % majorDistance) < 0.001;
            var points = new Point3DCollection();
            points.Add(center - halfWidth * xAxis + d * zAxis);
            points.Add(center + halfWidth * xAxis + d * zAxis);
            group.Children.Add(new LinesVisual3D
            {
                Points = points,
                Color = isMajor ? ((SolidColorBrush)majorLineBrush).Color : ((SolidColorBrush)lineBrush).Color,
                Thickness = isMajor ? 0.05 : 0.025
            });
        }

        return group;
    }

    private static ModelVisual3D CreateWorldAxes(double sceneExtent, Borehole borehole)
    {
        var group = new ModelVisual3D();
        double axisLen = sceneExtent * 0.75;

        group.Children.Add(CreateArrow(new Point3D(0, 0, 0), new Point3D(axisLen, 0, 0), AxisXColor));
        group.Children.Add(CreateAxisLabel($"东/X (E)", new Point3D(axisLen * 1.1, 0, 0), AxisXColor));

        group.Children.Add(CreateArrow(new Point3D(0, 0, 0), new Point3D(0, axisLen * 0.6, 0), AxisYColor));
        group.Children.Add(CreateAxisLabel($"上/Y (Z)", new Point3D(0, axisLen * 0.7, 0), AxisYColor));

        group.Children.Add(CreateArrow(new Point3D(0, 0, 0), new Point3D(0, 0, axisLen), AxisZColor));
        group.Children.Add(CreateAxisLabel($"北/Z (N)", new Point3D(0, 0, axisLen * 1.1), AxisZColor));

        return group;
    }

    private static ModelVisual3D CreateNorthArrow(double sceneExtent)
    {
        var group = new ModelVisual3D();
        double arrowLen = sceneExtent * 0.12;
        double posX = sceneExtent * 0.7;
        double posZ = sceneExtent * 0.7;

        group.Children.Add(CreateArrow(new Point3D(posX, 0.01, posZ), new Point3D(posX, 0.01, posZ + arrowLen), AxisZColor));

        group.Children.Add(new TextVisual3D
        {
            Text = "N",
            Position = new Point3D(posX + 0.5, 0.02, posZ + arrowLen + 0.5),
            Foreground = new SolidColorBrush(AxisZColor),
            Background = Brushes.Transparent,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Height = 0.8,
            Padding = new Thickness(1)
        });

        return group;
    }

    private static ModelVisual3D CreateGroundGridLabels(double sceneExtent, double gridSpacing, Borehole borehole)
    {
        var group = new ModelVisual3D();
        double originX = borehole.OrificeX;
        double originZ = borehole.OrificeY;

        for (double x = -sceneExtent; x <= sceneExtent + 0.001; x += gridSpacing * 5)
        {
            if (Math.Abs(x) < 0.001) continue;
            double worldX = originX + x;
            group.Children.Add(new TextVisual3D
            {
                Text = worldX.ToString("F0", CultureInfo.InvariantCulture),
                Position = new Point3D(x, 0.02, -sceneExtent * 0.9),
                Foreground = Brushes.DarkSlateGray,
                Background = Brushes.Transparent,
                FontSize = 9,
                Height = 0.4,
                Padding = new Thickness(1)
            });
        }

        for (double z = -sceneExtent; z <= sceneExtent + 0.001; z += gridSpacing * 5)
        {
            if (Math.Abs(z) < 0.001) continue;
            double worldZ = originZ + z;
            group.Children.Add(new TextVisual3D
            {
                Text = worldZ.ToString("F0", CultureInfo.InvariantCulture),
                Position = new Point3D(-sceneExtent * 0.9, 0.02, z),
                Foreground = Brushes.DarkSlateGray,
                Background = Brushes.Transparent,
                FontSize = 9,
                Height = 0.4,
                Padding = new Thickness(1)
            });
        }

        group.Children.Add(new TextVisual3D
        {
            Text = $"孔口 ({originX:F0}, {originZ:F0})",
            Position = new Point3D(0, 0.15, sceneExtent * 0.5),
            Foreground = Brushes.DarkSlateGray,
            Background = Brushes.Transparent,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Height = 0.5,
            Padding = new Thickness(2)
        });

        return group;
    }

    private static ModelVisual3D CreateOriginMark(Borehole borehole)
    {
        var group = new ModelVisual3D();
        double r = 0.5;

        var ringPoints = new Point3DCollection();
        int segs = 48;
        for (int i = 0; i <= segs; i++)
        {
            double a = 2 * Math.PI * i / segs;
            ringPoints.Add(new Point3D(r * Math.Cos(a), 0.01, r * Math.Sin(a)));
        }
        group.Children.Add(new LinesVisual3D
        {
            Points = ringPoints,
            Color = OriginColor,
            Thickness = 2
        });

        group.Children.Add(CreateSphere(0, 0.02, 0, 0.1, OriginColor));

        return group;
    }

    private static ModelVisual3D CreateCoreColumn(double centerY, double height, Color color)
    {
        var material = new DiffuseMaterial(new SolidColorBrush(color));

        var meshBuilder = new MeshBuilder();
        meshBuilder.AddCylinder(
            new Point3D(0, centerY - height / 2, 0),
            new Point3D(0, centerY + height / 2, 0),
            0.5, 32);

        return new ModelVisual3D
        {
            Content = new GeometryModel3D(meshBuilder.ToMesh(), material)
            {
                BackMaterial = material
            }
        };
    }

    private static ModelVisual3D CreateStructuralPlane(double centerX, double centerY, double strike, double dip)
    {
        const double size = 2.0;
        double dipRad = dip * Math.PI / 180.0;
        double strikeRad = strike * Math.PI / 180.0;
        double h = size / 2.0;

        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(80, 255, 60, 60)));

        var meshBuilder = new MeshBuilder();
        var corners = new[] { (-h, -h), (h, -h), (h, h), (-h, h) };
        var planePoints = new Point3DCollection();

        foreach (var (wx, wz) in corners)
        {
            double x1 = wx * Math.Cos(strikeRad) - wz * Math.Sin(strikeRad);
            double z1 = wx * Math.Sin(strikeRad) + wz * Math.Cos(strikeRad);
            double y1 = -h * Math.Sin(dipRad);
            double z2 = z1 + h * Math.Cos(dipRad);
            planePoints.Add(new Point3D(centerX + x1, centerY + y1, z2));
        }

        meshBuilder.AddQuad(planePoints[0], planePoints[1], planePoints[2], planePoints[3]);

        return new ModelVisual3D
        {
            Content = new GeometryModel3D(meshBuilder.ToMesh(), material)
            {
                BackMaterial = material
            }
        };
    }

    private static ModelVisual3D CreateGroundwaterPlane(double centerY, double width)
    {
        var points = new Point3DCollection
        {
            new Point3D(-width / 2, centerY, -width / 4),
            new Point3D(width / 2, centerY, -width / 4),
            new Point3D(width / 2, centerY, width / 4),
            new Point3D(-width / 2, centerY, width / 4),
            new Point3D(-width / 2, centerY, -width / 4)
        };
        return new LinesVisual3D
        {
            Points = points,
            Color = GroundwaterColor,
            Thickness = 3
        };
    }

    private static ModelVisual3D CreateDepthTick(double centerX, double depthY, double offset, double depth)
    {
        double tickLen = depth % 10 < 0.001 || depth % 10 > 9.999 ? 0.8 : 0.4;
        var points = new Point3DCollection
        {
            new Point3D(centerX + offset, depthY, 0),
            new Point3D(centerX + offset + tickLen, depthY, 0)
        };
        bool isMajor = depth % 10 < 0.001;
        return new LinesVisual3D
        {
            Points = points,
            Color = isMajor ? Color.FromRgb(50, 50, 50) : TickColor,
            Thickness = isMajor ? 2 : 1
        };
    }

    private static ModelVisual3D CreateDepthLabel(double centerX, double depthY, double offset, double depth)
    {
        return new TextVisual3D
        {
            Text = depth.ToString("0", CultureInfo.InvariantCulture) + "m",
            Position = new Point3D(centerX + offset, depthY, 0),
            Foreground = Brushes.DarkSlateGray,
            Background = Brushes.Transparent,
            FontSize = 9,
            Height = 0.5,
            Padding = new Thickness(1)
        };
    }

    private static ModelVisual3D CreateArrow(Point3D start, Point3D end, Color color)
    {
        var brush = new SolidColorBrush(color);
        return new ArrowVisual3D
        {
            Point1 = start,
            Point2 = end,
            Diameter = 0.12,
            HeadLength = 0.4,
            Fill = brush,
            Material = new DiffuseMaterial(brush)
        };
    }

    private static ModelVisual3D CreateAxisLabel(string text, Point3D position, Color color)
    {
        return new TextVisual3D
        {
            Text = text,
            Position = position,
            Foreground = new SolidColorBrush(color),
            Background = Brushes.Transparent,
            FontSize = 11,
            Height = 0.65,
            Padding = new Thickness(1)
        };
    }

    private static ModelVisual3D CreateSphere(double x, double y, double z, double radius, Color color)
    {
        var meshBuilder = new MeshBuilder();
        meshBuilder.AddSphere(new Point3D(x, y, z), radius, 16, 12);
        var material = new DiffuseMaterial(new SolidColorBrush(color));
        return new ModelVisual3D
        {
            Content = new GeometryModel3D(meshBuilder.ToMesh(), material)
        };
    }

    private static void ApplyBoreholeTransform(ModelVisual3D group, Borehole borehole)
    {
        if (Math.Abs(borehole.InclinationAngle) < 0.01 && Math.Abs(borehole.Azimuth) < 0.01)
            return;

        var transformGroup = new Transform3DGroup();

        if (Math.Abs(borehole.Azimuth) > 0.01)
        {
            transformGroup.Children.Add(new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 1, 0), -borehole.Azimuth)));
        }

        if (Math.Abs(borehole.InclinationAngle) > 0.01)
        {
            transformGroup.Children.Add(new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(1, 0, 0), borehole.InclinationAngle)));
        }

        group.Transform = transformGroup;
    }

    private static Color GetSegmentColor(ClassificationSegment segment, ColorScheme scheme)
    {
        return scheme switch
        {
            ColorScheme.ByRockClass => ColorMapper.GetColorByRockClass(segment.RockClass),
            ColorScheme.ByIntegrityLevel => ColorMapper.GetColorByIntegrityLevel(segment.IntegrityLevel),
            ColorScheme.ByRockType => ColorMapper.GetColorByRockType(segment.RockType),
            _ => ColorMapper.GetColorByRockClass(segment.RockClass)
        };
    }
}