using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

/// <summary>Names only conditions the player can know from the Sim's view.</summary>
public partial class VillageMap
{
	private Label _conditionsLabel = null!;

	private void BuildConditionsUi(VBoxContainer box)
	{
		_conditionsLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		box.AddChild(_conditionsLabel);
	}

	private void RefreshConditionsUi()
	{
		var conditions = _view.Conditions.Select(c => ConditionName(c.Kind)).ToArray();
		_conditionsLabel.Visible = conditions.Length > 0 || _view.Exhaustion > 0;
		_conditionsLabel.Text = (conditions.Length > 0 ? "Condizioni: " + string.Join(", ", conditions) : "")
			+ (_view.Exhaustion > 0 ? $"{(conditions.Length > 0 ? "\n" : "")}Sfinimento: {_view.Exhaustion}/6" : "");
	}

	private static string ConditionName(ConditionKind kind) => kind switch
	{
		ConditionKind.Blinded => "Accecato",
		ConditionKind.Charmed => "Affascinato",
		ConditionKind.Deafened => "Assordato",
		ConditionKind.Frightened => "Spaventato",
		ConditionKind.Grappled => "Afferrato",
		ConditionKind.Incapacitated => "Incapacitato",
		ConditionKind.Invisible => "Invisibile",
		ConditionKind.Paralyzed => "Paralizzato",
		ConditionKind.Poisoned => "Avvelenato",
		ConditionKind.Prone => "Prono",
		ConditionKind.Restrained => "Trattenuto",
		ConditionKind.Stunned => "Stordito",
		_ => kind.ToString(),
	};

	private static string ConditionsText(IReadOnlyList<ConditionView> conditions) =>
		conditions.Count == 0 ? "" : " · " + string.Join(", ", conditions.Select(c => ConditionName(c.Kind)));

	internal string ConditionsTextForSmoke => _conditionsLabel.Text;
	internal string FigureLabelForSmoke(ActorId actor) => _tokens[actor].GetNode<Label>("Name").Text;
	internal void PreviewConditionsForSmoke(IReadOnlyList<ConditionView> own, int exhaustion,
		ActorId visibleActor, IReadOnlyList<ConditionView> visible)
	{
		_view = _view with { Conditions = own, Exhaustion = exhaustion,
			VisibleActors = _view.VisibleActors.Select(a => a.Id == visibleActor ? a with { Conditions = visible } : a).ToArray() };
		RefreshConditionsUi();
		PlaceTokens();
	}
}
