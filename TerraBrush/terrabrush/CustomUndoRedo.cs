using Godot;
using System;
using System.Collections.Generic;

public partial class CustomUndoRedo : RefCounted
{
	List<UndoRedoAction> undoStack = new();
	List<UndoRedoAction> redoStack = new();

	UndoRedoAction currentAction;

	public void CreateAction(string actionName) {
		if(currentAction != null) {
			GD.PushWarning("Note: Previous action wasn't committed.");
		}

		currentAction = new UndoRedoAction();
		currentAction.actionName = actionName;
	}

	public void AddUndoMethod(Callable method) {
		currentAction.undoMethods.Add(method);
	}

	public void AddDoMethod(Callable method) {
		currentAction.doMethods.Add(method);
	}

	public void AddUndoReference(RefCounted reference) {
		currentAction.undoReferences.Add(reference);
	}

	public void AddDoReference(RefCounted reference) {
		currentAction.doReferences.Add(reference);
	}

	public void AddUndoProperty(GodotObject godotObject, StringName property, Variant value) {
		UndoRedoProperty p = new(godotObject, property, value);
		currentAction.undoProperties.Add(p);
	}

	public void AddDoProperty(GodotObject godotObject, StringName property, Variant value) {
		UndoRedoProperty p = new(godotObject, property, value);
		currentAction.doProperties.Add(p);
	}

	public void CommitAction(bool execute = true) {
		if(currentAction == null) {
			GD.PushError("No action to commit!");
			return;
		}

		undoStack.Add(currentAction);

		if(execute) {
			currentAction.ExecuteDo();
		}

		currentAction = null;
		redoStack.Clear();
	}

	public bool HasUndo() {
		return undoStack.Count > 0;
	}

	public bool HasRedo() {
		return redoStack.Count > 0;
	}

	public bool Undo() {
		if(undoStack.Count > 0) {
			UndoRedoAction action = undoStack[undoStack.Count - 1];
			action.ExecuteUndo();
			undoStack.RemoveAt(undoStack.Count - 1);
			redoStack.Add(action);
			return true;
		}
		return false;
	}

	public bool Redo() {
		if(redoStack.Count > 0) {
			UndoRedoAction action = redoStack[redoStack.Count - 1];
			action.ExecuteDo();
			redoStack.RemoveAt(redoStack.Count - 1);
			undoStack.Add(action);
			return true;
		}
		return false;
	}
	
	private partial class UndoRedoAction : RefCounted {
		public string actionName;

		public List<Callable> undoMethods = new();
		public List<Callable> doMethods = new();
		public List<RefCounted> undoReferences = new();
		public List<RefCounted> doReferences = new();
		public List<UndoRedoProperty> undoProperties = new();
		public List<UndoRedoProperty> doProperties = new();

		public void ExecuteUndo() {
			foreach(Callable c in undoMethods) {
				c.Call();
			}

			foreach(UndoRedoProperty p in undoProperties) {
				p.godotObject.Set(p.property, p.value);
			}
		}

		public void ExecuteDo() {
			foreach(Callable c in doMethods) {
				c.Call();
			}

			foreach(UndoRedoProperty p in doProperties) {
				p.godotObject.Set(p.property, p.value);
			}
		}
	}

	private partial class UndoRedoProperty : RefCounted {
		public GodotObject godotObject;
		public StringName property;
		public Variant value;

		public UndoRedoProperty(GodotObject godotObject, StringName property, Variant value) {
			this.godotObject = godotObject;
			this.property = property;
			this.value = value;
		}
	}
}
