using Godot;
using RpgSandbox.Sim.Api;

namespace RpgSandbox.Game;

public partial class VillageMap
{
	// Only local presentation belongs here. The camera, UI and simulation outlive an area's render tree.
	private Node2D _mapRoot = null!;
	private SimulationSession? _presentedSession;
	private bool _presentedOnMap;

	private void BuildMapPresentation()
	{
		_mapRoot = new Node2D { Name = "MapPresentation" };
		AddChild(_mapRoot);
		_presentation = PresentationFor(_activeEntry, _view.Map!.Area);
		BuildMap();
		BuildR5Map();
		BuildCombatMap();
		BuildSliceAccessMarker();
		_presentedSession = _sim;
		_presentedOnMap = _view.Position is not null;
		SyncFigureFacing(_view.Facing);
	}

	private void EnsureMapPresentation()
	{
		var onMap = _view.Map is not null && _view.Position is not null;
		var rebuild = onMap && (_view.Map!.Area != _builtMap.Area || !ReferenceEquals(_presentedSession, _sim));
		if (rebuild || onMap != _presentedOnMap) ClearMapInteractions();
		if (rebuild)
		{
			ReleaseMapPresentation();
			BuildMapPresentation();
		}
		_presentedOnMap = onMap;
		// Map may still describe the origin during travel: hide every spatial layer, including overlays.
		_mapRoot.Visible = onMap;
	}

	private void ReleaseMapPresentation()
	{
		ClearBarrierActivity();
		RemoveChild(_mapRoot);
		_mapRoot.QueueFree();
		_lit.Clear();
		_edgeLit.Clear();
		_windowPanels.Clear();
		_doorPanels.Clear();
		_doorFloors.Clear();
		_doorOutlines.Clear();
		_tokens.Clear();
		_storeLabels.Clear();
		_groundMarks.Clear();
		_roofTiles.Clear();
		_roofBounds.Clear();
		_roofBlends.Clear();
		_roofRims.Clear();
		_interiorEdges.Clear();
		_occludingWalls.Clear();
		_interiorProps.Clear();
		_barrierMarks.Clear();
		_trapMarks.Clear();
		_trapMarksKey = "";
		_glow = new float[0, 0];
		_lightKey = "";
		_visionTexture = null;
		_visionFeatherKey = null;
		_shotVisuals.Clear();
		_combatVisualTracks.Clear();
		_seenCombatVisuals = _view.Combat?.Log.Count ?? _view.LastFight?.Count ?? 0;
		_observingLastFight = _view.Combat is null && _view.LastFight is not null;
		_attackVisualUntil.Clear();
		_attackVisualStarted.Clear();
		_slashVisualUntil.Clear();
		_hitVisualUntil.Clear();
		_paladinLiveModel = null;
		_npcLiveModels.Clear();
		_npcYaw.Clear();
		_npcWalkDistance.Clear();
		_figureFacing.Clear();
		_paladin3dWalkDistance = 0;
		_paladin3dDirectionTravel = Vector2.Zero;
		_sliceAccessMarker = null;
		_sliceAccessLabel = null;
	}

	private void ClearMapInteractions()
	{
		// These are UI intents aimed at the old area, never simulation actions or world knowledge.
		_queue.Clear();
		_approaching = null;
		_pendingBarrier = null;
		_pendingStore = null;
		_attackAfterWalking = null;
		_exitAfterWalking = null;
		_talkingTo = null;
		_selectedStore = null;
		_storeOpen = false;
		_storeSelectionSuppressed = false;
		_storeOptionsKey = "";
		_selectedTrap = null;
		_travelDestination = null;
		_selectedCombatTarget = null;
		_openingTarget = null;
		_openingConfirmedAttack = null;
		_hoveredCombatCell = null;
		_hoveredMove = null;
		_attackArmed = false;
		_spotPreviewAt = null;
		_spotPreviewFrom = null;
		_spotPreviewTime = -1;
		_loreTarget = null;
		_peopleKey = "";
		_inventoryKey = "";
		_smokeFocus = null;
		CancelCasting();
		CancelSliceWait("Attesa interrotta.");
		_personMenu.Hide();
		_creaturePanel.Hide();
		_barrierMenu.Hide();
		_trapMenu.Hide();
		_travelChoicePanel.Hide();
		_openingPanel.Hide();
		_openingConfirm.Hide();
		_inventoryPanel.Hide();
		_classPanel.Hide();
		_spellPanel.Hide();
		_attackOptionsPanel.Hide();
		_hover.Hide();
		_spotOverlay.Hide();
		ClearShotVisuals();
		ClearCombatOverlay();
	}
}
