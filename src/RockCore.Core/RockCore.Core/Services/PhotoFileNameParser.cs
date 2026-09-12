using System.Globalization;
using System.Text.RegularExpressions;
using RockCore.Core.Models;

namespace RockCore.Core.Services;

/// <summary>
/// 岩芯照片文件名解析器。支持的典型命名方式：
///   * 第1箱0m-9m.jpg / 第12箱89.50m-97.51m.jpg
///   * 0-9.3m.jpg / 103-108.jpg
///   * 兜底：文件名中出现的前两个数字视为 DepthStart / DepthEnd
/// </summary>
public static class PhotoFileNameParser
{
    private static readonly Regex BoxAndDepthRegex = new Regex(
        @"第\s*(?<box>\d+)\s*箱.*?(?<d1>\d+(?:\.\d+)?)\s*m\s*[-~至到—]\s*(?<d2>\d+(?:\.\d+)?)\s*m",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex PureDepthWithUnitRegex = new Regex(
        @"(?<d1>\d+(?:\.\d+)?)\s*m\s*[-~至到—]\s*(?<d2>\d+(?:\.\d+)?)\s*m",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex PureDepthRegex = new Regex(
        @"(?<d1>\d+(?:\.\d+)?)\s*[-~至到—]\s*(?<d2>\d+(?:\.\d+)?)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex BoxOnlyRegex = new Regex(
        @"第\s*(?<box>\d+)\s*箱",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// 解析单个文件名。文件名为纯文件名或完整路径都可以。
    /// </summary>
    public static PhotoParseResult Parse(string filePath)
    {
        var result = new PhotoParseResult
        {
            FileName = Path.GetFileName(filePath),
            FullPath = filePath
        };

        var name = Path.GetFileNameWithoutExtension(result.FileName) ?? string.Empty;

        var m = BoxAndDepthRegex.Match(name);
        if (m.Success)
        {
            result.BoxNumber = int.Parse(m.Groups["box"].Value, CultureInfo.InvariantCulture);
            result.DepthStart = double.Parse(m.Groups["d1"].Value, CultureInfo.InvariantCulture);
            result.DepthEnd = double.Parse(m.Groups["d2"].Value, CultureInfo.InvariantCulture);
            result.ParseSucceeded = true;
            result.ParseMessage = $"箱号: 第{result.BoxNumber}箱";
            NormalizeAndValidate(result);
            return result;
        }

        var withUnit = PureDepthWithUnitRegex.Match(name);
        if (withUnit.Success)
        {
            result.DepthStart = double.Parse(withUnit.Groups["d1"].Value, CultureInfo.InvariantCulture);
            result.DepthEnd = double.Parse(withUnit.Groups["d2"].Value, CultureInfo.InvariantCulture);
            result.BoxNumber = TryExtractBoxNumber(name);
            result.ParseSucceeded = true;
            result.ParseMessage = "深度范围 (带单位)";
            NormalizeAndValidate(result);
            return result;
        }

        var pure = PureDepthRegex.Match(name);
        if (pure.Success)
        {
            result.DepthStart = double.Parse(pure.Groups["d1"].Value, CultureInfo.InvariantCulture);
            result.DepthEnd = double.Parse(pure.Groups["d2"].Value, CultureInfo.InvariantCulture);
            result.BoxNumber = TryExtractBoxNumber(name);
            result.ParseSucceeded = true;
            result.ParseMessage = "深度范围 (纯数字)";
            NormalizeAndValidate(result);
            return result;
        }

        var numbers = Regex.Matches(name, @"\d+(?:\.\d+)?")
            .OfType<Match>()
            .Select(mc => double.Parse(mc.Value, CultureInfo.InvariantCulture))
            .ToList();

        if (numbers.Count >= 2)
        {
            numbers.Sort();
            result.DepthStart = numbers[0];
            result.DepthEnd = numbers[^1];
            result.BoxNumber = TryExtractBoxNumber(name);
            result.ParseSucceeded = true;
            result.ParseMessage = "兜底：文件名最大/最小数字";
            NormalizeAndValidate(result);
            return result;
        }

        result.ParseSucceeded = false;
        result.ParseMessage = "无法解析，请手动输入深度范围";
        return result;
    }

    private static int TryExtractBoxNumber(string name)
    {
        var m = BoxOnlyRegex.Match(name);
        return m.Success ? int.Parse(m.Groups["box"].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static void NormalizeAndValidate(PhotoParseResult result)
    {
        if (result.DepthStart > result.DepthEnd)
        {
            (result.DepthStart, result.DepthEnd) = (result.DepthEnd, result.DepthStart);
            result.ValidationWarning = "深度起点大于终点，已自动交换";
        }

        if (Math.Abs(result.DepthStart - result.DepthEnd) < 0.001)
        {
            result.ParseSucceeded = false;
            result.ParseMessage = "深度范围无效，起终点相同";
        }
    }
}
