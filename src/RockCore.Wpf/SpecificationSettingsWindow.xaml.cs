using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Infrastructure.Services;

namespace RockCore.Wpf;

public partial class SpecificationSettingsWindow : Window
{
    private readonly ISpecificationService _specificationService;
    private RockSpecificationConfig _config;

    public SpecificationSettingsWindow(ISpecificationService specificationService)
    {
        InitializeComponent();
        _specificationService = specificationService;
        _config = CloneConfig(_specificationService.CurrentConfig);
        LoadConfigToUI();
    }

    private void LoadConfigToUI()
    {
        CaveAxisThresholdTxt.Text = _config.ConfigThresholds.CaveAxisAngleThreshold.ToString("F1");

        TerminologyItems.ItemsSource = _config.Terminology
            .Select(kvp => new TerminologyItem { Key = kvp.Key, Value = kvp.Value })
            .ToList();

        var hardRockRows = RockStructureTypeHelper.HardRockStructures
            .Select(s => new StructureMappingRow(s, RockType.HardRock, _config.StructureMappings))
            .ToList();
        HardRockMappingGrid.ItemsSource = hardRockRows;

        var softRockRows = RockStructureTypeHelper.SoftRockStructures
            .Select(s => new StructureMappingRow(s, RockType.SoftRock, _config.StructureMappings))
            .ToList();
        SoftRockMappingGrid.ItemsSource = softRockRows;

        var criteriaRows = _config.IntegrityLevelCriteria
            .Select(c => new IntegrityCriterionRow(c))
            .ToList();
        IntegrityCriteriaGrid.ItemsSource = criteriaRows;
    }

    private static RockSpecificationConfig CloneConfig(RockSpecificationConfig source)
    {
        return new RockSpecificationConfig
        {
            Terminology = new Dictionary<string, string>(source.Terminology),
            ConfigThresholds = new RockSpecificationConfig.Thresholds
            {
                CaveAxisAngleThreshold = source.ConfigThresholds.CaveAxisAngleThreshold
            },
            StructureMappings = source.StructureMappings
                .ToDictionary(kvp => kvp.Key, kvp => new List<int>(kvp.Value)),
            IntegrityLevelCriteria = source.IntegrityLevelCriteria
                .Select(c => new IntegrityLevelCriterion
                {
                    LevelKey = c.LevelKey,
                    JointSetCount = c.JointSetCount,
                    JointSpacing = c.JointSpacing,
                    JointDevelopment = c.JointDevelopment
                })
                .ToList()
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(CaveAxisThresholdTxt.Text, out double threshold) || threshold <= 0)
        {
            MessageBox.Show("请输入有效的夹角阈值（大于0）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.ConfigThresholds.CaveAxisAngleThreshold = threshold;

        _config.Terminology.Clear();
        foreach (var item in TerminologyItems.Items)
        {
            if (item is TerminologyItem ti)
                _config.Terminology[ti.Key] = ti.Value;
        }

        _config.StructureMappings.Clear();
        SaveRowsToConfig(HardRockMappingGrid.ItemsSource as IEnumerable<StructureMappingRow>, RockType.HardRock);
        SaveRowsToConfig(SoftRockMappingGrid.ItemsSource as IEnumerable<StructureMappingRow>, RockType.SoftRock);

        _config.IntegrityLevelCriteria.Clear();
        if (IntegrityCriteriaGrid.ItemsSource is IEnumerable<IntegrityCriterionRow> criteriaRows)
        {
            foreach (var row in criteriaRows)
            {
                _config.IntegrityLevelCriteria.Add(new IntegrityLevelCriterion
                {
                    LevelKey = IntegrityCriterionRow.ToLevelKeyString(row.LevelKey),
                    JointSetCount = row.JointSetCount,
                    JointSpacing = row.JointSpacing,
                    JointDevelopment = row.JointDevelopment
                });
            }
        }

        _specificationService.SaveConfig(_config);
        DialogResult = true;
        Close();
    }

    private void SaveRowsToConfig(IEnumerable<StructureMappingRow>? rows, RockType rockType)
    {
        if (rows == null) return;

        foreach (IntegrityLevel level in IntegrityLevelHelper.Values)
        {
            var key = $"{rockType}_{level}";
            var selected = rows
                .Where(r => r.GetLevelValue(level))
                .Select(r => (int)r.StructureType)
                .ToList();
            _config.StructureMappings[key] = selected;
        }
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaultConfig = new SpecificationService().CurrentConfig;
        _config = CloneConfig(defaultConfig);
        LoadConfigToUI();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public class TerminologyItem
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class IntegrityCriterionRow : INotifyPropertyChanged
{
    private IntegrityLevel _levelKey;
    private string _jointSetCount;
    private string _jointSpacing;
    private string _jointDevelopment;

    public IntegrityLevel LevelKey
    {
        get => _levelKey;
        set => SetProperty(ref _levelKey, value);
    }

    public string JointSetCount
    {
        get => _jointSetCount;
        set => SetProperty(ref _jointSetCount, value);
    }

    public string JointSpacing
    {
        get => _jointSpacing;
        set => SetProperty(ref _jointSpacing, value);
    }

    public string JointDevelopment
    {
        get => _jointDevelopment;
        set => SetProperty(ref _jointDevelopment, value);
    }

    public IntegrityCriterionRow(IntegrityLevelCriterion criterion)
    {
        _levelKey = ParseLevelKey(criterion.LevelKey);
        _jointSetCount = criterion.JointSetCount;
        _jointSpacing = criterion.JointSpacing;
        _jointDevelopment = criterion.JointDevelopment;
    }

    private static IntegrityLevel ParseLevelKey(string key)
    {
        return key switch
        {
            "Intact" => IntegrityLevel.Intact,
            "RelativelyIntact" => IntegrityLevel.RelativelyIntact,
            "Poor" => IntegrityLevel.Poor,
            "RelativelyBroken" => IntegrityLevel.RelativelyBroken,
            "Broken" => IntegrityLevel.Broken,
            _ => IntegrityLevel.Intact
        };
    }

    public static string ToLevelKeyString(IntegrityLevel level)
    {
        return level switch
        {
            IntegrityLevel.Intact => "Intact",
            IntegrityLevel.RelativelyIntact => "RelativelyIntact",
            IntegrityLevel.Poor => "Poor",
            IntegrityLevel.RelativelyBroken => "RelativelyBroken",
            IntegrityLevel.Broken => "Broken",
            _ => "Intact"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class StructureMappingRow : INotifyPropertyChanged
{
    public RockStructureType StructureType { get; }
    public string StructureName { get; }

    private bool _intact;
    private bool _relativelyIntact;
    private bool _poor;
    private bool _relativelyBroken;
    private bool _broken;

    public bool Intact
    {
        get => _intact;
        set => SetProperty(ref _intact, value);
    }

    public bool RelativelyIntact
    {
        get => _relativelyIntact;
        set => SetProperty(ref _relativelyIntact, value);
    }

    public bool Poor
    {
        get => _poor;
        set => SetProperty(ref _poor, value);
    }

    public bool RelativelyBroken
    {
        get => _relativelyBroken;
        set => SetProperty(ref _relativelyBroken, value);
    }

    public bool Broken
    {
        get => _broken;
        set => SetProperty(ref _broken, value);
    }

    public StructureMappingRow(RockStructureType structureType, RockType rockType,
        Dictionary<string, List<int>> structureMappings)
    {
        StructureType = structureType;
        StructureName = GetEnumDescription(structureType);

        foreach (IntegrityLevel level in IntegrityLevelHelper.Values)
        {
            var key = $"{rockType}_{level}";
            var selected = structureMappings.TryGetValue(key, out var list) ? list : new List<int>();
            SetLevelValue(level, selected.Contains((int)structureType));
        }
    }

    public bool GetLevelValue(IntegrityLevel level)
    {
        return level switch
        {
            IntegrityLevel.Intact => Intact,
            IntegrityLevel.RelativelyIntact => RelativelyIntact,
            IntegrityLevel.Poor => Poor,
            IntegrityLevel.RelativelyBroken => RelativelyBroken,
            IntegrityLevel.Broken => Broken,
            _ => false
        };
    }

    public void SetLevelValue(IntegrityLevel level, bool value)
    {
        switch (level)
        {
            case IntegrityLevel.Intact: Intact = value; break;
            case IntegrityLevel.RelativelyIntact: RelativelyIntact = value; break;
            case IntegrityLevel.Poor: Poor = value; break;
            case IntegrityLevel.RelativelyBroken: RelativelyBroken = value; break;
            case IntegrityLevel.Broken: Broken = value; break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private static string GetEnumDescription(RockStructureType value)
    {
        var field = value.GetType().GetField(value.ToString());
        if (field == null) return value.ToString();
        var attr = field.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .FirstOrDefault() as System.ComponentModel.DescriptionAttribute;
        return attr?.Description ?? value.ToString();
    }
}
