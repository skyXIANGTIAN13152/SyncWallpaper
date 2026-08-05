namespace SyncWallpaper.Core;

/// <summary>Deterministic layered matcher. It never uses Windows' temporary monitor numbers.</summary>
public sealed class ProfileMatcher
{
    private const int RoleHintScore = 40;
    private readonly DisplayIdentityMatcher _identityMatcher = new();

    public MatchResult Match(IReadOnlyList<MonitorIdentity> monitors, IReadOnlyList<WallpaperProfile> profiles)
    {
        if (monitors.Count == 0 || profiles.Count == 0)
            return new MatchResult { Status = MatchStatus.NoMatch, Message = "没有可用的显示器配置" };

        var candidates = profiles.Where(p => p.Enabled
            && (p.ExpectedMonitorCount <= 0 ? p.Roles.Count : p.ExpectedMonitorCount) == monitors.Count
            && p.Roles.Count == monitors.Count).ToList();
        if (candidates.Count == 0)
            return new MatchResult { Status = MatchStatus.NoMatch, Message = $"当前有 {monitors.Count} 台显示器，没有对应配置" };

        var evaluations = candidates.Select(p => EvaluateProfile(monitors, p))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Profile.Priority)
            .ToList();
        var best = evaluations[0];
        var runnerUp = evaluations.Count > 1 ? evaluations[1].Score : 0;
        if (best.Score <= 0)
            return new MatchResult { Status = MatchStatus.NoMatch, Profile = best.Profile, Score = best.Score, RunnerUpScore = runnerUp, Message = "没有足够的身份依据" };

        // A close tie is unsafe. Identical models without serials are explicitly not guessed.
        var duplicateNoSerial = monitors.Where(m => !m.HasUsableSerial).GroupBy(m => m.ModelKey, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        var tied = runnerUp > 0 && best.Score - runnerUp <= 20 && best.Profile.Priority == evaluations[1].Profile.Priority;
        var assignmentTie = best.TopAssignments > 1;
        if (duplicateNoSerial && (assignmentTie || best.WeakOnly))
            return BuildAmbiguous(best, runnerUp, "存在同型号且无可靠序列号的显示器，需手动确认");
        if (tied || assignmentTie)
            return BuildAmbiguous(best, runnerUp, "多个显示器映射得分接近，需手动确认");

        var compatible = best.WeakOnly;
        var confidence = best.Score <= 0 ? 0 : Math.Clamp((int)(100.0 * best.Score / Math.Max(best.Score, 1000)), 0, 100);
        var canAutoApply = !compatible || (best.Profile.AllowCompatibleMatch && confidence >= Math.Max(0, best.Profile.MinimumConfidence));
        return new MatchResult
        {
            Status = compatible ? MatchStatus.Compatible : MatchStatus.Exact,
            Profile = best.Profile,
            RoleMatches = best.Mapping,
            Evidence = best.Evidence,
            Score = best.Score,
            RunnerUpScore = runnerUp,
            Message = compatible ? "已通过兼容指纹匹配" : "已通过稳定身份匹配",
            IdentityStatus = best.IdentityStatus,
            CanAutoApply = canAutoApply,
            ConflictingFields = best.ConflictingFields
        };
    }

    private static MatchResult BuildAmbiguous(Evaluation e, int runnerUp, string message) => new()
    {
        Status = MatchStatus.Ambiguous,
        Profile = e.Profile,
        RoleMatches = new(StringComparer.OrdinalIgnoreCase),
        Evidence = e.Evidence,
        Score = e.Score,
        RunnerUpScore = runnerUp,
        Message = message,
        IdentityStatus = DisplayIdentityMatchStatus.Ambiguous,
        CanAutoApply = false,
        ConflictingFields = e.ConflictingFields
    };

    private Evaluation EvaluateProfile(IReadOnlyList<MonitorIdentity> monitors, WallpaperProfile profile)
    {
        var roles = profile.Roles;
        var used = new bool[monitors.Count];
        var current = new Dictionary<string, MonitorIdentity>(StringComparer.OrdinalIgnoreCase);
        var best = new Evaluation(profile);
        var tieCount = 0;
        void Search(int roleIndex, int score, List<MatchEvidence> evidence)
        {
            if (roleIndex == roles.Count)
            {
                if (score > best.Score)
                {
                    best.Score = score;
                    best.Mapping = new(current, StringComparer.OrdinalIgnoreCase);
                    best.Evidence = evidence.ToList();
                    best.IdentityStatus = AggregateStatus(evidence.Select(x => x.IdentityStatus));
                    best.ConflictingFields = evidence.SelectMany(x => x.ConflictingFields).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    tieCount = 1;
                }
                else if (score == best.Score) tieCount++;
                return;
            }
            var role = roles[roleIndex];
            for (var i = 0; i < monitors.Count; i++)
            {
                if (used[i]) continue;
                var pair = Score(role, monitors[i]);
                if (pair.Score <= 0 && !profile.AllowCompatibleMatch) continue;
                used[i] = true; current[role.Role] = monitors[i];
                var nextEvidence = evidence.Append(new MatchEvidence
                {
                    Role = role.DisplayName,
                    Monitor = monitors[i].DisplayLabel,
                    Score = pair.Score,
                    Reason = pair.Reason,
                    IdentityStatus = pair.Status,
                    ConflictingFields = pair.Conflicts
                }).ToList();
                Search(roleIndex + 1, score + pair.Score, nextEvidence);
                current.Remove(role.Role); used[i] = false;
            }
        }
        Search(0, 0, new());
        best.TopAssignments = tieCount;
        best.WeakOnly = best.IdentityStatus is DisplayIdentityMatchStatus.ProbableMatch or DisplayIdentityMatchStatus.Unknown;
        return best;
    }

    private (int Score, string Reason, DisplayIdentityMatchStatus Status, IReadOnlyList<string> Conflicts) Score(MonitorRoleBinding role, MonitorIdentity actual)
    {
        var expected = role.Fingerprint;
        var result = _identityMatcher.Match(expected, actual);
        return (result.Score + (result.Score > 0 ? RoleHintScore : 0), result.Basis, result.Status, result.ConflictingFields);
    }

    private static DisplayIdentityMatchStatus AggregateStatus(IEnumerable<DisplayIdentityMatchStatus> statuses)
    {
        var values = statuses.ToArray();
        if (values.Length == 0 || values.Any(x => x == DisplayIdentityMatchStatus.Unknown)) return DisplayIdentityMatchStatus.Unknown;
        if (values.Any(x => x == DisplayIdentityMatchStatus.Ambiguous)) return DisplayIdentityMatchStatus.Ambiguous;
        if (values.Any(x => x == DisplayIdentityMatchStatus.ProbableMatch)) return DisplayIdentityMatchStatus.ProbableMatch;
        if (values.Any(x => x == DisplayIdentityMatchStatus.StrongMatch)) return DisplayIdentityMatchStatus.StrongMatch;
        return DisplayIdentityMatchStatus.ExactMatch;
    }

    private sealed class Evaluation
    {
        public Evaluation(WallpaperProfile profile) => Profile = profile;
        public WallpaperProfile Profile { get; }
        public int Score { get; set; }
        public int TopAssignments { get; set; }
        public bool WeakOnly { get; set; }
        public DisplayIdentityMatchStatus IdentityStatus { get; set; } = DisplayIdentityMatchStatus.Unknown;
        public IReadOnlyList<string> ConflictingFields { get; set; } = Array.Empty<string>();
        public Dictionary<string, MonitorIdentity> Mapping { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<MatchEvidence> Evidence { get; set; } = new();
    }
}
