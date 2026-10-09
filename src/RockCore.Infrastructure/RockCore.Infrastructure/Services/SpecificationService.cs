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
        //   3) 定向迁移：仅当现存表与"内置默认表"逐行完全一致（说明用户从未改过）时，
        //      才替换为当前默认表。历史两版默认表都要覆盖：
        //        v1 —— 7 行、破碎行在末尾、组数列参与匹配；
        //        v2 —— 7 行、破碎行已置顶、组数列参与匹配。
        //      现默认表改为「只按间距分档」（组数列留 "—"，仅作地质描述），
        //      因为自动判级已不再把"回次内节理条数"当作组数代入。
        if (config.IntegrityLevelCriteria == null || config.IntegrityLevelCriteria.Count == 0)
        {
            config.IntegrityLevelCriteria = IntegrityCriteriaEngine.GetDefaultCriteria();
            changed = true;
        }
        else if (IsBuiltinDefaultCriteria(config.IntegrityLevelCriteria))
        {
            config.IntegrityLevelCriteria = IntegrityCriteriaEngine.GetDefaultCriteria();
            changed = true;
        }

        if (changed)
            SaveConfig(config);
    }

    /// <summary>
    /// 判断判定表是否仍是某个历史版本的<b>内置默认表</b>（用户从未修改）。
    /// 仅当逐行完全一致时返回 true，避免误伤用户自定义表。
    /// </summary>
    private static bool IsBuiltinDefaultCriteria(List<IntegrityLevelCriterion> criteria)
    {
        // v1：破碎行在末尾（更早版本）
        var legacyV1 = new (string Level, string J, string S)[]
        {
            ("Intact",           "1~2", ">95"),
            ("RelativelyIntact", "1~2", "50~95"),
            ("RelativelyIntact", "2~3", "30~50"),
            ("Poor",             "2~3", "10~30"),
            ("Poor",             "2~3", "≤10"),
            ("RelativelyBroken", ">3",  "≤10"),
            ("Broken",           "—",   "<2")
        };

        // v2：破碎行已置顶（上一版）
        var legacyV2 = new (string Level, string J, string S)[]
        {
            ("Broken",           "—",   "<2"),
            ("Intact",           "1~2", ">95"),
            ("RelativelyIntact", "1~2", "50~95"),
            ("RelativelyIntact", "2~3", "30~50"),
            ("Poor",             "2~3", "10~30"),
            ("Poor",             "2~3", "≤10"),
            ("RelativelyBroken", ">3",  "≤10")
        };

        return MatchesCriteriaTable(criteria, legacyV1) || MatchesCriteriaTable(criteria, legacyV2);
    }

    private static bool MatchesCriteriaTable(
        List<IntegrityLevelCriterion> criteria,
        (string Level, string J, string S)[] expected)
    {
        if (criteria.Count != expected.Length) return false;

        for (int i = 0; i < expected.Length; i++)
        {
            var c = criteria[i];
            if (!string.Equals(c.LevelKey, expected[i].Level, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals((c.JointSetCount ?? "").Trim(), expected[i].J, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals((c.JointSpacing ?? "").Trim(), expected[i].S, StringComparison.OrdinalIgnoreCase)) return false;
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