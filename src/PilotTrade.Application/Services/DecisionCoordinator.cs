namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.AI;
using PilotTrade.Domain.Decision;
using PilotTrade.Domain.Market;

public sealed class DecisionCoordinator : IDecisionCoordinator
{
    public StrategyDecision Decide(DecisionCoordinatorInput input)
    {
        var now = DateTimeOffset.UtcNow;
        var snap = input.Snapshot;
        var checks = new List<string>();
        var rejection = (string?)null;

        // Staleness: snapshot age
        if (now - snap.AsOfUtc > TimeSpan.FromMinutes(5)) { rejection = "Snapshot stale"; }

        var claude = input.Claude;
        var jev = input.Jev;

        if (claude is null) { rejection ??= "Claude missing"; }
        else
        {
            if (claude.SnapshotId != snap.SnapshotId) { rejection ??= "Claude snapshot mismatch"; }
            if (claude.SelectionVersion != snap.SelectionVersion) { rejection ??= "Claude selection version mismatch"; }
            if (claude.Environment != snap.Environment) { rejection ??= "Claude environment mismatch"; }
            if (claude.ValidUntilUtc < now) { rejection ??= "Claude signal expired"; }
        }
        if (jev is null) { rejection ??= "JEV missing"; }
        else
        {
            if (jev.SnapshotId != snap.SnapshotId) { rejection ??= "JEV snapshot mismatch"; }
        }

        // Determine candidate
        DecisionCandidate candidate;
        if (rejection is not null)
        {
            candidate = DecisionCandidate.NoTrade;
        }
        else
        {
            var cSignal = claude!.Signal;
            // JEV directional classification: look for a question named "direction" choice
            var jevDirection = jev!.Answers.FirstOrDefault(a => a.QuestionName.Equals("direction", StringComparison.OrdinalIgnoreCase))?.Choice;
            var jevCandidate = jevDirection is not null && Enum.TryParse<DecisionCandidate>(jevDirection, true, out var jc) ? jc : DecisionCandidate.NoTrade;

            if (cSignal != jevCandidate)
            {
                // disagreement -> NO_TRADE unless both NO_TRADE/HOLD? default no trade
                if (cSignal == DecisionCandidate.NoTrade || jevCandidate == DecisionCandidate.NoTrade)
                    candidate = DecisionCandidate.NoTrade;
                else
                    candidate = DecisionCandidate.NoTrade; // conservative
                checks.Add("AI disagreement -> NO_TRADE");
            }
            else
            {
                candidate = cSignal;
            }
        }

        if (candidate != DecisionCandidate.NoTrade)
        {
            checks.Add($"Candidate {candidate} from aligned AI");
        }
        else
        {
            checks.Add("NO_TRADE");
        }

        var explanation = rejection is not null
            ? $"Rejected: {rejection}"
            : candidate == DecisionCandidate.NoTrade
                ? "No trade: conditions not met or AI disagreement"
                : $"{candidate} candidate accepted by coordinator pending risk";

        return new StrategyDecision(
            $"dec_{snap.SnapshotId}", snap.SnapshotId, snap.AccountId, snap.Environment, snap.Epic, snap.SelectionVersion,
            input.StrategyId, input.StrategyVersion, candidate, explanation, checks, now,
            claude?.AnalysisId, jev?.EvaluationId, rejection);
    }
}