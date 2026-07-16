using System.Text.Json;
using System.Text.Json.Serialization;
using IcedSubDL.Core.Models;
using IcedSubDL.Providers.Assrt;

namespace IcedSubDL.Tests;

public class AssrtJsonAndQueryTests
{
    [Fact]
    public void Deserialize_EmptySubsObject_YieldsEmptyList()
    {
        // Real ASSRT payload when keyword has zero hits
        const string json =
            """{"status":0,"sub":{"subs":{},"result":"succeed","keyword":"foo","action":"search"}}""";

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        // Use same converter factory via reflecting provider behavior: call BuildSearchQueries path separately;
        // serialize through public API by reusing AssrtProvider's expected shape via dynamic parse test.

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("sub").GetProperty("subs").ValueKind);

        // Empty object must not throw when converted by our flexible converter
        var list = FlexibleListTestHelper.DeserializeListOfDummy(
            """{"status":0,"sub":{"subs":{}}}""");
        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    [Fact]
    public void Deserialize_SubsArray_Works()
    {
        const string json =
            """{"status":0,"sub":{"subs":[{"id":1,"nativeName":"A","subtype":"Subrip(srt)","vote_score":0}]}}""";

        var list = FlexibleListTestHelper.DeserializeListOfDummy(json);
        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal(1, list![0].Id);
        Assert.Equal("A", list[0].NativeName);
    }

    [Fact]
    public void BuildSearchQueries_IncludesLatinAndBroaderForms()
    {
        var media = new MediaInfo
        {
            FilePath = @"E:\x\Michael Jackson-德国慕尼黑历史演唱会4K修复版.mp4",
            FileName = "Michael Jackson-德国慕尼黑历史演唱会4K修复版.mp4",
            FileNameWithoutExtension = "Michael Jackson-德国慕尼黑历史演唱会4K修复版",
            FileSizeBytes = 1,
            ParsedTitle = "Michael Jackson 德国慕尼黑历史演唱会4K修复版"
        };

        var queries = AssrtProvider.BuildSearchQueries(media).ToList();
        Assert.Contains(queries, q => q.IsFile && q.Query.Contains("Michael Jackson", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(queries, q => !q.IsFile && q.Query.Equals("Michael Jackson", StringComparison.OrdinalIgnoreCase));
        // Noise stripped should remove 4K/修复版
        Assert.Contains(queries, q => q.Query.Contains("Michael Jackson", StringComparison.OrdinalIgnoreCase) &&
                                      !q.Query.Contains("4K", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Michael Jackson-德国慕尼黑历史演唱会4K修复版", "Michael Jackson")]
    [InlineData("纯中文标题没有英文", "")]
    [InlineData("OnlyChinese标题", "OnlyChinese")]
    public void ExtractLatinPhrase(string input, string expected)
    {
        Assert.Equal(expected, AssrtProvider.ExtractLatinPhrase(input));
    }
}

/// <summary>
/// Mirrors AssrtProvider flexible list conversion for unit tests without making DTO types public.
/// </summary>
file static class FlexibleListTestHelper
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static List<DummySub>? DeserializeListOfDummy(string json)
    {
        var root = JsonSerializer.Deserialize<Wrapper>(json, Options);
        return root?.Sub?.Subs;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new FlexibleListConverterFactory());
        return options;
    }

    private sealed class Wrapper
    {
        public int Status { get; set; }
        public Block? Sub { get; set; }
    }

    private sealed class Block
    {
        public List<DummySub>? Subs { get; set; }
    }

    internal sealed class DummySub
    {
        public int Id { get; set; }
        public string? NativeName { get; set; }
    }

    private sealed class FlexibleListConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(List<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type elementType = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(FlexibleListConverter<>).MakeGenericType(elementType);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
    }

    private sealed class FlexibleListConverter<T> : JsonConverter<List<T>>
    {
        public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.StartArray:
                {
                    var list = new List<T>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        var item = JsonSerializer.Deserialize<T>(ref reader, options);
                        if (item is not null)
                            list.Add(item);
                    }

                    return list;
                }
                case JsonTokenType.StartObject:
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    if (!doc.RootElement.EnumerateObject().Any())
                        return [];
                    var single = doc.RootElement.Deserialize<T>(options);
                    return single is null ? [] : [single];
                }
                default:
                    throw new JsonException();
            }
        }

        public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }
}
