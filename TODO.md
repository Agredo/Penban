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

0.5.6 clears five entries off the bug list: the pressure a stroke was written with is read on the
board as well, so a lightly written line stays light there instead of coming out at full width; the
row of colours used last fills up with the colours a note was actually **written** in rather than the
ones the picker was dragged across on the way; a recognised shape can be rubbed out along its sides
and not only at its corners; a frame that was picked up is put down by the eraser button and by a
tap on the group itself, so it no longer takes hold of what is drawn or rubbed out inside it; and the
red drop area of the board keeps to the drag it belongs to, so holding a note down without carrying it
anywhere no longer leaves it standing over the board. The paper palette also trades its second shade of
orange for a purple, and the picker lays its swatches out in hue order (orange, yellow, green, blue,
purple, pink) instead of in the order a note stores them, so no two neighbours in the row are the same
colour twice; the stored order itself is untouched, which keeps every note in the colour it was given.

0.6 adds **projects**: a project collects boards under itself and gets a page of its own, the boards
overview grows a row of filter chips for the tags a board carries, and creating or editing a board
opens a page of its own with an expander for tags, start and end date and the project it belongs to.
The start page is now the dashboard — the last three projects in a horizontal row, the last three
boards below it, each with a way to all of them — and a setting decides whether the app starts there,
on the projects or on the boards. A Shell flyout reaches all three areas and the settings, help,
feedback, privacy and about pages; the board card is one control now, so the dashboard draws the same
card as the boards overview, only without the buttons. A backup carries the projects as well. All of
it is written out in German in [docs/projekte.md](docs/projekte.md).

## Bugs

- [x] The undo stack and a picked-up group got out of step: erasing a picked-up stroke left the blue
  frame standing around strokes that were already gone, so a later contact inside that frame recorded a
  step of the history that did nothing at all — one undo that did nothing, and the next one brought
  back strokes erased long before. A selection is now pruned whenever a stroke leaves the note, and a
  carried group only ever records the strokes that are really in it (0.5.4).
- [ ] Swipe down on a card only works in the bottom half of the card, and when swiping horizontally in the middle of it.
- [x] Lines drawn with pen pressure always render at maximum width on the board, which makes them hard to read. The board now reads the pressure off the stroke and draws it with the mean width the editor would give its segments, so light and hard lines can be told apart there too (0.5.6).
- [x] Editing the title card sometimes changes its background colour when exiting to the menu. The stored
  colour is no longer wiped, but the editor compared the swatch the user tapped with the colour the note
  *showed*, and a note that was never coloured shows the shade its id derives: tapping exactly that shade
  was taken as "nothing changed" and never written down, so the note fell back to the derived one. The
  picked colour is now told apart from the derived one, and the row is told when the colour of a board it
  already shows has moved (0.5.6).
- [x] Strokes change appearance when switching pen pressure on/off (the flag is now stored per stroke).
- [x] A recognised shape could only be rubbed out at its corners: the hit test looked at the points a
  stroke was written with, and a shape keeps nothing but its corners, since its edges are drawn
  between them. The eraser now measures the distances to those edges as well (0.5.6).
- [x] The frame of a picked-up group stayed where it was, over the note, and kept the drawing under it:
  a contact inside the frame was taken as carrying the group, so pressing there moved the group instead
  of drawing or rubbing out. The eraser button and a tap on the group now put it down (0.5.6).
- [x] The row of pen colours used last filled up with the colours the picker was dragged across rather
  than the ones that were used: it is written once a stroke has been drawn, and colours that cannot be
  told apart are folded into one (0.5.6).
- [x] Holding a note down without carrying it anywhere left the red drop area standing over the board,
  and it only went away once another note was dragged. The control reports the end of a drag that
  missed every column to nobody, so the note is now taken down as the one in the air when the drag
  starts, and what puts the drop area back is the first move the control reports — a note that is only
  held stays where it is without anything on the board changing — the end of the drag, the first touch
  anywhere on the board, and a tap on the note the drop area is covering (0.5.6).

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
  searchable — an import writes the typed notes into the search as it brings them in. The mode a new,
  empty card starts in is a setting as well (0.5.5, see
  [docs/textfeld-notizen.md](docs/textfeld-notizen.md)).
- [~] Board tags for grouping and filtering: a board carries free-text tags, they can be typed in the
  board form and the boards overview filters by them with a chip each. Grouping and a tag list per
  board are missing.
- [~] Widget with multiple boards: every widget picks its board, and the long press in the overview
  that used to suggest one is gone (it blocked the long press that dragging a board card needs).
  Selecting by tag and by last edited is missing.
- [x] Compressed `.penban` files: the export is written through gzip, which takes a board with a
  page of handwriting to about an eighth of its size (9.66 MB → 1.12 MB on a real board with 55
  cards, in 90 ms), and the reader takes both the compressed file and the plain JSON every earlier
  version wrote — the two are told apart by gzip's own magic number, so nothing had to be declared or
  migrated. An install older than 0.5.5 cannot read a file written after it (it reports it as
  unreadable rather than importing half of something). See
  [docs/textfeld-notizen.md](docs/textfeld-notizen.md).
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
