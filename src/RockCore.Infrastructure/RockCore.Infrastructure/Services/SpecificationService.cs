using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RockCore.Core.Enums;
using RockCore.Core.Interfaces;
using RockCore.Core.Models;
using RockCore.Core.Services;

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

        // F.0.4 完整程度划分表：
        //   1) 配置缺失（空）时填充默认值；
        //   2) 用户已修改的判定表绝不覆盖（行序 = 优先级，尊重用户设置）；
        //   3) 定向迁移：旧版默认表（破碎行在末尾）迁移为新行序（破碎行置顶），
        //      保持"间距<2cm → 破碎"的最高优先级与历史行为一致。
        if (config.IntegrityLevelCriteria == null || config.IntegrityLevelCriteria.Count == 0)
        {
            config.IntegrityLevelCriteria = IntegrityCriteriaEngine.GetDefaultCriteria();
            changed = true;
        }
        else if (IsLegacyDefaultCriteria(config.IntegrityLevelCriteria))
        {
            // 旧默认表：将末行（Broken, —, <2）移到表首
            var rows = config.IntegrityLevelCriteria;
            var last = rows[rows.Count - 1];
            rows.RemoveAt(rows.Count - 1);
            rows.Insert(0, last);
            changed = true;
        }

        if (changed)
            SaveConfig(config);
    }

    /// <summary>
    /// 判断判定表是否为旧版默认表（7 行，破碎行在末尾）。
    /// 仅当与旧默认表逐行完全一致时返回 true，避免误伤用户自定义表。
    /// </summary>
    private static bool IsLegacyDefaultCriteria(List<IntegrityLevelCriterion> criteria)
    {
        if (criteria.Count != 7) return false;

        var expected = new (string Level, string J, string S)[]
        {
            ("Intact",           "1~2", ">95"),
            ("RelativelyIntact", "1~2", "50~95"),
            ("RelativelyIntact", "2~3", "30~50"),
            ("Poor",             "2~3", "10~30"),
            ("Poor",             "2~3", "≤10"),
            ("RelativelyBroken", ">3",  "≤10"),
            ("Broken",           "—",   "<2")
        };

        for (int i = 0; i < 7; i++)
        {
            var c = criteria[i];
            if (string.Equals(c.LevelKey, expected[i].Level, StringComparison.OrdinalIgnoreCase)
                && string.Equals((c.JointSetCount ?? "").Trim(), expected[i].J, StringComparison.OrdinalIgnoreCase)
                && string.Equals((c.JointSpacing ?? "").Trim(), expected[i].S, StringComparison.OrdinalIgnoreCase))
                continue;
            return false;
        }
        return true;
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
        // 使用 Core 层统一的默认判定表（与 RuleEngineImageAnalyzer 判定逻辑一致）：
        config.IntegrityLevelCriteria = IntegrityCriteriaEngine.GetDefaultCriteria();

        return config;
    }

    private void AddMapping(RockSpecificationConfig config, RockType rt, IntegrityLevel lvl, RockStructureType[] structures)
    {
        config.StructureMappings[$"{rt}_{lvl}"] = structures.Select(s => (int)s).ToList();
    }
}