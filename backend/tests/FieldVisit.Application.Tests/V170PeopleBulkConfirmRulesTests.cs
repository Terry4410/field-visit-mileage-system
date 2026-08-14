using FieldVisit.Application;
using Xunit;

namespace FieldVisit.Application.Tests;

public sealed class V170PeopleBulkConfirmRulesTests
{
    private static readonly DateTime Now =
        new(
            2026,
            8,
            14,
            12,
            0,
            0,
            DateTimeKind.Utc);

    [Fact]
    public void Rejects_non_previewed_batch()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkConfirmRules.Validate(
                    "Confirmed",
                    Now.AddHours(1),
                    0,
                    false,
                    false,
                    Now));
    }

    [Fact]
    public void Rejects_expired_batch()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkConfirmRules.Validate(
                    "Previewed",
                    Now.AddSeconds(-1),
                    0,
                    false,
                    false,
                    Now));
    }

    [Fact]
    public void Rejects_batch_with_preview_errors()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkConfirmRules.Validate(
                    "Previewed",
                    Now.AddHours(1),
                    1,
                    false,
                    false,
                    Now));
    }

    [Fact]
    public void Retroactive_batch_requires_second_confirmation()
    {
        Assert.Throws<InvalidOperationException>(
            () =>
                V170PeopleBulkConfirmRules.Validate(
                    "Previewed",
                    Now.AddHours(1),
                    0,
                    true,
                    false,
                    Now));
    }

    [Fact]
    public void Valid_retroactive_batch_can_be_confirmed()
    {
        V170PeopleBulkConfirmRules.Validate(
            "Previewed",
            Now.AddHours(1),
            0,
            true,
            true,
            Now);
    }
}
