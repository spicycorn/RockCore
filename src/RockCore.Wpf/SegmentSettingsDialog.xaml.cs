using System.Windows;
using System.Windows.Controls;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Wpf;

public partial class SegmentSettingsDialog : Window
{
    private readonly IntegrityLevel _targetLevel;

    public double DepthStart { get; private set; }
    public double DepthEnd { get; private set; }
    public RockType? SelectedRockType { get; private set; }
    public RockStructureType? SelectedRockStructureType { get; private set; }
    public RockHardnessLevel? SelectedHardnessLevel { get; private set; }
    public RockHomogeneity? SelectedHomogeneity { get; private set; }
    public GroundwaterCondition? SelectedGroundwaterCondition { get; private set; }
    public bool? CaveAxisAngleLessThan30 { get; private set; }

    public SegmentSettingsDialog(IntegrityLevel targetLevel)
    {
        InitializeComponent();
        _targetLevel = targetLevel;

        Title = $"分段设置参数 - {GetLevelDescription(targetLevel)}";

        RockTypeCombo.ItemsSource = RockTypeHelper.Values;
        RockStructureCombo.ItemsSource = RockStructureMapping.AllStructures;

        RockHardnessLevel[] hardnessValues = {
            RockHardnessLevel.StrongRock,
            RockHardnessLevel.MediumHardRock,
            RockHardnessLevel.RelativelySoftRock,
            RockHardnessLevel.SoftRock
        };
        HardnessCombo.ItemsSource = hardnessValues;

        RockHomogeneity[] homogeneityValues = {
            RockHomogeneity.HomogeneousNoWeakLayer,
            RockHomogeneity.HasWeakLayer
        };
        HomogeneityCombo.ItemsSource = homogeneityValues;

        GroundwaterCondition[] gwValues = {
            GroundwaterCondition.Dry,
            GroundwaterCondition.Damp,
            GroundwaterCondition.Wet
        };
        GroundwaterCombo.ItemsSource = gwValues;
    }

    private static string GetLevelDescription(IntegrityLevel level)
    {
        return level switch
        {
            IntegrityLevel.Intact => "完整",
            IntegrityLevel.RelativelyIntact => "较完整",
            IntegrityLevel.Poor => "完整性差",
            IntegrityLevel.RelativelyBroken => "较破碎",
            IntegrityLevel.Broken => "破碎",
            _ => string.Empty
        };
    }

    private void RockTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RockTypeCombo.SelectedItem is RockType rockType)
        {
            RockStructureCombo.ItemsSource = RockStructureMapping.GetStructures(rockType, _targetLevel);
            RockStructureCombo.SelectedIndex = -1;
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(DepthStartTxt.Text, out double start) || start < 0)
        {
            MessageBox.Show("请输入有效的起点深度", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!double.TryParse(DepthEndTxt.Text, out double end) || end <= start)
        {
            MessageBox.Show("请输入有效的终点深度（需大于起点）", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DepthStart = start;
        DepthEnd = end;

        if (RockTypeCombo.SelectedItem is RockType rt)
            SelectedRockType = rt;
        if (RockStructureCombo.SelectedItem is RockStructureType rst)
            SelectedRockStructureType = rst;
        if (HardnessCombo.SelectedItem is RockHardnessLevel rhl)
            SelectedHardnessLevel = rhl;
        if (HomogeneityCombo.SelectedItem is RockHomogeneity rh)
            SelectedHomogeneity = rh;
        if (GroundwaterCombo.SelectedItem is GroundwaterCondition gc)
            SelectedGroundwaterCondition = gc;

        if (CaveAxisCombo.SelectedIndex == 1)
            CaveAxisAngleLessThan30 = true;
        else if (CaveAxisCombo.SelectedIndex == 2)
            CaveAxisAngleLessThan30 = false;
        else
            CaveAxisAngleLessThan30 = null;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
