using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Wpf.ViewModels;

/// <summary>
/// 项目级围岩统计 ViewModel。
/// </summary>
public partial class ProjectStatisticsViewModel : ObservableObject
{
    [ObservableProperty]
    private double _totalCoreLength;

    [ObservableProperty]
    private double _totalClassifiedLength;

    [ObservableProperty]
    private double _totalUnclassifiedLength;

    [ObservableProperty]
    private string _overallClassSummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ClassStatViewModel> _classStats = new();

    [ObservableProperty]
    private ObservableCollection<BoreholeStatViewModel> _boreholeStats = new();

    [ObservableProperty]
    private ObservableCollection<WeakSectionViewModel> _weakSections = new();

    public void Update(ProjectStatisticsResult result)
    {
        TotalCoreLength = result.TotalCoreLength;
        TotalClassifiedLength = result.TotalClassifiedLength;
        TotalUnclassifiedLength = result.TotalUnclassifiedLength;
        OverallClassSummary = result.OverallClassSummary;

        ClassStats.Clear();
        foreach (var stat in result.ClassStatistics)
        {
            ClassStats.Add(new ClassStatViewModel(stat));
        }

        BoreholeStats.Clear();
        foreach (var stat in result.BoreholeStatistics)
        {
            BoreholeStats.Add(new BoreholeStatViewModel(stat));
        }

        WeakSections.Clear();
        foreach (var section in result.WeakSections)
        {
            WeakSections.Add(new WeakSectionViewModel(section));
        }
    }
}

public partial class ClassStatViewModel : ObservableObject
{
    public ClassStatViewModel(RockClassStatistics stat)
    {
        RockClass = stat.RockClass;
        ClassDescription = stat.ClassDescription;
        TotalLength = stat.TotalLength;
        Ratio = stat.Ratio;
        BoreholeCount = stat.BoreholeCount;
        DepthRangeText = string.Join("、", stat.DepthRanges.Select(r => $"{r.Start:F2}-{r.End:F2}m"));
    }

    [ObservableProperty]
    private RockClass _rockClass;

    [ObservableProperty]
    private string _classDescription = string.Empty;

    [ObservableProperty]
    private double _totalLength;

    [ObservableProperty]
    private double _ratio;

    [ObservableProperty]
    private int _boreholeCount;

    [ObservableProperty]
    private string _depthRangeText = string.Empty;
}

public partial class BoreholeStatViewModel : ObservableObject
{
    public BoreholeStatViewModel(BoreholeStatistics stat)
    {
        BoreholeNumber = stat.BoreholeNumber;
        TotalLength = stat.TotalLength;
        ClassifiedLength = stat.ClassifiedLength;
        UnclassifiedLength = stat.UnclassifiedLength;
        ClassSummary = stat.ClassSummary;
    }

    [ObservableProperty]
    private string _boreholeNumber = string.Empty;

    [ObservableProperty]
    private double _totalLength;

    [ObservableProperty]
    private double _classifiedLength;

    [ObservableProperty]
    private double _unclassifiedLength;

    [ObservableProperty]
    private string _classSummary = string.Empty;
}

public partial class WeakSectionViewModel : ObservableObject
{
    public WeakSectionViewModel(WeakSection section)
    {
        BoreholeNumber = section.BoreholeNumber;
        DepthStart = section.DepthStart;
        DepthEnd = section.DepthEnd;
        RockClass = section.RockClass;
        Length = section.Length;
    }

    [ObservableProperty]
    private string _boreholeNumber = string.Empty;

    [ObservableProperty]
    private double _depthStart;

    [ObservableProperty]
    private double _depthEnd;

    [ObservableProperty]
    private RockClass _rockClass;

    [ObservableProperty]
    private double _length;
}
