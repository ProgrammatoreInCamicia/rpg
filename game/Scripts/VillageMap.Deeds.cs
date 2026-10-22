using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>The player's account of deeds, using only what the Sim says they know.</summary>
public partial class VillageMap
{
	internal void PreviewDeedsForSmoke(IReadOnlyList<ObservationView> observations, PersonView listener)
	{
		_view = _view with { Observations = observations, PeopleHere = new[] { listener } };
		_talkingTo = listener.Id;
		_peopleKey = "";
		RefreshPeople();
		_peopleScroll.Visible = true;
		_journalOpen = true;
		RefreshJournal();
		PlaceTokens();
	}

	internal string JournalTextForSmoke => _journalText.Text;
	internal IReadOnlyList<string> TopicLabelsForSmoke => _people.GetChildren().OfType<Button>()
		.Select(b => b.Text).ToArray();

	private void BuildJournalUi(CanvasLayer ui)
	{
		_journalPanel = new PanelContainer { OffsetLeft = 900, OffsetRight = 1264,
			OffsetTop = PlayableSliceMode ? 165 : 16,
			OffsetBottom = PlayableSliceMode ? 550 : 690, Visible = false };
		_journalPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
			{ BgColor = new Color(0.10f, 0.11f, 0.13f, 0.96f) });
		ui.AddChild(_journalPanel);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		_journalPanel.AddChild(box);
		var header = new HBoxContainer();
		var title = new Label { Text = "Diario · ciò che sai", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		title.AddThemeFontSizeOverride("font_size", 20);
		header.AddChild(title);
		var close = new Button { Text = "Chiudi" };
		close.Pressed += ToggleJournal;
		header.AddChild(close);
		box.AddChild(header);
		_journalText = new RichTextLabel { BbcodeEnabled = false, ScrollFollowing = false,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		box.AddChild(_journalText);
	}

	private void ToggleJournal()
	{
		if (_view.Combat is not null || _view.Dead)
			return;
		_journalOpen = !_journalOpen;
		if (_journalOpen)
			_openingPanel.Visible = false;
		RefreshJournal();
		RefreshSliceGuidance();
	}

	private void RefreshJournal()
	{
		if (_journalPanel is null)
			return;
		_journalButton.Text = $"Diario ({_view.Observations.Count})";
		_journalButton.Disabled = _view.Combat is not null || _view.Dead;
		_journalPanel.Visible = _journalOpen && !_journalButton.Disabled;
		if (_journalPanel.Visible)
			_journalText.Text = _view.Observations.Count == 0 ? "Non hai ancora osservato o sentito fatti da raccontare."
				: string.Join("\n\n", _view.Observations.Reverse().Select(ObservationLine));
	}

	private string TopicKind(TopicView topic)
	{
		var observation = _view.Observations.FirstOrDefault(o => o.Id == topic.Observation);
		return observation?.Kind switch
		{
			"Theft" => "il furto",
			"Assault" when observation.Perceived == "Heard" => "i rumori di lotta",
			"Assault" => "lo scontro",
			"Killing" => "l'uccisione",
			_ => "il fatto",
		};
	}

	private string ObservationLine(ObservationView observation)
	{
		var title = observation.Kind switch
		{
			"Theft" when PlayableSliceMode && observation.Source is not null => "Furto raccontato",
			"Theft" when PlayableSliceMode && observation.Perceived == "Seen" => "Furto visto",
			"Theft" when PlayableSliceMode => "Furto udito",
			"Theft" => "Furto",
			"Assault" when observation.Perceived == "Heard" => "Rumori di lotta",
			"Assault" => "Scontro",
			"Killing" => "Uccisione",
			_ => observation.Kind,
		};
		var place = observation.Location is { } location
			? _view.Locations.FirstOrDefault(l => l.Id == location)?.Name ?? "luogo sconosciuto"
			: "all'aperto";
		var square = observation.Square is { } at
			? observation.Perceived == "Heard" ? $", circa {at}" : $", casella {at}"
			: "";
		var details = observation.Kind switch
		{
			"Theft" => $"{observation.CulpritName ?? "Ladro ignoto"}: {observation.Amount ?? 0} razioni da {observation.StoreName ?? "un deposito"}.",
			"Assault" when observation.Perceived == "Heard" => "Hai sentito la lotta, senza riconoscere chi combatteva.",
			"Assault" when observation.CulpritName is { } culprit =>
				$"{culprit} ha iniziato lo scontro{(observation.VictimName is { } victim ? $" contro {victim}" : "")}.",
			"Assault" when observation.InvolvedNames.Count > 0 =>
				$"Hai visto combattere {string.Join(", ", observation.InvolvedNames)}; non sai chi ha iniziato.",
			"Assault" => "Sai che c'è stato uno scontro, senza conoscere chi ha iniziato.",
			"Killing" => $"{observation.CulpritName ?? "Qualcuno"} ha ucciso {observation.VictimName ?? "qualcuno"}.",
			_ => "",
		};
		var source = observation.SourceName is { } name ? $"\nTe l'ha raccontato {name}." : "";
		var report = PlayableSliceMode && observation.Kind == "Theft"
			&& observation.Store == RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids.GranaryStore
			? observation.ToldTo.Contains(RpgSandbox.Sim.Scenarios.MappedVillageScenario.Ids.Guard)
				? "\nRapporto alla guardia: completato."
				: "\nRapporto alla guardia: non ancora completato."
			: "";
		return $"G{observation.ObservedAt.Day + 1} {observation.ObservedAt.Hour:00}:{observation.ObservedAt.Minute:00} · {title} · {place}{square}\n{details}{source}{report}";
	}
}
