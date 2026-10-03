# Penban TODO

Taken from the "Penban Feedback" board, grouped by column. `[x]` = done, `[~]` = partly done.
The QoL and "Future cool stuff" columns are written out in German as well, with the file and the
symbol behind every verdict: [docs/feedback-qol-cool-stuff.md](docs/feedback-qol-cool-stuff.md).
QoL 1, 2, 3, 4 and 7 are built (PR #48); the lasso and the shape recognition sit on top of that
(PR #49). All of it goes out together in 0.5.3, along with the board loading fix from 0.5.2. Both are
written out in German, with the files and symbols behind them:
[docs/lasso-und-formen.md](docs/lasso-und-formen.md).

## Bugs

- [ ] Swipe down on a card only works in the bottom half of the card, and when swiping horizontally in the middle of it.
- [ ] Lines drawn with pen pressure always render at maximum width on the board, which makes them hard to read.
- [~] Editing the title card sometimes changes its background colour when exiting to the menu. The stored colour is no longer wiped; the root cause is not reproduced yet.
- [x] Strokes change appearance when switching pen pressure on/off (the flag is now stored per stroke).

## Important features

- [ ] Reorder columns.
- [ ] Pick your own palettes: pen colours and card colours.
- [~] Selection tool: long press to activate; transform, copy and paste, delete the selection. The
  note selection behind it is in (select mode, move to another column, delete); the long press,
  transform and copy/paste are missing.

## QoL

- [x] Pen thickness: in the toolbar as a slider and a number next to the pens, the range comes from
  `PenThickness`; tapping beside the flyout closes it, and the width can be read off the pen, the
  number and the ring. Dragging the pen in hand changes it too.
- [x] Undo after erase restores everything erased since the pen went down, not only the last stroke.
- [x] Picking a colour switches to the pen.
- [x] Delete cards directly from the board view: select mode with checkmarks, one question for the
  whole selection, and a red drop area above the board while dragging a card.
- [ ] Board overview: show only the title card instead of laying it on top of the card stack.
  - Fit it to size so the title card is easier to tell apart from a normal card stack.
  - Keep the card stack when no title card is given.
- [x] Pen pressure toggle in the card draw toolbar: the switch now sits in the row of tools.
- [ ] Reorder/sort boards in the list. The list is sorted by last change; a manual order and other
      sort keys are missing.

## Future cool stuff

- [~] Shape recognition: draw and hold makes lines, rectangles/squares, circles/ellipses; trimming a
  drawn shape back to hand-drawn ink is in on top of that. Hold and drag to resize is missing — after
  the hold the shape is finished; pick it up with the lasso to move it. See
  [docs/lasso-und-formen.md](docs/lasso-und-formen.md).
- [x] Lasso: a loop around strokes picks them up as a group, which can then be carried elsewhere or
  thrown away, each in a single undo step. Chosen with the button in the toolbar; the button on a
  Windows pen picks it up while it is held, and the Apple Pencil double-tap reaches it only where
  iOS's own double-tap setting is "previous tool" (see
  [docs/lasso-und-formen.md](docs/lasso-und-formen.md)).
- [ ] Board tags for grouping and filtering.
- [~] Widget with multiple boards: every widget picks its board, and the long press in the overview is only the suggestion. Selecting by tag and by last edited is missing.
- [ ] Share selected columns/cards (the export knows the whole board or all of its cards, nothing in between).
- [~] Card pins: emoji tags (fixed set, max 4 per card) are in and can be filtered in the board header. Hand-drawn pins and a set per board are missing.
- [~] Group/filter cards: text search and the tag filter are in the board header. The colour filter and grouping are missing.
- [ ] Radial menu: activate by dragging (drag activates, release confirms) and choose which actions the ring holds.
- [ ] Change the card colour from the board view: a palette at hand, or a palette change reaching every card of that colour.
- [ ] Gesture: three-finger drag changes the pen thickness.
- [ ] Legend: shows what a colour means.
- [ ] Pen smoothing.
- [ ] Hide columns.
- [ ] More flexible card placement in columns: cards beside each other or half stacked, empty space, "move all below this line up/down".
- [ ] Linux support. Not possible with MAUI as it stands: it would need a second desktop branch (Avalonia) and its drawing surface.
- [ ] Sync across devices. `LocalOnlySyncService` is a placeholder that keeps everything local; `SyncableEntity` and `SyncVersionTag` are only prepared.

## UI / UX

- [ ] Add a "+" symbol to the add-column button.
- [ ] Inconsistent button display: some have a background, some don't; the toggle buttons look different from normal buttons.
- [ ] Use consistent icons for import/export in the different places.
