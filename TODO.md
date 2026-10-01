# Penban TODO

Taken from the "Penban Feedback" board, grouped by column. `[x]` = done, `[~]` = partly done.

## Bugs

- [ ] Swipe down on a card only works in the bottom half of the card, and when swiping horizontally in the middle of it.
- [ ] Lines drawn with pen pressure always render at maximum width on the board, which makes them hard to read.
- [~] Editing the title card sometimes changes its background colour when exiting to the menu. The stored colour is no longer wiped; the root cause is not reproduced yet.
- [x] Strokes change appearance when switching pen pressure on/off (the flag is now stored per stroke).

## Important features

- [ ] Reorder columns.
- [ ] Pick your own palettes: pen colours and card colours.
- [ ] Selection tool: long press to activate; transform, copy and paste, delete the selection.

## QoL

- [ ] Undo after erase should restore all strokes erased since the pen went down, not only the last one.
- [ ] Picking a colour automatically switches to the pen.
- [ ] Delete cards directly from the board view (e.g. a trash can when dragging a card).
- [ ] Board overview: show only the title card instead of laying it on top of the card stack.
  - Fit it to size so the title card is easier to tell apart from a normal card stack.
  - Keep the card stack when no title card is given.
- [ ] Connect the title card's background colour with the board colour (the card used for the card count in the column overview).
- [ ] Add a pen pressure toggle to the card draw toolbar.
- [x] Scroll to the board after exiting to the main menu.
- [~] Reorder/sort boards in the list. The list is sorted by last change; tags and creation date are missing.

## Future cool stuff

- [ ] Shape recognition: draw and hold makes lines, rectangles/squares, circles/ellipses; hold and drag to resize. Maybe just straight lines at the beginning.
- [ ] Board tags for grouping and filtering.
- [ ] Widget with multiple boards: select by tag and last edited. Probably wait for board tags.
- [~] Card tags/pins: emoji, stickers or hand-drawn pins as tags on cards (one set per board). Emoji tags (fixed set, max 4 per card) can be pinned in the editor and show on the note; stickers, hand-drawn/per-board sets are missing.
  - Later: filter/group cards by these tags.
- [ ] Share selected columns/cards.
- [ ] Gesture: three-finger drag changes the pen thickness.
- [ ] Group/filter cards by colour and by search text.

## UI / UX

- [ ] Add a "+" symbol to the add-column button.
- [ ] Inconsistent button display: some have a background, some don't; the toggle buttons look different from normal buttons.
- [ ] Use consistent icons for import/export in the different places.
