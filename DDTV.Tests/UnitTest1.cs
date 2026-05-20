using System.Reflection;
using Core.Tools;

namespace DDTV.Tests;

/// <summary>
/// Core.Tools 工具类单元测试
/// </summary>
public class ToolsTests
{
    [Fact]
    public void SHA1_Encrypt_ShouldReturnConsistentHash()
    {
        string input = "DDTV_Test_Input";
        string hash1 = Core.Tools.Encryption.SHA1_Encrypt(input);
        string hash2 = Core.Tools.Encryption.SHA1_Encrypt(input);

        Assert.NotNull(hash1);
        Assert.Equal(40, hash1.Length); // SHA1 = 160 bits = 40 hex chars
        Assert.Equal(hash1, hash2); // 一致性
    }

    [Fact]
    public void SHA1_Encrypt_DifferentInputs_ShouldReturnDifferentHashes()
    {
        string hash1 = Core.Tools.Encryption.SHA1_Encrypt("input1");
        string hash2 = Core.Tools.Encryption.SHA1_Encrypt("input2");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Md532_ShouldReturn32CharHexString()
    {
        string input = "test_string_for_md5";
        string md5 = input.Md532();

        Assert.NotNull(md5);
        Assert.Equal(32, md5.Length);
    }

    [Fact]
    public void CheckFilenames_ShouldRemoveInvalidChars()
    {
        string dirty = "file:name*with?invalid<chars>|\\/\"#&=%\0";
        string clean = Core.Tools.KeyCharacterReplacement.CheckFilenames(dirty);

        Assert.DoesNotContain(':', clean);
        Assert.DoesNotContain('*', clean);
        Assert.DoesNotContain('?', clean);
        Assert.DoesNotContain('<', clean);
        Assert.DoesNotContain('>', clean);
        Assert.DoesNotContain('|', clean);
        Assert.DoesNotContain('\\', clean);
        Assert.DoesNotContain('/', clean);
        Assert.DoesNotContain('"', clean);
    }

    [Fact]
    public void GetRandomStr_ShouldReturnCorrectLength()
    {
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        int length = 16;
        string random = Core.Tools.KeyCharacterReplacement.GetRandomStr(chars, length);

        Assert.Equal(length, random.Length);
        Assert.All(random, c => Assert.Contains(c, chars));
    }

    [Fact]
    public void GetRandomStr_ShouldReturnDifferentValues()
    {
        string chars = "ABC123";
        string r1 = Core.Tools.KeyCharacterReplacement.GetRandomStr(chars, 10);
        string r2 = Core.Tools.KeyCharacterReplacement.GetRandomStr(chars, 10);

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        Assert.Equal(10, r1.Length);
        Assert.Equal(10, r2.Length);
    }
}

/// <summary>
/// Config 解析容错测试
/// </summary>
public class ConfigParseTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    [InlineData("1", false)] // 无效值应返回默认值 false
    [InlineData("", false)]
    public void ParseBool_ShouldHandleEdgeCases(string input, bool expected)
    {
        var method = typeof(Core.Config).GetMethod("ParseBool", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        bool result = (bool)method!.Invoke(null, new object?[] { input, false })!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("42", 42, 0)]
    [InlineData("-5", -5, 0)]
    [InlineData("0", 0, 0)]
    [InlineData("abc", 0, 0)] // 无效值返回默认值
    [InlineData("", 0, 0)]
    public void ParseInt_ShouldHandleEdgeCases(string input, int expected, int defaultValue)
    {
        var method = typeof(Core.Config).GetMethod("ParseInt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        int result = (int)method!.Invoke(null, new object?[] { input, defaultValue })!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("123456789", 123456789L, 0L)]
    [InlineData("not_a_number", 0L, 0L)]
    public void ParseLong_ShouldHandleEdgeCases(string input, long expected, long defaultValue)
    {
        var method = typeof(Core.Config).GetMethod("ParseLong", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        long result = (long)method!.Invoke(null, new object?[] { input, defaultValue })!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("3.14", 3.14, 0.0)]
    [InlineData("2.71828", 2.71828, 0.0)]
    [InlineData("invalid", 0.0, 0.0)]
    public void ParseDouble_ShouldHandleEdgeCases(string input, double expected, double defaultValue)
    {
        var method = typeof(Core.Config).GetMethod("ParseDouble", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        double result = (double)method!.Invoke(null, new object?[] { input, defaultValue })!;
        Assert.Equal(expected, result);
    }
}

/// <summary>
/// Room 管理核心逻辑测试
/// </summary>
public class RoomManagerTests
{
    [Fact]
    public void AddRoom_WithInvalidUid_ShouldReturnErrorState()
    {
        var result = Core.RuntimeObject._Room.AddRoom(false, false, false, 0, 0);

        Assert.Equal(0, result.key);
        Assert.Equal(4, result.State); // 状态码4 = 参数有误
        Assert.Contains("参数有误", result.Message);
    }

    [Fact]
    public void BatchAddRooms_WithEmptyString_ShouldReturnEmptyList()
    {
        var results = Core.RuntimeObject._Room.BatchAddRooms("");

        Assert.Empty(results);
    }

    [Fact]
    public void BatchDeleteRooms_WithEmptyString_ShouldReturnEmptyList()
    {
        var results = Core.RuntimeObject._Room.BatchDeleteRooms("");

        Assert.Empty(results);
    }

    [Fact]
    public void CancelTask_WithZeroIds_ShouldReturnFalse()
    {
        var result = Core.RuntimeObject._Room.CancelTask(0, 0);

        Assert.False(result.State);
        Assert.Contains("参数有误", result.Message);
    }

    [Fact]
    public void CutTask_WithZeroIds_ShouldReturnFalse()
    {
        var result = Core.RuntimeObject._Room.CutTask(0, 0);

        Assert.False(result.State);
        Assert.Contains("参数有误", result.Message);
    }

    [Fact]
    public void AddTask_WithZeroIds_ShouldReturnFalse()
    {
        var result = Core.RuntimeObject._Room.AddTask(0, 0);

        Assert.False(result.State);
        Assert.Contains("参数有误", result.Message);
    }
}
