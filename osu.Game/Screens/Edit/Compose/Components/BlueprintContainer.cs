﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK;
using osuTK.Input;

namespace osu.Game.Screens.Edit.Compose.Components
{
    /// <summary>
    /// A container which provides a "blueprint" display of hitobjects.
    /// Includes selection and manipulation support via a <see cref="Components.SelectionHandler{T}"/>.
    /// </summary>
    public abstract class BlueprintContainer<T> : CompositeDrawable, IKeyBindingHandler<PlatformAction>
    {
        protected DragBox DragBox { get; private set; }

        public Container<SelectionBlueprint<T>> SelectionBlueprints { get; private set; }

        protected SelectionHandler<T> SelectionHandler { get; private set; }

        private readonly Dictionary<T, SelectionBlueprint<T>> blueprintMap = new Dictionary<T, SelectionBlueprint<T>>();

        [Resolved(canBeNull: true)]
        private IPositionSnapProvider snapProvider { get; set; }

        [Resolved(CanBeNull = true)]
        private IEditorChangeHandler changeHandler { get; set; }

        protected BlueprintContainer()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            SelectionHandler = CreateSelectionHandler();
            SelectionHandler.DeselectAll = deselectAll;

            AddRangeInternal(new[]
            {
                DragBox = CreateDragBox(selectBlueprintsFromDragRectangle),
                SelectionHandler,
                SelectionBlueprints = CreateSelectionBlueprintContainer(),
                SelectionHandler.CreateProxy(),
                DragBox.CreateProxy().With(p => p.Depth = float.MinValue)
            });
        }

        protected virtual Container<SelectionBlueprint<T>> CreateSelectionBlueprintContainer() => new Container<SelectionBlueprint<T>> { RelativeSizeAxes = Axes.Both };

        /// <summary>
        /// Creates a <see cref="Components.SelectionHandler{T}"/> which outlines <see cref="DrawableHitObject"/>s and handles movement of selections.
        /// </summary>
        protected virtual SelectionHandler<T> CreateSelectionHandler() => new SelectionHandler<T>();

        /// <summary>
        /// Creates a <see cref="SelectionBlueprint{T}"/> for a specific <see cref="DrawableHitObject"/>.
        /// </summary>
        /// <param name="hitObject">The <see cref="DrawableHitObject"/> to create the overlay for.</param>
        protected virtual SelectionBlueprint<T> CreateBlueprintFor(T hitObject) => null;

        protected virtual DragBox CreateDragBox(Action<RectangleF> performSelect) => new DragBox(performSelect);

        /// <summary>
        /// Whether this component is in a state where deselection should be allowed. If false, selection will only be added to.
        /// </summary>
        protected virtual bool AllowDeselection => true;

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            bool selectionPerformed = performMouseDownActions(e);

            // even if a selection didn't occur, a drag event may still move the selection.
            prepareSelectionMovement();

            return selectionPerformed || e.Button == MouseButton.Left;
        }

        protected SelectionBlueprint<T> ClickedBlueprint { get; private set; }

        protected override bool OnClick(ClickEvent e)
        {
            if (e.Button == MouseButton.Right)
                return false;

            // store for double-click handling
            ClickedBlueprint = SelectionHandler.SelectedBlueprints.FirstOrDefault(b => b.IsHovered);

            // Deselection should only occur if no selected blueprints are hovered
            // A special case for when a blueprint was selected via this click is added since OnClick() may occur outside the hitobject and should not trigger deselection
            if (endClickSelection(e) || ClickedBlueprint != null)
                return true;

            deselectAll();
            return true;
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            if (e.Button == MouseButton.Right)
                return false;

            // ensure the blueprint which was hovered for the first click is still the hovered blueprint.
            if (ClickedBlueprint == null || SelectionHandler.SelectedBlueprints.FirstOrDefault(b => b.IsHovered) != ClickedBlueprint)
                return false;

            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            // Special case for when a drag happened instead of a click
            Schedule(() =>
            {
                endClickSelection(e);
                clickSelectionBegan = false;
                isDraggingBlueprint = false;
            });

            finishSelectionMovement();
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            if (e.Button == MouseButton.Right)
                return false;

            if (movementBlueprints != null)
            {
                isDraggingBlueprint = true;
                changeHandler?.BeginChange();
                return true;
            }

            if (DragBox.HandleDrag(e))
            {
                DragBox.Show();
                return true;
            }

            return false;
        }

        protected override void OnDrag(DragEvent e)
        {
            if (e.Button == MouseButton.Right)
                return;

            if (DragBox.State == Visibility.Visible)
                DragBox.HandleDrag(e);

            moveCurrentSelection(e);
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            if (e.Button == MouseButton.Right)
                return;

            if (isDraggingBlueprint)
            {
                UpdateSelection();

                changeHandler?.EndChange();
            }

            if (DragBox.State == Visibility.Visible)
                DragBox.Hide();
        }

        protected virtual void UpdateSelection()
        {
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (!SelectionHandler.SelectedBlueprints.Any())
                        return false;

                    deselectAll();
                    return true;
            }

            return false;
        }

        public bool OnPressed(PlatformAction action)
        {
            switch (action.ActionType)
            {
                case PlatformActionType.SelectAll:
                    SelectAll();
                    return true;
            }

            return false;
        }

        public void OnReleased(PlatformAction action)
        {
        }

        #region Blueprint Addition/Removal

        protected virtual void AddBlueprintFor(T item)
        {
            if (blueprintMap.ContainsKey(item))
                return;

            var blueprint = CreateBlueprintFor(item);
            if (blueprint == null)
                return;

            blueprintMap[item] = blueprint;

            blueprint.Selected += OnBlueprintSelected;
            blueprint.Deselected += OnBlueprintDeselected;

            SelectionBlueprints.Add(blueprint);

            OnBlueprintAdded(blueprint);
        }

        protected void RemoveBlueprintFor(T item)
        {
            if (!blueprintMap.Remove(item, out var blueprint))
                return;

            blueprint.Deselect();
            blueprint.Selected -= OnBlueprintSelected;
            blueprint.Deselected -= OnBlueprintDeselected;

            SelectionBlueprints.Remove(blueprint);

            if (movementBlueprints?.Contains(blueprint) == true)
                finishSelectionMovement();

            OnBlueprintRemoved(blueprint);
        }

        /// <summary>
        /// Called after a <see cref="HitObject"/> blueprint has been added.
        /// </summary>
        /// <param name="blueprint">The <see cref="HitObject"/> for which the blueprint has been added.</param>
        protected virtual void OnBlueprintAdded(SelectionBlueprint<T> blueprint)
        {
        }

        /// <summary>
        /// Called after a <see cref="HitObject"/> blueprint has been removed.
        /// </summary>
        /// <param name="item">The <see cref="HitObject"/> for which the blueprint has been removed.</param>
        protected virtual void OnBlueprintRemoved(SelectionBlueprint<T> item)
        {
        }

        #endregion

        #region Selection

        /// <summary>
        /// Whether a blueprint was selected by a previous click event.
        /// </summary>
        private bool clickSelectionBegan;

        /// <summary>
        /// Attempts to select any hovered blueprints.
        /// </summary>
        /// <param name="e">The input event that triggered this selection.</param>
        /// <returns>Whether a selection was performed.</returns>
        private bool performMouseDownActions(MouseButtonEvent e)
        {
            // Iterate from the top of the input stack (blueprints closest to the front of the screen first).
            // Priority is given to already-selected blueprints.
            foreach (SelectionBlueprint<T> blueprint in SelectionBlueprints.AliveChildren.Reverse().OrderByDescending(b => b.IsSelected))
            {
                if (!blueprint.IsHovered) continue;

                return clickSelectionBegan = SelectionHandler.MouseDownSelectionRequested(blueprint, e);
            }

            return false;
        }

        /// <summary>
        /// Finishes the current blueprint selection.
        /// </summary>
        /// <param name="e">The mouse event which triggered end of selection.</param>
        /// <returns>Whether a click selection was active.</returns>
        private bool endClickSelection(MouseButtonEvent e)
        {
            if (!clickSelectionBegan && !isDraggingBlueprint)
            {
                // if a selection didn't occur, we may want to trigger a deselection.
                if (e.ControlPressed && e.Button == MouseButton.Left)
                {
                    // Iterate from the top of the input stack (blueprints closest to the front of the screen first).
                    // Priority is given to already-selected blueprints.
                    foreach (SelectionBlueprint<T> blueprint in SelectionBlueprints.AliveChildren.Reverse().OrderByDescending(b => b.IsSelected))
                    {
                        if (!blueprint.IsHovered) continue;

                        return clickSelectionBegan = SelectionHandler.MouseUpSelectionRequested(blueprint, e);
                    }
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Select all masks in a given rectangle selection area.
        /// </summary>
        /// <param name="rect">The rectangle to perform a selection on in screen-space coordinates.</param>
        private void selectBlueprintsFromDragRectangle(RectangleF rect)
        {
            foreach (var blueprint in SelectionBlueprints)
            {
                // only run when utmost necessary to avoid unnecessary rect computations.
                bool isValidForSelection() => blueprint.IsAlive && blueprint.IsPresent && rect.Contains(blueprint.ScreenSpaceSelectionPoint);

                switch (blueprint.State)
                {
                    case SelectionState.NotSelected:
                        if (isValidForSelection())
                            blueprint.Select();
                        break;

                    case SelectionState.Selected:
                        // if the editor is playing, we generally don't want to deselect objects even if outside the selection area.
                        if (AllowDeselection && !isValidForSelection())
                            blueprint.Deselect();
                        break;
                }
            }
        }

        /// <summary>
        /// Selects all <see cref="SelectionBlueprint{T}"/>s.
        /// </summary>
        protected virtual void SelectAll()
        {
            // Scheduled to allow the change in lifetime to take place.
            Schedule(() => SelectionBlueprints.ToList().ForEach(m => m.Select()));
        }

        /// <summary>
        /// Deselects all selected <see cref="SelectionBlueprint{T}"/>s.
        /// </summary>
        private void deselectAll() => SelectionHandler.SelectedBlueprints.ToList().ForEach(m => m.Deselect());

        protected virtual void OnBlueprintSelected(SelectionBlueprint<T> blueprint)
        {
            SelectionHandler.HandleSelected(blueprint);
            SelectionBlueprints.ChangeChildDepth(blueprint, 1);
        }

        protected virtual void OnBlueprintDeselected(SelectionBlueprint<T> blueprint)
        {
            SelectionBlueprints.ChangeChildDepth(blueprint, 0);
            SelectionHandler.HandleDeselected(blueprint);
        }

        #endregion

        #region Selection Movement

        private Vector2[] movementBlueprintOriginalPositions;
        private SelectionBlueprint<T>[] movementBlueprints;
        private bool isDraggingBlueprint;

        /// <summary>
        /// Attempts to begin the movement of any selected blueprints.
        /// </summary>
        private void prepareSelectionMovement()
        {
            if (!SelectionHandler.SelectedBlueprints.Any())
                return;

            // Any selected blueprint that is hovered can begin the movement of the group, however only the earliest hitobject is used for movement
            // A special case is added for when a click selection occurred before the drag
            if (!clickSelectionBegan && !SelectionHandler.SelectedBlueprints.Any(b => b.IsHovered))
                return;

            // Movement is tracked from the blueprint of the earliest hitobject, since it only makes sense to distance snap from that hitobject
            movementBlueprints = SortForMovement(SelectionHandler.SelectedBlueprints).ToArray();
            movementBlueprintOriginalPositions = movementBlueprints.Select(m => m.ScreenSpaceSelectionPoint).ToArray();
        }

        protected virtual IEnumerable<SelectionBlueprint<T>> SortForMovement(IReadOnlyList<SelectionBlueprint<T>> blueprints) => blueprints;

        /// <summary>
        /// Moves the current selected blueprints.
        /// </summary>
        /// <param name="e">The <see cref="DragEvent"/> defining the movement event.</param>
        /// <returns>Whether a movement was active.</returns>
        private bool moveCurrentSelection(DragEvent e)
        {
            if (movementBlueprints == null)
                return false;

            if (snapProvider == null)
                return true;

            Debug.Assert(movementBlueprintOriginalPositions != null);

            Vector2 distanceTravelled = e.ScreenSpaceMousePosition - e.ScreenSpaceMouseDownPosition;

            // check for positional snap for every object in selection (for things like object-object snapping)
            for (var i = 0; i < movementBlueprintOriginalPositions.Length; i++)
            {
                var testPosition = movementBlueprintOriginalPositions[i] + distanceTravelled;

                var positionalResult = snapProvider.SnapScreenSpacePositionToValidPosition(testPosition);

                if (positionalResult.ScreenSpacePosition == testPosition) continue;

                // attempt to move the objects, and abort any time based snapping if we can.
                if (SelectionHandler.HandleMovement(new MoveSelectionEvent<T>(movementBlueprints[i], positionalResult.ScreenSpacePosition)))
                    return true;
            }

            // if no positional snapping could be performed, try unrestricted snapping from the earliest
            // hitobject in the selection.

            // The final movement position, relative to movementBlueprintOriginalPosition.
            Vector2 movePosition = movementBlueprintOriginalPositions.First() + distanceTravelled;

            // Retrieve a snapped position.
            var result = snapProvider.SnapScreenSpacePositionToValidTime(movePosition);

            return ApplySnapResult(movementBlueprints, result);
        }

        protected virtual bool ApplySnapResult(SelectionBlueprint<T>[] blueprints, SnapResult result)
        {
            return !SelectionHandler.HandleMovement(new MoveSelectionEvent<T>(blueprints.First(), result.ScreenSpacePosition));
        }

        /// <summary>
        /// Finishes the current movement of selected blueprints.
        /// </summary>
        /// <returns>Whether a movement was active.</returns>
        private bool finishSelectionMovement()
        {
            if (movementBlueprints == null)
                return false;

            movementBlueprintOriginalPositions = null;
            movementBlueprints = null;

            return true;
        }

        #endregion
    }
}
