using Godot;
using RpgSandbox.Sim.Api;
using RpgSandbox.Sim.Scenarios;

namespace RpgSandbox.Game;

/// <summary>Report feedback for the granary slice, derived from player-visible knowledge.</summary>
public partial class VillageMap
{
	private ActionId? _sliceReportAction;
	private ObservationId? _sliceReportObservation;
	private ActorId? _sliceReportRecipient;
	private GameTime _sliceReportStartedAt;

	private bool IsSliceTheftTopic(TopicView topic) => topic.Kind == "Tell"
		&& _view.Observations.Any(observation => observation.Id == topic.Observation
			&& observation.Kind == "Theft" && observation.Store == MappedVillageScenario.Ids.GranaryStore);

	private string SliceReportPrompt(PersonView person)
	{
		if (_view.Action?.Kind == "Report")
			return "Rapporto in corso: stai parlando con la guardia. La notizia non è ancora stata riferita.";
		if (_view.Observations.Any(o => o.Kind == "Theft"
			&& o.Store == MappedVillageScenario.Ids.GranaryStore && o.ToldTo.Contains(person.Id)))
			return "Furto riferito alla guardia. Osserva come reagisce.";
		if (person.Topics.Any(IsSliceTheftTopic))
			return "Hai notato il furto: scegli «Riferisci il furto» per raccontarlo alla guardia.";
		return "Non hai ancora una notizia del furto da riferire.";
	}

	private void RefreshSliceReportState()
	{
		if (!PlayableSliceMode || _sliceReportAction is not { } action || _view.Action?.Id == action)
			return;
		var observation = _sliceReportObservation;
		var recipient = _sliceReportRecipient;
		var startedAt = _sliceReportStartedAt;
		ClearSliceReport();
		if (recipient is { } guard && _view.Observations.Any(o => o.Id == observation
			&& o.ToldTo.Contains(guard)))
			ShowMessage("Rapporto concluso: hai riferito il furto alla guardia. Osserva come reagisce.");
		else if (_view.RecentEvents.Any(e => e.Kind == "ReportFailed"
			&& e.At.Since(startedAt).Ticks >= 0))
			ShowMessage("Rapporto non concluso: la guardia non era più vicina. Raggiungila e riprova.");
		else ShowMessage("Rapporto interrotto: il furto non è ancora stato riferito alla guardia.");
	}

	private void ClearSliceReport()
	{
		_sliceReportAction = null;
		_sliceReportObservation = null;
		_sliceReportRecipient = null;
	}
}
