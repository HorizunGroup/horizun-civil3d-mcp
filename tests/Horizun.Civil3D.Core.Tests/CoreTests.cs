using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class ContractTests
{
    [Fact]
    public void Hash_is_stable_and_hex()
    {
        // Pinned: a schema or effect change must be a deliberate joint server + plug-in release.
        Assert.Equal("e36ee390efbb5d34ee0fe86c", Contract.Hash);
        Assert.Matches("^[0-9a-f]{24}$", Contract.Hash);
    }

    [Fact]
    public void Every_tool_is_prefixed_unique_and_has_an_object_schema()
    {
        Assert.Equal(Contract.All.Count, Contract.All.Select(c => c.Name).Distinct().Count());
        foreach (var c in Contract.All)
        {
            Assert.StartsWith(Contract.Prefix, c.Name);
            Assert.Equal("object", c.InputSchema["type"]!.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(c.Description));
        }
        Assert.Equal(Contract.PluginCommands.Count(), Contract.PluginCommands.Distinct().Count());
    }

    [Fact]
    public void Save_is_full_write_and_info_is_read()
    {
        var doc = Contract.Find("horizun_c3d_document")!;
        Assert.Equal(ToolEffect.FullWrite, doc.EffectFor(new JsonObject { ["action"] = "save" }));
        Assert.Equal(ToolEffect.Read, doc.EffectFor(new JsonObject { ["action"] = "info" }));
        Assert.Equal(ToolEffect.FullWrite, doc.MaxEffect);
    }

    [Fact]
    public void Catalogue_stays_small()
    {
        // Model tool selection degrades with large catalogues: the product target is < 30 tools.
        Assert.True(Contract.All.Count < 30);
    }
}

public class SettingsTests
{
    [Fact]
    public void Missing_file_is_safe_write()
    {
        var s = Settings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        Assert.Equal(PermissionProfile.SafeWrite, s.Profile);
        Assert.False(s.FailedClosed);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"permission_profile\":\"god_mode\"}")]
    public void Bad_settings_fail_closed_to_read_only(string text)
    {
        var s = Settings.Parse(text);
        Assert.Equal(PermissionProfile.ReadOnly, s.Profile);
        Assert.True(s.FailedClosed);
    }

    [Fact]
    public void Profile_gates_effects()
    {
        var ro = Settings.Parse("{\"permission_profile\":\"read_only\"}");
        Assert.Null(ro.Refusal("horizun_c3d_query", ToolEffect.Read));
        Assert.Null(ro.Refusal("horizun_c3d_target", ToolEffect.HostState));
        Assert.NotNull(ro.Refusal("x", ToolEffect.SafeWrite));

        var sw = Settings.Parse("{}");
        Assert.Null(sw.Refusal("x", ToolEffect.SafeWrite));
        Assert.NotNull(sw.Refusal("x", ToolEffect.FullWrite));

        var fw = Settings.Parse("{\"permission_profile\":\"full_write\"}");
        Assert.Null(fw.Refusal("x", ToolEffect.FullWrite));
        Assert.NotNull(fw.Refusal("x", ToolEffect.UnsafeCode));
    }

    [Fact]
    public void Unsafe_code_needs_both_keys()
    {
        Assert.NotNull(Settings.Parse("{\"permission_profile\":\"unsafe_code\"}").Refusal("x", ToolEffect.UnsafeCode));
        Assert.Null(Settings.Parse("{\"permission_profile\":\"unsafe_code\",\"enable_execute_csharp\":true}").Refusal("x", ToolEffect.UnsafeCode));
    }

    [Fact]
    public void Denied_wins_and_pause_leaves_only_health()
    {
        var s = Settings.Parse("{\"permission_profile\":\"full_write\",\"denied_tools\":[\"horizun_c3d_query\"]}");
        Assert.NotNull(s.Refusal("horizun_c3d_query", ToolEffect.Read));

        var p = Settings.Parse("{\"paused\":true}");
        Assert.Null(p.Refusal("horizun_c3d_health", ToolEffect.Read));
        Assert.NotNull(p.Refusal("horizun_c3d_query", ToolEffect.Read));
    }

    [Fact]
    public void Allowlist_restricts_but_keeps_health()
    {
        var s = Settings.Parse("{\"allowed_tools\":[\"horizun_c3d_query\"]}");
        Assert.Null(s.Refusal("horizun_c3d_query", ToolEffect.Read));
        Assert.Null(s.Refusal("horizun_c3d_health", ToolEffect.Read));
        Assert.NotNull(s.Refusal("horizun_c3d_styles", ToolEffect.Read));
    }

    [Theory]
    [InlineData("{\"permission_profile\":\"full_write\",\"denied_tools\":[\"horizun_c3d_Cleanup\"]}")]
    [InlineData("{\"permission_profile\":\"full_write\",\"denied_tools\":[\"horizun_c3d_cleanups\"]}")]
    [InlineData("{\"allowed_tools\":[\"horizun_c3d_qery\"]}")]
    public void Misspelled_tool_lists_fail_closed(string json)
    {
        var s = Settings.Parse(json);
        Assert.True(s.FailedClosed);
        Assert.Equal(PermissionProfile.ReadOnly, s.Profile);
        Assert.NotNull(s.Refusal("horizun_c3d_cleanup", ToolEffect.FullWrite));
    }
}

public class RequestGateTests
{
    [Fact]
    public void Fifo_and_exactly_once()
    {
        var g = new RequestGate(4);
        var a = g.Begin("a", "health", new JsonObject(), out _)!;
        var b = g.Begin("b", "query", new JsonObject(), out _)!;
        Assert.Same(a, g.Take());
        Assert.Null(g.Take()); // one in flight at a time
        g.Complete(a);
        Assert.Same(b, g.Take());
        g.Complete(b);
        Assert.Null(g.Take());
        Assert.True(a.IsDone && b.IsDone);
    }

    [Fact]
    public void Full_queue_refuses_without_queueing()
    {
        var g = new RequestGate(1);
        Assert.NotNull(g.Begin("a", "x", new JsonObject(), out _));
        Assert.Null(g.Begin("b", "x", new JsonObject(), out var why));
        Assert.Contains("Nothing was queued", why);
        Assert.Equal(1, g.PendingCount);
    }

    [Fact]
    public void Cancel_only_claims_work_that_never_started()
    {
        var g = new RequestGate();
        var a = g.Begin("a", "x", new JsonObject(), out _)!;
        var b = g.Begin("b", "x", new JsonObject(), out _)!;
        Assert.Same(a, g.Take());
        Assert.False(g.CancelQueued("a", out var d1));
        Assert.Equal("already_running", d1);
        Assert.True(g.CancelQueued("b", out var d2));
        Assert.Equal("cancelled_before_start", d2);
        Assert.True(b.IsDone);
        Assert.Contains("NEVER STARTED", b.Result!.Error);
        Assert.False(g.CancelQueued("zzz", out var d3));
        Assert.Equal("not_found_or_finished", d3);
    }

    [Fact]
    public void Abandoned_entry_never_starts()
    {
        var g = new RequestGate();
        var a = g.Begin("a", "x", new JsonObject(), out _)!;
        g.Abandon(a);
        Assert.True(a.CancelledBeforeStart);
        Assert.Null(g.Take());
    }

    [Fact]
    public void Shutdown_wakes_waiters()
    {
        var g = new RequestGate();
        var a = g.Begin("a", "x", new JsonObject(), out _)!;
        Assert.Equal(1, g.FailQueued("closing"));
        Assert.True(a.Wait(0));
        Assert.False(a.Result!.Success);
    }
}

public class ConfirmationTests
{
    private static readonly JsonObject Req = new() { ["action"] = "save", ["target_document"] = "a.dwg" };

    [Fact]
    public void Token_is_single_use_and_ignores_non_plan_fields()
    {
        var store = new ConfirmationStore();
        var (token, _) = store.Issue("doc:save", "k", ConfirmationStore.RequestHash(Req), "p1");
        var apply = (JsonObject)Req.DeepClone();
        apply["dry_run"] = false;
        apply["confirmation_token"] = token;
        Assert.Equal(ConfirmationStore.RequestHash(Req), ConfirmationStore.RequestHash(apply));
        Assert.True(store.Validate(token, "doc:save", "k", ConfirmationStore.RequestHash(apply), "p1").Ok);
        Assert.Equal(ConfirmationState.AlreadyUsed, store.Validate(token, "doc:save", "k", ConfirmationStore.RequestHash(apply), "p1").State);
    }

    [Fact]
    public void Replay_after_another_dry_run_is_still_reported_as_already_used()
    {
        var now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
        var store = new ConfirmationStore(() => now);
        var h = ConfirmationStore.RequestHash(Req);
        var (token, _) = store.Issue("doc:save", "k", h, "p1");
        Assert.True(store.Validate(token, "doc:save", "k", h, "p1").Ok);
        store.Issue("doc:save", "k", h, "p1");
        Assert.Equal(1, store.OutstandingCount);
        Assert.Equal(ConfirmationState.AlreadyUsed, store.Validate(token, "doc:save", "k", h, "p1").State);
        now = now.AddMinutes(11);
        store.Issue("doc:save", "k", h, "p1");
        Assert.Equal(ConfirmationState.Unknown, store.Validate(token, "doc:save", "k", h, "p1").State);
    }

    [Fact]
    public void Each_binding_is_named_when_it_breaks()
    {
        var store = new ConfirmationStore();
        var h = ConfirmationStore.RequestHash(Req);
        string T() => store.Issue("doc:save", "k", h, "p1").Token;
        Assert.Equal(ConfirmationState.Missing, store.Validate(null, "doc:save", "k", h, "p1").State);
        Assert.Equal(ConfirmationState.Unknown, store.Validate("hz-nope", "doc:save", "k", h, "p1").State);
        Assert.Equal(ConfirmationState.WrongOperation, store.Validate(T(), "other", "k", h, "p1").State);
        Assert.Equal(ConfirmationState.DocumentChanged, store.Validate(T(), "doc:save", "other", h, "p1").State);
        Assert.Equal(ConfirmationState.RequestChanged, store.Validate(T(), "doc:save", "k", "different", "p1").State);
        Assert.Equal(ConfirmationState.StalePlan, store.Validate(T(), "doc:save", "k", h, "p2").State);
    }

    [Fact]
    public void Tokens_expire()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var store = new ConfirmationStore(() => now);
        var (token, _) = store.Issue("op", "k", "h", null);
        now = now.AddMinutes(11);
        Assert.Equal(ConfirmationState.Expired, store.Validate(token, "op", "k", "h", null).State);
    }

    [Fact]
    public void Array_order_is_part_of_the_plan()
    {
        var a = new JsonObject { ["names"] = new JsonArray("A", "B") };
        var b = new JsonObject { ["names"] = new JsonArray("B", "A") };
        Assert.NotEqual(ConfirmationStore.RequestHash(a), ConfirmationStore.RequestHash(b));
        var c = new JsonObject { ["y"] = 1, ["x"] = 2 };
        var d = new JsonObject { ["x"] = 2, ["y"] = 1 };
        Assert.Equal(ConfirmationStore.RequestHash(c), ConfirmationStore.RequestHash(d));
    }
}

public class VerificationTests
{
    [Fact]
    public void Nothing_checked_is_never_a_pass()
    {
        var v = new VerificationSet();
        Assert.Equal("unverified", v.Status);
        Assert.False(v.AllVerified);
    }

    [Fact]
    public void Status_reflects_checks()
    {
        var v = new VerificationSet();
        v.Text("name", "EG", "eg");
        Assert.Equal("match", v.Status);
        v.Number("elev", 10, 10.5, 0.01);
        Assert.Equal("partial", v.Status);
        var m = new VerificationSet();
        m.Number("x", double.NaN, 1, 1);
        Assert.Equal("mismatch", m.Status);
        Assert.Contains("UNMEASURED", m.ToJson().ToJsonString());
    }

    [Fact]
    public void Reconcile_reports_difference()
    {
        var r = Reconcile.Compare("cut", 1371.32, "civil3d", 1370.11, "sampled", 0.5);
        Assert.True(r["agree"]!.GetValue<bool>());
        Assert.False(Reconcile.Compare("cut", 100, "a", 90, "b", 0.5)["agree"]!.GetValue<bool>());
    }
}

public class HzTests
{
    [Theory]
    [InlineData("PRD_TOR_1_VOL", "PRD_*_VOL", true)]
    [InlineData("PRD_TOR_1_VOL", "prd_tor_?_vol", true)]
    [InlineData("EG", "FG", false)]
    [InlineData("anything", null, true)]
    [InlineData("", "*", true)]
    [InlineData("", "?", false)]
    [InlineData("abc", "a*c*", true)]
    [InlineData("abc", "*b", false)]
    [InlineData("aXbXc", "a*b*c", true)]
    [InlineData("mississippi", "m*iss*pi", true)]
    [InlineData("mississippi", "m*iss*px", false)]
    [InlineData("C-ROAD", "c-road", true)]
    public void Wildcards(string text, string? pattern, bool expected) => Assert.Equal(expected, Hz.Like(text, pattern));

    [Fact]
    public void Wildcards_do_not_backtrack_exponentially()
    {
        var text = string.Concat(Enumerable.Repeat("-A", 200));
        var pattern = string.Concat(Enumerable.Repeat("*-", 30)) + "Z";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(Hz.Like(text, pattern));
        Assert.True(sw.ElapsedMilliseconds < 1000, "Hz.Like took " + sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void Finite_never_turns_nan_into_zero()
    {
        Assert.Null(Hz.Finite(double.NaN));
        Assert.Null(Hz.Finite(double.PositiveInfinity));
        Assert.Equal(1.5, Hz.Finite(1.5)!.GetValue<double>());
    }

    [Fact]
    public void Secret_comparison()
    {
        Assert.True(Hz.SecretEquals("abc", "abc"));
        Assert.False(Hz.SecretEquals("abc", "abd"));
        Assert.False(Hz.SecretEquals(null, "abc"));
    }
}

public class DiscoveryTests
{
    [Theory]
    [InlineData("civil3d-2025-1234.json", true)]
    [InlineData("civil3d-25-1234.json", false)]
    [InlineData("revit-2025-1234.json", false)]
    [InlineData("civil3d-2025-1234.json.tmp-x", false)]
    [InlineData("civil3d-2025-12a4.json", false)]
    public void Only_exact_names_are_discovery_files(string name, bool ok) => Assert.Equal(ok, Discovery.IsDiscoveryFileName(name));

    [Fact]
    public void Roundtrip_and_sweep_of_dead_process()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hzc3d-disc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var rec = new DiscoveryRecord
            {
                Year = 2025, Pid = 999999, PipeName = "p", AuthToken = "t", StartedUtc = DateTime.UtcNow,
                ProtocolVersion = Contract.ProtocolVersion, ContractHash = Contract.Hash, PluginVersion = "0.1.0",
                Commands = new[] { "health" },
            };
            Discovery.Write(rec, dir);
            var back = Discovery.ReadAll(dir).Single();
            Assert.Equal(2025, back.Year);
            Assert.Equal("t", back.AuthToken);
            Assert.Equal(new[] { "health" }, back.Commands);
            Assert.False(Discovery.IsAlive(back)); // pid 999999 is not acad.exe
            Assert.Equal(1, Discovery.SweepStale(dir));
            Assert.Empty(Discovery.ReadAll(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

public class VerificationNoteTests
{
    [Fact]
    public void Failure_note_is_not_shown_on_a_passing_check()
    {
        var v = new VerificationSet();
        v.Check("written", "a", "a", true, "this would be a lie if shown");
        v.Check("size", "> 0", 0, false, "empty file");
        var checks = (JsonArray)v.ToJson("file on disk")["checks"]!;
        Assert.Null(checks[0]!["note"]);
        Assert.Equal("empty file", checks[1]!["note"]!.GetValue<string>());
        Assert.Equal("file on disk", v.ToJson("file on disk")["how"]!.GetValue<string>());
    }
}

/// <summary>
/// Live finding (Civil 3D 2025): acad.exe runs with reflection-based JSON serialization disabled.
/// This test project's runtimeconfig.template.json disables it too, so the whole suite runs under
/// the host's condition; these tests pin the exact failure that broke health live.
/// </summary>
public class HostJsonConditionTests
{
    [Fact]
    public void Suite_runs_with_reflection_serialization_disabled_like_acad()
    {
        Assert.True(AppContext.TryGetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", out var on));
        Assert.False(on);
    }

    [Fact]
    public void Raw_generic_Add_is_unsafe_in_the_host()
    {
        // JsonArray.Add<string>("x") needs reflection metadata: this is what broke health in acad.exe.
        Assert.ThrowsAny<Exception>(() => new JsonObject { ["a"] = new JsonArray() }["a"]!.AsArray().Add("view"));
    }

    [Fact]
    public void Explicit_JsonValue_Add_serializes_in_the_host()
    {
        var o = new JsonObject { ["kinds"] = new JsonArray() };
        ((JsonArray)o["kinds"]!).Add(JsonValue.Create("view"));
        ((JsonArray)o["kinds"]!).Add(JsonValue.Create(16));
        var reply = Wire.Reply("1", CommandResult.Ok(new JsonObject { ["document"] = new JsonObject { ["unsaved"] = o } }));
        Assert.Contains("\"view\"", reply.ToJsonString(Hz.Indented));
        Assert.Contains("\"view\"", Hz.Canonical(reply));
        Assert.NotEmpty(ConfirmationStore.PlanFingerprint(reply));
    }
}
