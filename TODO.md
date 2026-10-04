# Penban TODO

Taken from the "Penban Feedback" board, grouped by column. `[x]` = done, `[~]` = partly done.
The QoL and "Future cool stuff" columns are written out in German as well, with the file and the
symbol behind every verdict: [docs/feedback-qol-cool-stuff.md](docs/feedback-qol-cool-stuff.md).
QoL 1, 2, 3, 4 and 7 are built (PR #48); the lasso and the shape recognition sit on top of that
(PR #49). All of it goes out together in 0.5.3, along with the board loading fix from 0.5.2. Both are
written out in German, with the files and symbols behind them:
[docs/lasso-und-formen.md](docs/lasso-und-formen.md).

0.5.4 adds the rest of the shape card — holding with the pen still down, pulling the shape into its
size, and triangles/pentagons/hexagons — and fixes the undo stack getting out of step with a picked-up
group (see below). It also lets the board cards be dragged into an order of their own, keeps the pen
tap that closes a lasso from leaving a dot behind, and gives the card page the same header as the
other pages.

0.5.5 adds notes that are **typed** instead of written: a card can be filled in on the keyboard, in as
many lines as fit, with a title (optional), one size and one colour for the whole note, and bold or
italic. The switch sits at the head of the toolbar and swaps the row of drawing tools for the text
one; a setting decides which mode a new, empty card starts in, and whether the ink steps back while
typing. Both kinds of text are searchable in the same search — typed text needs no reading, so it is
findable the moment it is saved. All of it is written out in German in
[docs/textfeld-notizen.md](docs/textfeld-notizen.md).

## Bugs

- [x] The undo stack and a picked-up group got out of step: erasing a picked-up stroke left the blue
  frame standing around strokes that were already gone, so a later contact inside that frame recorded a
  step of the history that did nothing at all — one undo that did nothing, and the next one brought
  back strokes erased long before. A selection is now pruned whenever a stroke leaves the note, and a
  carried group only ever records the strokes that are really in it (0.5.4).
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
- [~] Reorder/sort boards in the list: a board card can be dragged onto another one and the new
  order is stored (`SortOrder`), so the overview keeps it. Other sort keys and a "sort by" switch are
  missing.

## Future cool stuff

- [x] Shape recognition: draw and hold with the pen still down makes lines, rectangles/squares,
  circles/ellipses, and triangles/pentagons/hexagons (the rings molecules are drawn from); the pen
  that is still down then pulls the shape into its size ("hold and drag to resize") — the box corner
  across from the pen stays put, a circle pulled sideways becomes an ellipse, and a line follows the
  pen at its far end. Recognising a shape is a single undo step, trimming it back to hand-drawn ink
  is in on top of that. See [docs/lasso-und-formen.md](docs/lasso-und-formen.md).
- [x] Lasso: a loop around strokes picks them up as a group, which can then be carried elsewhere or
  thrown away, each in a single undo step. Chosen with the button in the toolbar; the button on a
  Windows pen picks it up while it is held, and the Apple Pencil double-tap reaches it only where
  iOS's own double-tap setting is "previous tool" (see
  [docs/lasso-und-formen.md](docs/lasso-und-formen.md)). A group that is picked up is carried by the
  pen tip whatever tool is in hand, and a contact away from it puts it down first.
- [x] Notes with a text field: a card can be written on the keyboard instead of with the pen —
  several lines, an optional title, one font size and one colour for the note, bold and italic, all
  in a second toolbar the switch at the head of the row leads to. Nothing is lost when switching:
  the pen mode shows ink and text, the text mode steps the ink back (a setting), and both stay
  searchable. The mode a new, empty card starts in is a setting as well (0.5.5, see
  [docs/textfeld-notizen.md](docs/textfeld-notizen.md)).
- [ ] Board tags for grouping and filtering.
- [~] Widget with multiple boards: every widget picks its board, and the long press in the overview
  that used to suggest one is gone (it blocked the long press that dragging a board card needs).
  Selecting by tag and by last edited is missing.
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
