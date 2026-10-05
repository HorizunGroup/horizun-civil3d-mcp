using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class SaveRevisionTests
{
    private static JsonObject Plan(RevisionClock clock) => new()
    {
        ["op"] = "save", ["path"] = "fixture.dwg", ["dbmod"] = 1,
        ["file_last_write_utc"] = "2026-10-01T12:00:00Z", ["drawing_revision"] = clock.Snapshot,
    };

    [Fact]
    public void Additional_edits_with_identical_dbmod_and_file_time_reject_save()
    {
        var clock = new RevisionClock();
        var store = new ConfirmationStore();
        var before = Plan(clock);
        var token = store.Issue("document:save", "drawing", "request", ConfirmationStore.PlanFingerprint(before)).Token;
        clock.Advance();
        var after = Plan(clock);
        Assert.Equal(before["dbmod"]!.ToJsonString(), after["dbmod"]!.ToJsonString());
        Assert.Equal(before["file_last_write_utc"]!.ToJsonString(), after["file_last_write_utc"]!.ToJsonString());
        Assert.Equal(ConfirmationState.StalePlan,
            store.Validate(token, "document:save", "drawing", "request", ConfirmationStore.PlanFingerprint(after)).State);
    }

    [Fact]
    public void An_unchanged_plan_can_be_applied_once()
    {
        var clock = new RevisionClock();
        var store = new ConfirmationStore();
        var fingerprint = ConfirmationStore.PlanFingerprint(Plan(clock));
        var token = store.Issue("document:save", "drawing", "request", fingerprint).Token;
        Assert.True(store.Validate(token, "document:save", "drawing", "request", ConfirmationStore.PlanFingerprint(Plan(clock))).Ok);
        Assert.Equal(ConfirmationState.AlreadyUsed, store.Validate(token, "document:save", "drawing", "request", fingerprint).State);
    }

    [Fact]
    public void Undo_or_reopening_cannot_restore_the_old_revision()
    {
        var clock = new RevisionClock();
        var before = clock.Snapshot;
        clock.Advance(); // Edit.
        var edited = clock.Snapshot;
        clock.Advance(); // Undo also emits a change.
        Assert.NotEqual(before, clock.Snapshot);
        Assert.NotEqual(edited, clock.Snapshot);
        Assert.NotEqual(before, new RevisionClock().Snapshot);
    }
}
