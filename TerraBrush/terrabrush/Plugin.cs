using Godot;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TerraBrush;

public partial class Plugin : Node3D {
	private static Plugin instance;
	public static Plugin Instance {
		get => instance;
	}

	private CustomUndoRedo _undoRedo;
	private CustomUndoRedo undoRedo {
		get {
			if(_undoRedo == null) {
				_undoRedo = new CustomUndoRedo();
			}
			return _undoRedo;
		}
	}

	private const float UpdateDelay = 0.005f;
	private const int ToolInfoOffset = 20;

	private TerrainControlDock _terrainControlDock;
	private PackedScene _terrainControlDockPrefab;
	private PackedScene _toolsPieMenuPrefab;
	private PackedScene _customContentPieMenuPrefab;
	private BrushDecal _brushDecal;
	private TerraBrush _currentTerraBrushNode;
	private ToolInfo _toolInfo;
	private bool _isMousePressed;
	private Vector3 _mouseHitPosition;
	private float _updateTime = 0;
	//private EditorUndoRedoManager _undoRedo;
	private bool _preventInitialDo = false;
	private Control _overlaySelector = null;
	private Button _updateTerrainSettingsButton = null;
	private CheckBox _autoAddZonesCheckbox = null;
	private Control _uiParent;

	private void CreateCustomSetting(string name, Variant defaultValue, Variant.Type type, PropertyHint hint = PropertyHint.None, string hintString = null) {
		if (ProjectSettings.HasSetting(name)) {
			return;
		}

		var propertyInfo = new Godot.Collections.Dictionary() {
			{"name", name},
			{"type", (int)type},
			{"hint", (int)hint},
			{"hint_string", hintString}
		};

		ProjectSettings.SetSetting(name, defaultValue);
		ProjectSettings.AddPropertyInfo(propertyInfo);
		ProjectSettings.SetInitialValue(name, defaultValue);
	}

	public override void _EnterTree() {
		instance = this;

		var keybindManager = new KeybindManager();
		var script = GD.Load<Script>("res://TerraBrush/terrabrush/TerraBrush.cs");
		var icon = GD.Load<Texture2D>("res://TerraBrush/terrabrush/icon.png");

		//AddCustomType("TerraBrush", "Node3D", script, icon);
		CreateCustomSetting(SettingContants.DecalColor, Colors.Red, Variant.Type.Color);
		CreateCustomSetting(SettingContants.CustomBrushesFolder, "res://TerraBrush_CustomBrushes", Variant.Type.String);
		CreateCustomSetting(SettingContants.SculptingMultiplier, 10, Variant.Type.Int);
		//AddInspectorPlugin(new ButtonInspectorPlugin());

		_terrainControlDockPrefab = ResourceLoader.Load<PackedScene>("res://TerraBrush/terrabrush/Components/TerrainControlDock.tscn");
		_toolsPieMenuPrefab = ResourceLoader.Load<PackedScene>("res://TerraBrush/terrabrush/Components/ToolsPieMenu.tscn");
		_customContentPieMenuPrefab = ResourceLoader.Load<PackedScene>("res://TerraBrush/terrabrush/Components/CustomContentPieMenu.tscn");

		keybindManager.RegisterInputMap();
		//keybindManager.LoadEditorSettings();
		AddToolMenuItem("TerraBrush Key bindings", Callable.From(HandleKeyBindings));
	}

	private void AddInspectorPlugin(Control inspectorPlugin) {
		GD.Print("Attempting to add an inspector plugin: ", inspectorPlugin);
	}

	private void RemoveInspectorPlugin(Control inspectorPlugin) { }

	private void AddControlToDock(EditorPlugin.DockSlot slot, Control control) {
		if(_uiParent == null) {
			GD.PrintErr("UIParent isn't set!");
			return;
		}

		GD.Print("Adding control to dock: ", control);
		_uiParent.AddChild(control, true);
	}

	private void RemoveControlFromDocks(Control control) {
		if(_uiParent == null) {
			GD.PrintErr("UIParent isn't set!");
			return;
		}

		if(control.GetParent() != _uiParent) {
			GD.PrintErr("UIParent isn't the parent of this Control!");
			control.GetParent().RemoveChild(control);
			return;
		}

		_uiParent.RemoveChild(control);
	}

	private void AddControlToContainer(EditorPlugin.CustomControlContainer container, Control control) { // (EditorPlugin.CustomControlContainer, Control control)
		if(_uiParent == null) {
			GD.PrintErr("UIParent isn't set!");
			return;
		}

		GD.Print("Adding control to container: ", control);
		_uiParent.AddChild(control);
	}

	private void RemoveControlFromContainer(EditorPlugin.CustomControlContainer container, Control control) { // (EditorPlugin.CustomControlContainer, Control control)
		control.GetParent().RemoveChild(control);
	}

	private void AddToolMenuItem(string buttonName, Callable callable) {
		GD.Print("We should be adding a tool menu item here called ", buttonName);
	}

	private void RemoveToolMenuItem(string buttonName) {
		GD.Print("We should be removing a tool menu item here called ", buttonName);
	}

	private void HandleKeyBindings() {
		var dlg = ResourceLoader.Load<PackedScene>("res://TerraBrush/terrabrush/Components/KeybindSettings.tscn")
			.Instantiate<KeybindSettings>();
		dlg.Confirmed += () => dlg.QueueFree();
		GetTree().Root.AddChild(dlg);
		dlg.PopupCentered();
	}

	public void _Edit(GodotObject @object) {
		//base._Edit(@object);

		GD.Print("Trying to edit...");

		if (_Handles(@object)) {
			GD.Print("We can edit!");
			OnEditTerrainNode((TerraBrush) @object);
		} else {
			GD.Print("Nope, that's the wrong object.");
			OnExitEditTerrainNode();
		}
	}

	public bool _Handles(GodotObject @object) {
		return @object is TerraBrush;
	}

	public void _SaveExternalData() {
		//base._SaveExternalData();

		_currentTerraBrushNode?.SaveResources();
	}

	public override void _UnhandledInput(InputEvent @event) {
		Camera3D viewportCamera = GetViewport().GetCamera3D();
		var preventGuiInput = false;

		if (_toolInfo != null) {
			//_toolInfo.Position = viewportCamera.GetViewport().GetMousePosition() + viewportCamera.GetViewport().GetParent<SubViewportContainer>().GlobalPosition + new Vector2I(ToolInfoOffset, ToolInfoOffset);
			_toolInfo.Position = GetViewport().GetMousePosition() + new Vector2I(ToolInfoOffset, ToolInfoOffset);
			_toolInfo.SetText(_currentTerraBrushNode?.CurrentTool?.GetToolInfo(_currentTerraBrushNode.TerrainTool));
		}

		if (@event is InputEventMouseMotion inputMotion) {
			var meshPosition = GetRayCastWithTerrain(viewportCamera);
			if (meshPosition == Vector3.Inf) {
				_brushDecal.Visible = false;
			} else {
				_brushDecal.Visible = true;
				_brushDecal.Position = new Vector3(meshPosition.X, 0, meshPosition.Z);
			}

			_mouseHitPosition = meshPosition;
		}

		if (@event is InputEventKey inputEvent) {
			_terrainControlDock.SetShiftPressed(Input.IsKeyPressed(Key.Shift));

			if (!inputEvent.Pressed || inputEvent.Echo) return;

			if (inputEvent.IsAction(KeybindManager.StringNames.ToolPie)) {
				ShowToolPieMenu();
				GetViewport().SetInputAsHandled();
				return;
				//return (int) AfterGuiInput.Stop;
			}

			if (inputEvent.IsAction(KeybindManager.StringNames.BrushPie)) {
				ShowCustomContentPieMenu("Brushes", customContentPieMenu => {
					CustomContentLoader.AddBrushesPreviewToParent(customContentPieMenu.PieMenu, index => {
						_terrainControlDock.SetSelectedBrushIndex(index);
						HideOverlaySelector();
					});
				});
				GetViewport().SetInputAsHandled();
				return;
				//return (int) AfterGuiInput.Stop;
			}

			if (inputEvent.IsAction(KeybindManager.StringNames.ToolContentPie)) {
				ShowCurrentToolCustomContentPieMenu();
				GetViewport().SetInputAsHandled();
				return;
				//return (int) AfterGuiInput.Stop;
			}

			if (inputEvent.IsAction(KeybindManager.StringNames.BrushSizeSelector)) {
				ShowBrushNumericSelector(1, 200, Colors.LimeGreen, _currentTerraBrushNode.BrushSize, value => {
					_terrainControlDock.SetBrushSize(value);
				});
				GetViewport().SetInputAsHandled();
				return;
				//return (int) AfterGuiInput.Stop;
			}

			if (inputEvent.IsAction(KeybindManager.StringNames.BrushStrengthSelector)) {
				ShowBrushNumericSelector(1, 100, Colors.Crimson, (int) (_currentTerraBrushNode.BrushStrength * 100), value => {
					_terrainControlDock.SetBrushStrength(value / 100.0f);
				});
				GetViewport().SetInputAsHandled();
				return;
				//return (int) AfterGuiInput.Stop;
			}

			if (inputEvent.IsAction(KeybindManager.StringNames.EscapeSelector) && _overlaySelector != null) {
				HideOverlaySelector();
				GetViewport().SetInputAsHandled();
				return;
				//return (int) AfterGuiInput.Stop;
			}

			if (inputEvent.IsAction(KeybindManager.StringNames.ToggleAutoAddZones)) {
				_autoAddZonesCheckbox.ButtonPressed = !_autoAddZonesCheckbox.ButtonPressed;
				UpdateAutoAddZonesSetting();
				GetViewport().SetInputAsHandled();
				return;
				//return (int) AfterGuiInput.Stop;
			}
		}

		if (@event is InputEventMouseButton inputButton) {
			if (_overlaySelector != null) {
				if (_overlaySelector is BrushNumericSelector brushNumericSelector) {
					brushNumericSelector.RequestSelectValue();
				}

				HideOverlaySelector();
				GetViewport().SetInputAsHandled();
				return;
				//return (int) EditorPlugin.AfterGuiInput.Stop;
			}

			if (inputButton.ButtonIndex == MouseButton.Left) {
				if (inputButton.Pressed) {
					if (_mouseHitPosition != Vector3.Inf) {
						_undoRedo.CreateAction("Modify terrain");

						// Trigger a dirty state
						_undoRedo.AddUndoProperty(_currentTerraBrushNode, nameof(TerraBrush.ZonesSize), _currentTerraBrushNode.ZonesSize);

						_isMousePressed = true;
						preventGuiInput = true;
						_currentTerraBrushNode.BeingEditTerrain();
					}
				} else if (_isMousePressed) {
					_currentTerraBrushNode.Terrain.TerrainUpdated();
					_isMousePressed = false;


					// Trigger a dirty state
					_undoRedo.AddDoProperty(_currentTerraBrushNode, nameof(TerraBrush.ZonesSize), _currentTerraBrushNode.ZonesSize);

					_currentTerraBrushNode.EndEditTerrain();

					_undoRedo.AddUndoMethod(Callable.From(OnUndoRedo));
					_undoRedo.AddDoMethod(Callable.From(OnUndoRedo));

					_preventInitialDo = true;
					_undoRedo.CommitAction();
					_preventInitialDo = false;
				}
			}
		}

		_currentTerraBrushNode?.UpdateCameraPosition(viewportCamera);

		if (preventGuiInput) {
			GetViewport().SetInputAsHandled();
			return;
			//return (int) EditorPlugin.AfterGuiInput.Stop;
		} else {
			if ((_currentTerraBrushNode?.CurrentTool?.HandleInput(_currentTerraBrushNode.TerrainTool, @event)).GetValueOrDefault()) {
				GetViewport().SetInputAsHandled();
				return;
				//return (int) EditorPlugin.AfterGuiInput.Stop;
			}

			//return base._Forward3DGuiInput(viewportCamera, @event);
		}
	}

	public override void _PhysicsProcess(double delta) {
		base._PhysicsProcess(delta);

		if (!_isMousePressed) {
			_updateTime = 0;
		} else if (_updateTime > 0) {
			_updateTime -= (float) delta;
		} else if (_isMousePressed && _mouseHitPosition != Vector3.Inf) {
			_currentTerraBrushNode.EditTerrain(_mouseHitPosition);

			_updateTime = UpdateDelay;
		}
	}

	private void OnUndoTexture(ImageTexture imageTexture, byte[] previousImageData) {
		if (_preventInitialDo) {
			return;
		}

		var image = new Image();
		image.SetData(imageTexture.GetWidth(), imageTexture.GetHeight(), imageTexture.GetImage().HasMipmaps(), imageTexture.GetFormat(), previousImageData);
		imageTexture.Update(image);
	}

	private async void OnUndoRedo() {
		if (_preventInitialDo) {
			return;
		}

		_currentTerraBrushNode.Terrain.TerrainUpdated();
		_currentTerraBrushNode.TerrainZones?.UpdateImageTextures();

		_currentTerraBrushNode.ClearObjects();
		await _currentTerraBrushNode.CreateObjects();
	}

	private Vector3 GetRayCastWithTerrain(Camera3D editorCamera) {
		var spaceState = editorCamera.GetWorld3D().DirectSpaceState;

		var screenPosition = editorCamera.GetViewport().GetMousePosition();

		var from = editorCamera.ProjectRayOrigin(screenPosition);
		var dir = editorCamera.ProjectRayNormal(screenPosition);

		var distance = 2000;
		var query = new PhysicsRayQueryParameters3D() {
			From = from,
			To = from + dir * distance
		};
		var result = spaceState.IntersectRay(query);

		if (result?.Count > 0 && result["collider"].Obj == _currentTerraBrushNode.Terrain?.TerrainCollider) {
			return (Vector3)result["position"] + new Vector3(0, 0.1f, 0);
		} else {
			return GetMouseClickToZoneHeight(from, dir);
		}
	}

	private Vector3 GetMouseClickToZoneHeight(Vector3 from, Vector3 direction) {
		var heightmapsCache = new Dictionary<ImageTexture, Image>();

		for (var i = 0; i < 20000; i++) {
			var position = from + (direction * i * 0.1f);

			var zoneInfo = ZoneUtils.GetPixelToZoneInfo(position.X + (_currentTerraBrushNode.ZonesSize / 2), position.Z + (_currentTerraBrushNode.ZonesSize / 2), _currentTerraBrushNode.ZonesSize);
			var zone = _currentTerraBrushNode?.TerrainZones?.GetZoneForZoneInfo(zoneInfo);
			if (zone != null && zone.HeightMapTexture != null) {
				heightmapsCache.TryGetValue(zone.HeightMapTexture, out var heightMapImage);
				if (heightMapImage == null) {
					heightMapImage = zone.HeightMapTexture.GetImage();
					heightmapsCache.Add(zone.HeightMapTexture, heightMapImage);
				}

				var zoneHeight = heightMapImage.GetPixel(zoneInfo.ImagePosition.X, zoneInfo.ImagePosition.Y).R;

				if (zoneHeight >= position.Y) {
					return new Vector3(position.X, zoneHeight, position.Z);
				}
			}

		}

		return Vector3.Inf;
	}

	public override void _ExitTree() {
		//RemoveCustomType("TerraBrush");
		RemoveToolMenuItem("TerraBrush Key bindings");

		OnExitEditTerrainNode();
	}

	private void RemoveDock() {
		if (_terrainControlDock != null) {
			RemoveControlFromDocks(_terrainControlDock);
			_terrainControlDock.Free();

			_terrainControlDock = null;
		}

		if (_updateTerrainSettingsButton != null) {
			RemoveControlFromContainer(EditorPlugin.CustomControlContainer.SpatialEditorMenu, _updateTerrainSettingsButton);
			_updateTerrainSettingsButton.QueueFree();

			_updateTerrainSettingsButton = null;
		}

		if (_autoAddZonesCheckbox != null) {
			RemoveControlFromContainer(EditorPlugin.CustomControlContainer.SpatialEditorMenu, _autoAddZonesCheckbox);
			_autoAddZonesCheckbox.QueueFree();

			_autoAddZonesCheckbox = null;
		}
	}

	private void OnEditTerrainNode(TerraBrush terraBrush) {
		RemoveDock();
		GetNodeOrNull("BrushDecal")?.QueueFree();
		_brushDecal?.QueueFree();

		_uiParent = terraBrush.UIParent;

		_brushDecal = ResourceLoader.Load<PackedScene>("res://TerraBrush/terrabrush/Components/BrushDecal.tscn").Instantiate<BrushDecal>();
		_brushDecal.Name = "BrushDecal";
		AddChild(_brushDecal);

		_brushDecal.SetSize(terraBrush.BrushSize);
		_brushDecal.SetBrushImage(terraBrush.BrushImage);

		_currentTerraBrushNode = terraBrush;
		_currentTerraBrushNode.TerrainSettingsUpdated += () => {
			RemoveDock();
			AddDock();
		};
		//_undoRedo = GetUndoRedo();
		_currentTerraBrushNode.UndoRedo = undoRedo;

		GetNodeOrNull("ToolInfo")?.QueueFree();
		_toolInfo?.QueueFree();

		_toolInfo = ResourceLoader.Load<PackedScene>("res://TerraBrush/terrabrush/Components/ToolInfo.tscn").Instantiate<ToolInfo>();
		_toolInfo.Name = "ToolInfo";
		AddChild(_toolInfo);

		AddDock();

		terraBrush.SetMeta("_edit_lock_", true);
	}

	private void AddDock() {
		_terrainControlDock = _terrainControlDockPrefab.Instantiate<TerrainControlDock>();
		_terrainControlDock.TerraBrush = _currentTerraBrushNode;
		_terrainControlDock.BrushDecal = _brushDecal;
		//_terrainControlDock.EditorResourcePreview = EditorInterface.Singleton.GetResourcePreviewer();
		AddControlToDock(EditorPlugin.DockSlot.RightBl, _terrainControlDock);

		_updateTerrainSettingsButton = new Button() {
			Text = "Update terrain"
		};
		_updateTerrainSettingsButton.Connect("pressed", new Callable(this, nameof(UpdateTerrainSettings)));
		AddControlToContainer(EditorPlugin.CustomControlContainer.SpatialEditorMenu, _updateTerrainSettingsButton);

		_autoAddZonesCheckbox = new CheckBox() {
			Text = "Auto add zones",
			ButtonPressed = _currentTerraBrushNode.AutoAddZones
		};
		_autoAddZonesCheckbox.Connect("pressed", new Callable(this, nameof(UpdateAutoAddZonesSetting)));
		AddControlToContainer(EditorPlugin.CustomControlContainer.SpatialEditorMenu, _autoAddZonesCheckbox);
	}

	private void OnExitEditTerrainNode() {
		RemoveDock();
		HideOverlaySelector();

		_brushDecal?.QueueFree();
		_brushDecal = null;
		GetNodeOrNull("BrushDecal")?.QueueFree();

		GetNodeOrNull("ToolInfo")?.QueueFree();
		_toolInfo?.QueueFree();
		_toolInfo = null;

		_currentTerraBrushNode.SetMeta("_edit_lock_", false);

		_currentTerraBrushNode = null;
	}

	private Node GetEditorViewportsContainerRecursive(Node node) {
		foreach (var child in node.GetChildren()) {
			if (child.GetClass() == "Node3DEditorViewportContainer") {
				return child;
			}

			var subChild = GetEditorViewportsContainerRecursive(child);
			if (subChild != null) {
				return subChild;
			}
		}

		return null;
	}

	private void HideOverlaySelector() {
		if (_overlaySelector != null) {
			_overlaySelector.QueueFree();
			_overlaySelector = null;
		}
	}

	private void ShowToolPieMenu() {
		HideOverlaySelector();

		var activeViewport = GetViewport();
		if (activeViewport != null) {
			var pieMenu = _toolsPieMenuPrefab.Instantiate<ToolsPieMenu>();
			pieMenu.OnToolSelected = toolType => {
				_terrainControlDock.SelectToolType(toolType);

				HideOverlaySelector();
			};

			_overlaySelector = pieMenu;

			_overlaySelector.Position = activeViewport.GetMousePosition();

			EditorInterface.Singleton.GetBaseControl().AddChild(_overlaySelector);
		}
	}

	private void ShowCustomContentPieMenu(string label, Action<CustomContentPieMenu> addItems) {
		HideOverlaySelector();

		var activeViewport = GetViewport();
		if (activeViewport != null) {
			var customContentPieMenu = _customContentPieMenuPrefab.Instantiate<CustomContentPieMenu>();

			_overlaySelector = customContentPieMenu;
			_overlaySelector.Position = activeViewport.GetMousePosition();

			EditorInterface.Singleton.GetBaseControl().AddChild(_overlaySelector);

			addItems(customContentPieMenu);

			customContentPieMenu.PieMenu.Label = label;
			customContentPieMenu.PieMenu.UpdateContent();
		}
	}

	private void ShowCurrentToolCustomContentPieMenu() {
		switch (_currentTerraBrushNode.TerrainTool) {
			case TerrainToolType.Paint:
				ShowCustomContentPieMenu("Textures", customContentPieMenu => {
					CustomContentLoader.AddTexturesPreviewToParent(_currentTerraBrushNode, customContentPieMenu.PieMenu, index => {
						_terrainControlDock.SetSelectedTextureIndex(index);
						HideOverlaySelector();
					});
				});

				break;
			case TerrainToolType.FoliageAdd:
			case TerrainToolType.FoliagRemove:
				ShowCustomContentPieMenu("Foliages", customContentPieMenu => {
					CustomContentLoader.AddFoliagesPreviewToParent(_currentTerraBrushNode, customContentPieMenu.PieMenu, index => {
						_terrainControlDock.SetSelectedFoliageIndex(index);
						HideOverlaySelector();
					});
				});

				break;
			case TerrainToolType.ObjectAdd:
			case TerrainToolType.ObjectRemove:
				ShowCustomContentPieMenu("Objects", customContentPieMenu => {
					CustomContentLoader.AddObjectsPreviewToParent(_currentTerraBrushNode, customContentPieMenu.PieMenu, index => {
						_terrainControlDock.SetSelectedObjectIndex(index);
						HideOverlaySelector();
					});
				});

				break;
		}
	}

	private void ShowBrushNumericSelector(int minVale, int maxValue, Color widgetColor, int initialValue, Action<int> onValueSelected) {
		HideOverlaySelector();

		var activeViewport = GetViewport();
		if (activeViewport != null) {
			var selectorPrefab = ResourceLoader.Load<PackedScene>("res://TerraBrush/terrabrush/Components/BrushNumericSelector.tscn");
			var selector = selectorPrefab.Instantiate<BrushNumericSelector>();

			selector.MinValue = minVale;
			selector.MaxValue = maxValue;
			selector.WidgetColor = widgetColor;

			_overlaySelector = selector;
			_overlaySelector.Position = activeViewport.GetMousePosition();

			EditorInterface.Singleton.GetBaseControl().AddChild(_overlaySelector);

			selector.SetInitialValue(initialValue);

			selector.OnValueSelected = value => {
				onValueSelected(value);
				HideOverlaySelector();
			};

			selector.OnCancel = () => {
				HideOverlaySelector();
			};
		}
	}

	private void UpdateTerrainSettings() {
		_currentTerraBrushNode?.OnUpdateTerrainSettings();
	}

	private void UpdateAutoAddZonesSetting() {
		_currentTerraBrushNode.AutoAddZones = _autoAddZonesCheckbox.ButtonPressed;
	}
}
