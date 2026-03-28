using Xunit;

namespace CelDotNet.EntityFrameworkCore.Tests;

/// <summary>
/// Unit tests for <see cref="CelExpressionTranslator"/>.
/// Verifies that non-EF-translatable expression nodes are correctly rewritten.
/// </summary>
public class CelExpressionTranslatorTests
{
    #region Test Models

    public class Event
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset StartTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    #endregion

    #region Timestamp Member Rewrites

    [Fact]
    public void Translate_TimestampGetFullYear_RewritesToPropertyAccess()
    {
        var expr = CelExpression.Parse("StartTime.getFullYear() == 2023")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        Assert.True(compiled(new Event
        {
            StartTime = new DateTimeOffset(2023, 6, 15, 0, 0, 0, TimeSpan.Zero)
        }));
        Assert.False(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
        }));
    }

    [Fact]
    public void Translate_TimestampGetMonth_RewritesToZeroBasedProperty()
    {
        // CEL months are 0-based: January = 0
        var expr = CelExpression.Parse("StartTime.getMonth() == 0")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        // January (Month=1) should map to 0
        Assert.True(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)
        }));
        // June (Month=6) should map to 5
        Assert.False(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero)
        }));
    }

    [Fact]
    public void Translate_TimestampGetDayOfMonth_RewritesToZeroBasedProperty()
    {
        // CEL days are 0-based
        var expr = CelExpression.Parse("StartTime.getDayOfMonth() == 14")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        // Day 15 → 14 (0-based)
        Assert.True(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero)
        }));
    }

    [Fact]
    public void Translate_TimestampGetHours_RewritesToProperty()
    {
        var expr = CelExpression.Parse("StartTime.getHours() == 14")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        Assert.True(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 15, 14, 30, 0, TimeSpan.Zero)
        }));
        Assert.False(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero)
        }));
    }

    [Fact]
    public void Translate_TimestampGetDayOfWeek_RewritesToProperty()
    {
        var expr = CelExpression.Parse("StartTime.getDayOfWeek() == 1")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        // Monday = 1
        Assert.True(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero) // Monday
        }));
    }

    [Fact]
    public void Translate_TimestampGetMinutes_RewritesToProperty()
    {
        var expr = CelExpression.Parse("StartTime.getMinutes() == 30")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        Assert.True(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 15, 14, 30, 0, TimeSpan.Zero)
        }));
    }

    [Fact]
    public void Translate_TimestampGetSeconds_RewritesToProperty()
    {
        var expr = CelExpression.Parse("StartTime.getSeconds() == 45")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        Assert.True(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 15, 14, 30, 45, TimeSpan.Zero)
        }));
    }

    #endregion

    #region Timestamp/Duration Constructor Rewrites

    [Fact]
    public void Translate_TimestampConstructorWithConstant_EvaluatesToConstant()
    {
        var expr = CelExpression.Parse("StartTime > timestamp('2023-01-01T00:00:00Z')")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        Assert.True(compiled(new Event
        {
            StartTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
        }));
        Assert.False(compiled(new Event
        {
            StartTime = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero)
        }));
    }

    [Fact]
    public void Translate_DurationConstructorWithConstant_EvaluatesToConstant()
    {
        var expr = CelExpression.Parse("Duration > duration('3600s')")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        Assert.True(compiled(new Event { Duration = TimeSpan.FromHours(2) }));
        Assert.False(compiled(new Event { Duration = TimeSpan.FromMinutes(30) }));
    }

    #endregion

    #region Passthrough (Already Translatable)

    [Fact]
    public void Translate_SimpleComparison_PassesThrough()
    {
        var expr = CelExpression.Parse("Name == 'test'")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        Assert.True(compiled(new Event { Name = "test" }));
        Assert.False(compiled(new Event { Name = "other" }));
    }

    [Fact]
    public void Translate_StringContains_PassesThrough()
    {
        var expr = CelExpression.Parse("Name.contains('es')")
            .ToExpression<Event>();

        var translated = CelExpressionTranslator.Translate(expr);
        var compiled = translated.Compile();

        Assert.True(compiled(new Event { Name = "test" }));
        Assert.False(compiled(new Event { Name = "other" }));
    }

    #endregion
}
