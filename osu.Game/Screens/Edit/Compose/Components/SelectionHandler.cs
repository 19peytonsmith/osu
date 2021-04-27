﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK;
using osuTK.Input;

namespace osu.Game.Screens.Edit.Compose.Components
{
    /// <summary>
    /// A component which outlines <see cref="DrawableHitObject"/>s and handles movement of selections.
    /// </summary>
    public class SelectionHandler<T> : CompositeDrawable, IKeyBindingHandler<PlatformAction>
    {
        /// <summary>
        /// The currently selected blueprints.
        /// Should be used when operations are dealing directly with the visible blueprints.
        /// For more general selection operations, use <see cref="osu.Game.Screens.Edit.EditorBeatmap.SelectedHitObjects"/> instead.
        /// </summary>
        public IReadOnlyList<SelectionBlueprint<T>> SelectedBlueprints => selectedBlueprints;

        protected BindableList<T> SelectedItems = new BindableList<T>();

        private readonly List<SelectionBlueprint<T>> selectedBlueprints;

        private Drawable content;

        private OsuSpriteText selectionDetailsText;

        protected SelectionBox SelectionBox { get; private set; }

        [Resolved(CanBeNull = true)]
        protected IEditorChangeHandler ChangeHandler { get; private set; }

        public SelectionHandler()
        {
            selectedBlueprints = new List<SelectionBlueprint<T>>();

            RelativeSizeAxes = Axes.Both;
            AlwaysPresent = true;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            InternalChild = content = new Container
            {
                Children = new Drawable[]
                {
                    // todo: should maybe be inside the SelectionBox?
                    new Container
                    {
                        Name = "info text",
                        AutoSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                Colour = colours.YellowDark,
                                RelativeSizeAxes = Axes.Both,
                            },
                            selectionDetailsText = new OsuSpriteText
                            {
                                Padding = new MarginPadding(2),
                                Colour = colours.Gray0,
                                Font = OsuFont.Default.With(size: 11)
                            }
                        }
                    },
                    SelectionBox = CreateSelectionBox(),
                }
            };

            SelectedItems.CollectionChanged += (sender, args) =>
            {
                Scheduler.AddOnce(updateVisibility);
            };
        }

        public SelectionBox CreateSelectionBox()
            => new SelectionBox
            {
                OperationStarted = OnOperationBegan,
                OperationEnded = OnOperationEnded,

                OnRotation = HandleRotation,
                OnScale = HandleScale,
                OnFlip = HandleFlip,
                OnReverse = HandleReverse,
            };

        /// <summary>
        /// Fired when a drag operation ends from the selection box.
        /// </summary>
        protected virtual void OnOperationBegan()
        {
            ChangeHandler?.BeginChange();
        }

        /// <summary>
        /// Fired when a drag operation begins from the selection box.
        /// </summary>
        protected virtual void OnOperationEnded()
        {
            ChangeHandler?.EndChange();
        }

        #region User Input Handling

        /// <summary>
        /// Handles the selected <see cref="DrawableHitObject"/>s being moved.
        /// </summary>
        /// <remarks>
        /// Just returning true is enough to allow <see cref="HitObject.StartTime"/> updates to take place.
        /// Custom implementation is only required if other attributes are to be considered, like changing columns.
        /// </remarks>
        /// <param name="moveEvent">The move event.</param>
        /// <returns>
        /// Whether any <see cref="DrawableHitObject"/>s could be moved.
        /// Returning true will also propagate StartTime changes provided by the closest <see cref="IPositionSnapProvider.SnapScreenSpacePositionToValidTime"/>.
        /// </returns>
        public virtual bool HandleMovement(MoveSelectionEvent<T> moveEvent) => false;

        /// <summary>
        /// Handles the selected <see cref="DrawableHitObject"/>s being rotated.
        /// </summary>
        /// <param name="angle">The delta angle to apply to the selection.</param>
        /// <returns>Whether any <see cref="DrawableHitObject"/>s could be rotated.</returns>
        public virtual bool HandleRotation(float angle) => false;

        /// <summary>
        /// Handles the selected <see cref="DrawableHitObject"/>s being scaled.
        /// </summary>
        /// <param name="scale">The delta scale to apply, in playfield local coordinates.</param>
        /// <param name="anchor">The point of reference where the scale is originating from.</param>
        /// <returns>Whether any <see cref="DrawableHitObject"/>s could be scaled.</returns>
        public virtual bool HandleScale(Vector2 scale, Anchor anchor) => false;

        /// <summary>
        /// Handles the selected <see cref="DrawableHitObject"/>s being flipped.
        /// </summary>
        /// <param name="direction">The direction to flip</param>
        /// <returns>Whether any <see cref="DrawableHitObject"/>s could be flipped.</returns>
        public virtual bool HandleFlip(Direction direction) => false;

        /// <summary>
        /// Handles the selected <see cref="DrawableHitObject"/>s being reversed pattern-wise.
        /// </summary>
        /// <returns>Whether any <see cref="DrawableHitObject"/>s could be reversed.</returns>
        public virtual bool HandleReverse() => false;

        public bool OnPressed(PlatformAction action)
        {
            switch (action.ActionMethod)
            {
                case PlatformActionMethod.Delete:
                    DeleteSelected();
                    return true;
            }

            return false;
        }

        public void OnReleased(PlatformAction action)
        {
        }

        #endregion

        #region Selection Handling

        /// <summary>
        /// Bind an action to deselect all selected blueprints.
        /// </summary>
        internal Action DeselectAll { private get; set; }

        /// <summary>
        /// Handle a blueprint becoming selected.
        /// </summary>
        /// <param name="blueprint">The blueprint.</param>
        internal virtual void HandleSelected(SelectionBlueprint<T> blueprint)
        {
            selectedBlueprints.Add(blueprint);
        }

        /// <summary>
        /// Handle a blueprint becoming deselected.
        /// </summary>
        /// <param name="blueprint">The blueprint.</param>
        internal virtual void HandleDeselected(SelectionBlueprint<T> blueprint)
        {
            selectedBlueprints.Remove(blueprint);
        }

        /// <summary>
        /// Handle a blueprint requesting selection.
        /// </summary>
        /// <param name="blueprint">The blueprint.</param>
        /// <param name="e">The mouse event responsible for selection.</param>
        /// <returns>Whether a selection was performed.</returns>
        internal bool MouseDownSelectionRequested(SelectionBlueprint<T> blueprint, MouseButtonEvent e)
        {
            if (e.ShiftPressed && e.Button == MouseButton.Right)
            {
                handleQuickDeletion(blueprint);
                return true;
            }

            // while holding control, we only want to add to selection, not replace an existing selection.
            if (e.ControlPressed && e.Button == MouseButton.Left && !blueprint.IsSelected)
            {
                blueprint.ToggleSelection();
                return true;
            }

            return ensureSelected(blueprint);
        }

        /// <summary>
        /// Handle a blueprint requesting selection.
        /// </summary>
        /// <param name="blueprint">The blueprint.</param>
        /// <param name="e">The mouse event responsible for deselection.</param>
        /// <returns>Whether a deselection was performed.</returns>
        internal bool MouseUpSelectionRequested(SelectionBlueprint<T> blueprint, MouseButtonEvent e)
        {
            if (blueprint.IsSelected)
            {
                blueprint.ToggleSelection();
                return true;
            }

            return false;
        }

        private void handleQuickDeletion(SelectionBlueprint<T> blueprint)
        {
            if (blueprint.HandleQuickDeletion())
                return;

            if (!blueprint.IsSelected)
                DeleteItems(new[] { blueprint.Item });
            else
                DeleteSelected();
        }

        protected virtual void DeleteItems(IEnumerable<T> items)
        {
        }

        /// <summary>
        /// Ensure the blueprint is in a selected state.
        /// </summary>
        /// <param name="blueprint">The blueprint to select.</param>
        /// <returns>Whether selection state was changed.</returns>
        private bool ensureSelected(SelectionBlueprint<T> blueprint)
        {
            if (blueprint.IsSelected)
                return false;

            DeselectAll?.Invoke();
            blueprint.Select();
            return true;
        }

        protected void DeleteSelected()
        {
            DeleteItems(selectedBlueprints.Select(b => b.Item));
        }

        #endregion

        #region Outline Display

        /// <summary>
        /// Updates whether this <see cref="SelectionHandler{T}"/> is visible.
        /// </summary>
        private void updateVisibility()
        {
            int count = SelectedItems.Count;

            selectionDetailsText.Text = count > 0 ? count.ToString() : string.Empty;

            this.FadeTo(count > 0 ? 1 : 0);
            OnSelectionChanged();
        }

        /// <summary>
        /// Triggered whenever the set of selected objects changes.
        /// Should update the selection box's state to match supported operations.
        /// </summary>
        protected virtual void OnSelectionChanged()
        {
        }

        protected override void Update()
        {
            base.Update();

            if (selectedBlueprints.Count == 0)
                return;

            // Move the rectangle to cover the hitobjects
            var topLeft = new Vector2(float.MaxValue, float.MaxValue);
            var bottomRight = new Vector2(float.MinValue, float.MinValue);

            foreach (var blueprint in selectedBlueprints)
            {
                topLeft = Vector2.ComponentMin(topLeft, ToLocalSpace(blueprint.SelectionQuad.TopLeft));
                bottomRight = Vector2.ComponentMax(bottomRight, ToLocalSpace(blueprint.SelectionQuad.BottomRight));
            }

            topLeft -= new Vector2(5);
            bottomRight += new Vector2(5);

            content.Size = bottomRight - topLeft;
            content.Position = topLeft;
        }

        #endregion
    }
}
