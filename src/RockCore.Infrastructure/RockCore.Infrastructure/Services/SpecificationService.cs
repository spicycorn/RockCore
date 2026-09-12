using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;

namespace RockCore.Infrastructure.Services;

public class SpecificationService : ISpecificationService
{
    private readonly string _configPath;
    private RockSpecificationConfig _currentConfig = null!;

    public RockSpecificationConfig CurrentConfig => _currentConfig;

    public SpecificationService()
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rock_specification.json");
        LoadConfig();
    }

    private void LoadConfig()
    {
        if (File.Exists(_configPath))
        {
            try
            {
                string json = File.ReadAllText(_configPath);
                _currentConfig = JsonSerializer.Deserialize<RockSpecificationConfig>(json) ?? GetDefaultConfig();
                EnsureDefaults(_currentConfig);
            }
            catch
            {
                _currentConfig = GetDefaultConfig();
                SaveConfig(_currentConfig);
            }
        }
        else
        {
            _currentConfig = GetDefaultConfig();
            SaveConfig(_currentConfig);
        }
    }

    private void EnsureDefaults(RockSpecificationConfig config)
    {
        bool changed = false;

        if (config.Terminology == null || config.Terminology.Count == 0)
        {
            config.Terminology = new Dictionary<string, string>
            {
                ["HardRock"] = "硬质岩",
                ["SoftRock"] = "软质岩",
                ["Intact"] = "完整",
                ["RelativelyIntact"] = "较完整",
                ["Poor"] = "完整性差",
                ["RelativelyBroken"] = "较破碎",
                ["Broken"] = "破碎"
            };
            changed = true;
        }

        if (config.ConfigThresholds == null)
        {
            config.ConfigThresholds = new RockSpecificationConfig.Thresholds { CaveAxisAngleThreshold = 30.0 };
            changed = true;
        }

        // 岩体结构类型与完整性等级的映射必须严格对应规范表F.0.2
        // 仅当配置为空时初始化，不自动覆盖已有配置
        if (config.StructureMappings == null || config.StructureMappings.Count == 0)
        {
            config.StructureMappings = new Dictionary<string, List<int>>();
            AddMapping(config, RockType.HardRock, IntegrityLevel.Intact, new[] { RockStructureType.Massive });
            AddMapping(config, RockType.HardRock, IntegrityLevel.RelativelyIntact, new[] { RockStructureType.Blocky, RockStructureType.SubBlocky, RockStructureType.ThickLayered, RockStructureType.Interbedded });
            AddMapping(config, RockType.HardRock, IntegrityLevel.Poor, new[] { RockStructureType.ThinLayered, RockStructureType.Mosaic, RockStructureType.BlockyFractured });
            AddMapping(config, RockType.HardRock, IntegrityLevel.RelativelyBroken, new[] { RockStructureType.Cataclastic });
            AddMapping(config, RockType.HardRock, IntegrityLevel.Broken, new[] { RockStructureType.GranularHard });
            AddMapping(config, RockType.SoftRock, IntegrityLevel.Intact, new[] { RockStructureType.Massive });
            AddMapping(config, RockType.SoftRock, IntegrityLevel.RelativelyIntact, new[] { RockStructureType.BlockyOrSubBlocky, RockStructureType.ThickOrInterbedded });
            AddMapping(config, RockType.SoftRock, IntegrityLevel.Poor, new[] { RockStructureType.ThinOrBlockyFractured });
            AddMapping(config, RockType.SoftRock, IntegrityLevel.RelativelyBroken, new[] { RockStructureType.Cataclastic });
            AddMapping(config, RockType.SoftRock, IntegrityLevel.Broken, new[] { RockStructureType.GranularSoft });
            changed = true;
        }

        // 如果 F.0.4 表缺失，或存在旧版 5 行数据（与阶段三逻辑不一致），则重置为新的 7 行默认值
        var expectedKeys = new[] { "Intact", "RelativelyIntact", "RelativelyIntact", "Poor", "Poor", "RelativelyBroken", "Broken" };
        bool criteriaMissing = config.IntegrityLevelCriteria == null || config.IntegrityLevelCriteria.Count == 0;
        bool criteriaOutdated = !criteriaMissing && !expectedKeys.SequenceEqual(config.IntegrityLevelCriteria!.Select(c => c.LevelKey));

        if (criteriaMissing || criteriaOutdated)
        {
            config.IntegrityLevelCriteria = GetDefaultIntegrityLevelCriteria();
            changed = true;
        }

        if (changed)
            SaveConfig(config);
    }

    private static List<IntegrityLevelCriterion> GetDefaultIntegrityLevelCriteria()
    {
        return new List<IntegrityLevelCriterion>
        {
            new() { LevelKey = "Intact", JointSetCount = "1~2", JointSpacing = ">95", JointDevelopment = "不发育" },
            new() { LevelKey = "RelativelyIntact", JointSetCount = "1~2", JointSpacing = "50~95", JointDevelopment = "轻度发育" },
            new() { LevelKey = "RelativelyIntact", JointSetCount = "2~3", JointSpacing = "30~50", JointDevelopment = "中等发育" },
            new() { LevelKey = "Poor", JointSetCount = "2~3", JointSpacing = "10~30", JointDevelopment = "较发育" },
            new() { LevelKey = "Poor", JointSetCount = "2~3", JointSpacing = "≤10", JointDevelopment = "发育" },
            new() { LevelKey = "RelativelyBroken", JointSetCount = ">3", JointSpacing = "≤10", JointDevelopment = "很发育" },
            new() { LevelKey = "Broken", JointSetCount = "—", JointSpacing = "<2", JointDevelopment = "——" }
        };
    }

    public void SaveConfig(RockSpecificationConfig config)
    {
        _currentConfig = config;
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    public RockStructureType[] GetAvailableStructures(RockType rockType, IntegrityLevel level)
    {
        string key = $"{rockType}_{level}";
        if (_currentConfig.StructureMappings.TryGetValue(key, out var structures))
        {
            return structures.Select(s => (RockStructureType)s).ToArray();
        }
        return rockType == RockType.HardRock ? 
            new[] { RockStructureType.Massive, RockStructureType.Blocky, RockStructureType.SubBlocky, RockStructureType.ThickLayered, RockStructureType.Interbedded, RockStructureType.ThinLayered, RockStructureType.Mosaic, RockStructureType.BlockyFractured, RockStructureType.Cataclastic, RockStructureType.GranularHard } :
            new[] { RockStructureType.Massive, RockStructureType.BlockyOrSubBlocky, RockStructureType.ThickOrInterbedded, RockStructureType.ThinOrBlockyFractured, RockStructureType.Cataclastic, RockStructureType.GranularSoft };
    }

    public double GetCaveAxisThreshold() => _currentConfig.ConfigThresholds.CaveAxisAngleThreshold;

    private RockSpecificationConfig GetDefaultConfig()
    {
        var config = new RockSpecificationConfig();
        
        config.ConfigThresholds.CaveAxisAngleThreshold = 30.0;

        // 硬质岩
        AddMapping(config, RockType.HardRock, IntegrityLevel.Intact, new[] { RockStructureType.Massive });
        AddMapping(config, RockType.HardRock, IntegrityLevel.RelativelyIntact, new[] { RockStructureType.Blocky, RockStructureType.SubBlocky, RockStructureType.ThickLayered, RockStructureType.Interbedded });
        AddMapping(config, RockType.HardRock, IntegrityLevel.Poor, new[] { RockStructureType.ThinLayered, RockStructureType.Mosaic, RockStructureType.BlockyFractured });
        AddMapping(config, RockType.HardRock, IntegrityLevel.RelativelyBroken, new[] { RockStructureType.Cataclastic });
        AddMapping(config, RockType.HardRock, IntegrityLevel.Broken, new[] { RockStructureType.GranularHard });

        // 软质岩
        AddMapping(config, RockType.SoftRock, IntegrityLevel.Intact, new[] { RockStructureType.Massive });
        AddMapping(config, RockType.SoftRock, IntegrityLevel.RelativelyIntact, new[] { RockStructureType.BlockyOrSubBlocky, RockStructureType.ThickOrInterbedded });
        AddMapping(config, RockType.SoftRock, IntegrityLevel.Poor, new[] { RockStructureType.ThinOrBlockyFractured });
        AddMapping(config, RockType.SoftRock, IntegrityLevel.RelativelyBroken, new[] { RockStructureType.Cataclastic });
        AddMapping(config, RockType.SoftRock, IntegrityLevel.Broken, new[] { RockStructureType.GranularSoft });

        // 词汇映射
        config.Terminology["HardRock"] = "硬质岩";
        config.Terminology["SoftRock"] = "软质岩";
        config.Terminology["Intact"] = "完整";
        config.Terminology["RelativelyIntact"] = "较完整";
        config.Terminology["Poor"] = "完整性差";
        config.Terminology["RelativelyBroken"] = "较破碎";
        config.Terminology["Broken"] = "破碎";

        // 岩体完整程度划分表 F.0.4
        // 按阶段三实际判定逻辑（RuleEngineImageAnalyzer.JudgeSegmentDouble）对应：
        config.IntegrityLevelCriteria = GetDefaultIntegrityLevelCriteria();

        return config;
    }

    private void AddMapping(RockSpecificationConfig config, RockType rt, IntegrityLevel lvl, RockStructureType[] structures)
    {
        config.StructureMappings[$"{rt}_{lvl}"] = structures.Select(s => (int)s).ToList();
    }
}