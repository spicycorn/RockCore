using CommunityToolkit.Mvvm.ComponentModel;
using RockCore.Core.Enums;
using RockCore.Core.Models;

namespace RockCore.Wpf.ViewModels;

public partial class CorePhotoViewModel : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private int _boreholeId;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _relativePath = string.Empty;

    [ObservableProperty]
    private double _depthStart;

    [ObservableProperty]
    private double _depthEnd;

    [ObservableProperty]
    private int _boxNumber;

    [ObservableProperty]
    private IntegrityLevel _integrityLevel = IntegrityLevel.UserOverride;

    [ObservableProperty]
    private double _integrityIndex;

    [ObservableProperty]
    private double _rQD;

    [ObservableProperty]
    private int _jointCount;

    [ObservableProperty]
    private double _avgJointSpacingCm;

    [ObservableProperty]
    private bool _isFragmented;

    [ObservableProperty]
    private string _analysisStatus = "未分析";

    [ObservableProperty]
    private bool _isUserModified;

    [ObservableProperty]
    private string? _analysisResultJson;

    public double Length => DepthEnd - DepthStart;

    public CorePhotoViewModel()
    {
    }

    public CorePhotoViewModel(CorePhoto model)
    {
        Id = model.Id;
        BoreholeId = model.BoreholeId;
        FileName = model.FileName;
        RelativePath = model.RelativePath;
        DepthStart = model.DepthStart;
        DepthEnd = model.DepthEnd;
        BoxNumber = model.BoxNumber;
        IntegrityLevel = model.IntegrityLevel;
        IntegrityIndex = model.IntegrityIndex;
        RQD = model.RQD;
        JointCount = model.JointCount;
        AvgJointSpacingCm = model.AvgJointSpacingCm;
        IsFragmented = model.IsFragmented;
        AnalysisStatus = model.AnalysisStatus;
        IsUserModified = model.IsUserModified;
        AnalysisResultJson = model.AnalysisResultJson;
    }

    public CorePhoto ToModel()
    {
        return new CorePhoto
        {
            Id = Id,
            BoreholeId = BoreholeId,
            FileName = FileName,
            RelativePath = RelativePath,
            DepthStart = DepthStart,
            DepthEnd = DepthEnd,
            BoxNumber = BoxNumber,
            IntegrityLevel = IntegrityLevel,
            IntegrityIndex = IntegrityIndex,
            RQD = RQD,
            JointCount = JointCount,
            AvgJointSpacingCm = AvgJointSpacingCm,
            IsFragmented = IsFragmented,
            AnalysisStatus = AnalysisStatus,
            IsUserModified = IsUserModified,
            AnalysisResultJson = AnalysisResultJson
        };
    }
}
