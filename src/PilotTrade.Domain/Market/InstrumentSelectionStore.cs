namespace PilotTrade.Domain.Market;

using PilotTrade.Domain;

// Durable record of the owner's last instrument choice per account and environment.
public interface IInstrumentSelectionStore
{
    IReadOnlyList<InstrumentSelection> LoadAll();

    // The highest selection version stored anywhere: selections, decisions and AI analyses.
    int HighestStoredVersion();

    void Save(InstrumentSelection selection);
}
