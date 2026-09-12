using System.Windows.Media.Media3D;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Wpf.ThreeDim;

namespace RockCore.Wpf.ThreeDim;

public interface IThreeDimSceneService
{
    Task<IEnumerable<ClassificationSegment>> GetClassificationSegmentsAsync(int boreholeId);
    Task<List<StructuralPlane>> GetStructuralPlanesAsync(int boreholeId);
    Task<ModelVisual3D> BuildBoreholeSceneAsync(
        Borehole borehole,
        ColorScheme colorScheme,
        double? tickInterval = null,
        bool showStructuralPlanes = true,
        bool showGroundwater = true,
        bool showDepthLabels = true,
        double gridSpacing = 0);
}

public class ThreeDimSceneService : IThreeDimSceneService
{
    private readonly ICorePhotoRepository _corePhotoRepository;
    private readonly IClassificationSegmentRepository _classificationSegmentRepository;
    private readonly IStructuralPlaneRepository _structuralPlaneRepository;

    public ThreeDimSceneService(
        ICorePhotoRepository corePhotoRepository,
        IClassificationSegmentRepository classificationSegmentRepository,
        IStructuralPlaneRepository structuralPlaneRepository)
    {
        _corePhotoRepository = corePhotoRepository;
        _classificationSegmentRepository = classificationSegmentRepository;
        _structuralPlaneRepository = structuralPlaneRepository;
    }

    public async Task<IEnumerable<ClassificationSegment>> GetClassificationSegmentsAsync(int boreholeId)
    {
        return await _classificationSegmentRepository.GetByBoreholeIdAsync(boreholeId);
    }

    public async Task<List<StructuralPlane>> GetStructuralPlanesAsync(int boreholeId)
    {
        var photos = await _corePhotoRepository.GetByBoreholeIdAsync(boreholeId);
        var allPlanes = new List<StructuralPlane>();
        
        if (photos == null || !photos.Any())
            return allPlanes;

        foreach (var photo in photos)
        {
            var planes = await _structuralPlaneRepository.GetByCorePhotoIdAsync(photo.Id);
            if (planes != null) allPlanes.AddRange(planes);
        }

        return allPlanes;
    }

    public async Task<ModelVisual3D> BuildBoreholeSceneAsync(
        Borehole borehole,
        ColorScheme colorScheme,
        double? tickInterval = null,
        bool showStructuralPlanes = true,
        bool showGroundwater = true,
        bool showDepthLabels = true,
        double gridSpacing = 0)
    {
        if (borehole == null) 
            return new SceneBuilder().BuildScene(
                borehole: null!,
                classificationSegments: Array.Empty<ClassificationSegment>(),
                structuralPlanes: Array.Empty<StructuralPlane>(),
                colorScheme);

        var segments = await _classificationSegmentRepository.GetByBoreholeIdAsync(borehole.Id);

        var photos = await _corePhotoRepository.GetByBoreholeIdAsync(borehole.Id);
        var allPlanes = new List<StructuralPlane>();
        foreach (var photo in photos ?? [])
        {
            var planes = await _structuralPlaneRepository.GetByCorePhotoIdAsync(photo.Id);
            if (planes != null) allPlanes.AddRange(planes);
        }

        var builder = new SceneBuilder(gridSpacing: gridSpacing);
        return builder.BuildScene(
            borehole,
            segments ?? [],
            showStructuralPlanes ? allPlanes : [],
            colorScheme,
            tickInterval,
            showGroundwater,
            showDepthLabels);
    }
}