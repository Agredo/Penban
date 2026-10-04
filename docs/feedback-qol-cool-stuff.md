# Feedback-Board: QoL und Future cool Stuff

Quelle: Board „Penban Feedback", exportiert am 2026-10-03T16:27:51Z (`.penban`), die Spalten
**QoL** (9 Karten) und **Future cool Stuff** (15 Karten). Die Karten sind von Hand geschrieben, also
über die Striche gelesen und nicht aus einem Textfeld kopiert.

Abgeglichen mit `main` (`f52ee4e`, Version 0.5.2, TestFlight-Build 14). Jeder Punkt darunter sagt,
was davon schon da ist und wo im Quelltext das steht — damit die Liste nur noch das enthält, was
wirklich fehlt.

Nachgezogen bis PR #48: die QoL-Karten 1, 2, 3, 4 und 7 sind damit gebaut (noch nicht
veröffentlicht) und stehen unten unter „Nicht mehr auf der Liste".

Nachgezogen bis PR #49 (Lassowerkzeug und Formerkennung): **Future 1** ist damit von „offen" auf
„teilweise" gerutscht — die Formen sind gebaut, das Ziehen nach dem Halten fehlt noch. Dazu kommt das
Lassowerkzeug, das auf keiner Karte steht und deshalb nur im Abschnitt „Ohne Karte dazu" (ganz unten)
und in [docs/lasso-und-formen.md](lasso-und-formen.md) auftaucht.

Nachgezogen bis Version 0.5.4: **Future 1** ist damit **erledigt** — das Halten passiert jetzt mit
aufgesetztem Stift, und der Stift zieht die Form anschließend in ihre Größe; dazu Dreieck, Fünfeck und
Sechseck (für die Chemie). Die Wartezeit ist dieselbe wie vorher, sie beginnt nur früher.

Nachgezogen bis Version 0.5.5 (Textfeld-Notizen): auf keiner Karte des Boards, sondern eine eigene
Bitte — eine Notiz lässt sich auf der Tastatur schreiben statt mit dem Stift. Steht deshalb in
„Ohne Karte dazu" und in [docs/textfeld-notizen.md](textfeld-notizen.md).

Legende: **offen** = nichts davon ist gebaut · **teilweise** = ein Teil ist gebaut, der Rest steht
dabei · **erledigt** = der Punkt ist abgehakt und steht nur noch unter
„Nicht mehr auf der Liste" (ganz unten).

## QoL

Auf der Liste bleiben 2 der 9 Karten: beide ganz offen. Die Nummern sind die Karten des Boards,
deshalb fehlen hier QoL 1, 2, 3, 4, 5, 7 und 8 — sie stehen unten unter
„Nicht mehr auf der Liste".

### QoL 6 (Karte 6) – Übersicht zeigt nur die Titelkarte · **offen**

> Display only title card in board overview instead of title card on top of card stack; fit to size
> so the title card is easier to tell apart from a normal card stack; a card stack is great when no
> title card is given.

Die Notiz des Boards führt den Stapel an und nimmt einen der drei Plätze ein, geschriebene Karten
füllen den Rest (`BoardViewModel.LoadSummaryAsync`, `:173` ff., `PreviewNoteLimit = 3`) — sie liegt
also weiterhin **auf** dem Stapel statt allein zu stehen. Das zweite, kleinere Anliegen der Karte
(größer, damit man die Titelkarte vom Stapel unterscheiden kann) fehlt ebenfalls.

### QoL 9 (Karte 9) – Boards in der Liste sortieren · **teilweise**

> Reorder/sort boards in list.

Erledigt: Eine Boardkarte lässt sich auf eine andere ziehen und legt sich dort ab
(`BoardsPage.xaml`: `DragGestureRecognizer`/`DropGestureRecognizer` an beiden Kartenvorlagen,
`BoardsPage.xaml.cs`: `OnBoardDragStarting`/`OnBoardDrop`; `BoardsViewModel.MoveBoardAsync`). Die
Reihenfolge steht in `Board.SortOrder`, wird von `BoardService.ReorderBoardsAsync` in einem Zug
geschrieben und von `GetBoardsAsync`/`CompareRows` zuerst gelesen — sie überlebt also den Neustart.
Offen: andere Sortierschlüssel (Name, angelegt am) und eine Gruppierung. Ein Board, das vor 0.5.4
angelegt wurde, hat `SortOrder == 0` und fällt auf „letzte Änderung" zurück.

## Future cool Stuff

Auf der Liste bleiben 14 der 15 Karten: Future 1 ist erledigt und steht unten unter „Nicht mehr auf der
Liste", elf sind offen, drei teilweise.

### Future 2 (Karte 2) – Board-Tags · **offen**

> Board tags – for grouping/filtering.

Ein Board hat keinen Platz für Tags (`Penban.Models/Board.cs`: Titel, Spalten, Notiz, Notizfarbe).
Tags gibt es nur auf Karten (Emoji, bis vier), und der Filter im Board-Kopf filtert Karten.

### Future 3 (Karte 3) – Widget mit mehreren Boards · **teilweise**

> Widget with multiple boards – select by tag & last edited → probably wait for board tags.

Erledigt: Jedes Widget wählt sein Board selbst, im Konfigurationsblatt aus der Liste
(`PenbanBoardIntent`/`PenbanBoardQuery`, `Penban.Widget.iOS/Sources/`). Der frühere lange Druck auf
eine Boardkarte (0.5.0), der das Board als Vorschlag nach vorn stellte, ist in #37 wieder entfernt
worden — er hat den langen Druck belegt, den das Umsortieren per Drag & Drop braucht.
Offen: die Auswahl nach Tag und nach „zuletzt geändert" — beides braucht die Board-Tags
(Future 2).

### Future 4 (Karte 4) – Auswahl teilen · **offen**

> Share selected columns/cards.

Der Export kennt heute nur „Board" oder „alle Karten eines Boards"
(`TransferCoordinator`, `ExportScope.Board`/`Cards`). Eine Auswahl, die man teilen könnte, gibt es
nicht — sie hängt am Auswahlwerkzeug (Spalte „Important Features": Auswahl per langem Druck,
verschieben, kopieren, löschen).

### Future 5 (Karte 5) – Karten-Pins · **teilweise**

> Card pins = Tags, e.g. …; Bonus: hand drawn set per board.

Erledigt: Emoji-Tags auf Karten (fester Satz, bis vier pro Karte), sichtbar auf der Notiz, und der
Tag-Filter im Board-Kopf (0.5.0: 0d69743, acb1c07, 90c95ae). Offen: der Bonus — selbst
gezeichnete Pins und ein eigener Satz pro Board.

### Future 6 (Karte 6) – Karten gruppieren und filtern · **teilweise**

> Group/filter cards – by color, search text.

Erledigt: Textsuche über den gelesenen Notiztext und Filter nach Tags, beides im Board-Kopf
(0.5.0). Offen: Filter nach Kartenfarbe und jedes Gruppieren (nach Farbe, Tag, Spalte).

### Future 7 (Karte 7) – Ring per Ziehen · **offen**

> Radial menu drag activation → drag activates, release confirms; would only work with one layer of
> actions – maybe setting for which actions are there (colors, thickness, tools pen/eraser/undo/
> redo/lasso…) – not that important until more tools are available.

Der Ring öffnet mit einem Tipp des freien Fingers auf die Notiz (oder Rechtsklick), nicht durch
Ziehen; die Aktionen darin sind fest (Farben, Dicken, Rückgängig/Wiederholen, Stift/Finger), nicht
wählbar. Das Ziehen als Auslöser und „loslassen bestätigt" fehlen. Ein Lassowerkzeug gibt es
inzwischen, es sitzt aber in der Leiste und nicht im Ring (siehe unten).

### Future 8 (Karte 8) – Kartenfarbe im Board ändern · **offen**

> Change card color from board view – radial menu with pen? drag and drop from palette; changing
> palette then changes all cards with that color.

Kartenfarben gibt es nur im Notiz-Editor (`ColorPicker`, aus der Palette). Im Board lässt sich
weder eine Farbe auf eine Karte ziehen noch greift ein Farbwechsel später auf alle Karten dieser
Farbe durch.

### Future 9 (Karte 9) – Zwei/Drei-Finger-Ziehen ändert die Dicke · **offen**

> Gesture – 3 finger drag pen thickness.

Zwei-Finger-Tipp ist Rückgängig, Drei-Finger-Tipp ist Wiederholen
(`MultiFingerTapBehavior`). Ein Ziehen mit mehreren Fingern ändert nichts; die Dicke kommt aus dem
Flyout oder dem Ring.

### Future 10 (Karte 10) – Legende · **offen**

> Legend → shows what color means what.

Es gibt keine Legende und keine Beschriftung der Farben auf dem Board. Kartenfarben sind reine
Papierfarben; was eine Farbe bedeuten soll, muss man sich merken.

### Future 11 (Karte 11) – Striche glätten · **offen**

> Pen smoothing.

Es gibt keinen Glättungsschalter. Geschrieben wird so, wie der Finger bzw. der Stift schreibt (nur
die Breite folgt dem Druck, wenn der Schalter an ist); `InkLineRenderer` in `Penban.Recognition`
zeichnet Zeilen für die Erkennung und ist kein Zeichen-Werkzeug.

### Future 12 (Karte 12) – Spalten ausblenden · **offen**

> Hide columns.

Eine Spalte hat Titel, Reihenfolge und Karten (`BoardColumn`) — nichts, womit man sie aus- und
wieder einblenden könnte. Der Board-Kopf filtert nur Karten.

### Future 13 (Karte 13) – Freieres Anordnen in der Spalte · **offen**

> More flexible card placement in columns – e.g. half stacking cards to mark dependencies, next to
> each other, place freely in column; add tools to add/remove empty space; move all below line
> up/down.

Karten sind eine geordnete Liste je Spalte (`SortOrder`), gezogen wird zwischen den Spalten (ganz
nach oben oder an eine Position). Leerraum, Karten nebeneinander oder halb übereinander und ein
Werkzeug für die Anordnung fehlen.

### Future 14 (Karte 14) – Linux · **offen**

> Linux support – I know it's not possible right now, but let a girl dream.

Die App baut für Windows, iOS, Mac Catalyst und Android
(`Penban.Maui/Penban.Maui.csproj`, `TargetFrameworks`). Linux ist mit MAUI nicht dabei: dafür
bräuchte es einen eigenen Desktop-Zweig (z. B. Avalonia) samt Zeichenfläche — kein kleiner Wunsch,
sondern ein zweiter Kopf.

### Future 15 (Karte 15) – Geräteübergreifender Abgleich · **offen**

> Sync across devices.

Vorbereitet, aber nicht gebaut: `SyncableEntity` und `SyncVersionTag` stehen schon an den
Datensätzen, und `LocalOnlySyncService` ist genau das, was der Name sagt — ein Platzhalter, der
alles lokal lässt. Es gibt keinen Server, kein Konto und keine Übertragung.

## Nicht mehr auf der Liste

Erledigt und deshalb aus der Liste gestrichen:

| Punkt | Stand |
| --- | --- |
| QoL 1 – „Pen thickness sub-menu" | Der Tipp daneben schließt das Flyout, und die Dicke ist am Stift, an der Zahl und im Ring zu sehen (0.5.2, PR #45). Seit PR #48 steht sie zusätzlich direkt in der Leiste: Regler und Zahl neben den Stiften (`ToolbarThicknessSlider`/`ToolbarThicknessValue`, `CardInkEditorPage.xaml:89`), der Bereich kommt aus `PenThickness`; der Stift in der Hand verstellt sie unterwegs (`OnPenSlotPanned`). |
| QoL 2 – „Undo after erase" | Ein Radierkontakt ist ein Edit: `EraseSession` sammelt den ganzen Zug und legt ihn einmal in die Historie — für den Radierer und für die Rückseite des Stifts (PR #48, `SkiaInkCanvasView.cs`). |
| QoL 3 – „Picking color auto switch to pen" | Eine gewählte Farbe legt den Radierer weg: Palette, Ring und Farbbänder setzen `InkHost.IsEraserMode = false` (PR #48). |
| QoL 4 – „Delete cards from board view directly" | Auswahlmodus mit Häkchen an den Notizen und Leiste (`SelectButton`, `SelectionBar`), Verschieben in eine andere Spalte (`MoveCardsAsync`) und Löschen mit Rückfrage (`DeleteCardsAsync`). Beim Ziehen einer Notiz füllt sich die Löschfläche über dem Board rot (`TrashDropArea`, `SetBinHot`/`PaintBin`) — PR #48. |
| QoL 5 – „Clear card button in toolbar" | Der Knopf ist da: `ClearButton` mit `ClearNote` in der Zeichenleiste (`CardInkEditorPage.xaml:84`), löscht die Tinte einer Notiz in einem Schritt. |
| QoL 7 – „Add pen pressure toggle to card draw toolbar" | Der Schalter sitzt als Knopf in der Leiste (`PressureButton`, `OnPressureClicked`, Glyph `InkStrokeArrowUpDown`) und zeigt zwei Texte, `PressureWidthOn`/`PressureWidthOff` (PR #48). |
| QoL 8 – „Scroll to board after exiting to main menu" | Die Übersicht rollt beim Zurückkommen zum zuletzt geöffneten Board (`BoardsPage.xaml.cs:95/169`, `ScrollToLastOpenedBoard`, ohne Animation). |
| Future 1 – „Shape recognition" | Zeichnen und kurz halten macht die Form daraus, und der noch aufgesetzte Stift zieht sie anschließend in ihre Größe („hold & drag to resize", 0.5.4). Erkannt werden Linie, Rechteck/Quadrat, Kreis/Ellipse sowie Dreieck, Fünfeck und Sechseck — die Vielecke sind für die Chemie da. Gehalten wird mit **aufgesetztem** Stift 700 ms ohne Bewegung (`SkiaInkCanvasView.StartShapeHold`); jeder weitere Punkt startet die Wartezeit neu, deshalb wird aus einem „o" oder einer „8" nie eine Form. Gezogen wird um die Ecke der Box, die dem Stift gegenüberliegt, ein Kreis wird dabei zur Ellipse; bei der Linie folgt der zweite Punkt dem Stift. Die Form wird in denselben Strich geschrieben und ist ein einziger Rückgängig-Schritt. Schalter „Linie, Rechteck und Ellipse nach kurzem Halten begradigen" (`Draw.ShapeRecognitionEnabled`). Alles Weitere in [docs/lasso-und-formen.md](lasso-und-formen.md). |
| Future 3, Teil 1 – Board pro Widget | Jedes Widget hat seine eigene Board-Auswahl (`PenbanBoardIntent`); der lange Druck in der Übersicht ist nur noch der Vorschlag. |
| Future 5, Teil 1 – Karten-Pins als Tags | Emoji-Tags auf Karten (bis vier) samt Filter im Board-Kopf (0.5.0). |
| Future 6, Teil 1 – Textsuche und Tag-Filter | Beides im Board-Kopf (0.5.0, Suche über den gelesenen Notiztext). |

## Ohne Karte dazu

Das **Lassowerkzeug** stand auf keiner Karte des Boards; es kam als eigene Bitte dazu (PR #49). Ein
Knopf in der Zeichenleiste (`LassoButton`, `CardInkEditorPage.xaml:63`) macht es zum Werkzeug, eine
gezogene Schleife nimmt alle Striche auf, die ganz in ihr liegen, und die Gruppe lässt sich als Ganzes
verschieben oder mit dem Papierkorb im Werkzeug wegwerfen — je ein Rückgängig-Schritt
(`SkiaInkCanvasView`: `HandleLassoTouch`, `StrokesInsideLoop`, `SelectionDrag`, `DeleteSelection`).

Die Griffe am Stift sind dabei die Standardgriffe der Plattform: der Knopf an einem Windows-Stift
schaltet das Lasso ein, solange er gedrückt ist (`PenButtonLassoBehavior.cs`,
`Draw.PenButtonLassoEnabled`), und auf iOS folgen Doppeltipp und Drücken des Apple Pencil der
iOS-Einstellung dafür (`PencilTapBehavior.cs`, `Draw.PencilDoubleTapEnabled`). Alles Weitere steht in
[docs/lasso-und-formen.md](lasso-und-formen.md).

Die **Textfeld-Notizen** standen ebenfalls auf keiner Karte des Boards; sie kamen als eigene Bitte
dazu (0.5.5). Eine Karte lässt sich auf der Tastatur schreiben statt mit dem Stift: mehrere Zeilen,
ein Titel, der keiner sein muss, eine Schriftgröße und eine Schriftfarbe für die ganze Notiz, fett
und kursiv. Der Umschalter sitzt am Kopf der Leiste und tauscht die Zeichenwerkzeuge gegen die
Textwerkzeuge (Schriftgröße, Farbe, fett, kursiv) — der Knopf in der jeweiligen Leiste trägt das
Werkzeug, zu dem er führt. Umgeschaltet wird nichts gelöscht: im Stiftmodus stehen Tinte **und** Text
auf der Karte, im Textfeldmodus tritt die Tinte zurück (`Draw.HideInkInTextMode`, Standard an), und
eine Einstellung entscheidet, in welchem Modus eine neue, leere Karte beginnt
(`Card.DefaultContentMode`). Beide Texte sind durchsuchbar, der getippte sofort und ohne Erkennung.
Alles Weitere in [docs/textfeld-notizen.md](textfeld-notizen.md), die Erkennungsseite in
[docs/texterkennung.md](texterkennung.md).

Zur Einordnung: Die Spalte „Done 🥳" des Boards enthält genau die Karten, die zu diesen Punkten
gehören (u. a. „Pressure sensitivity (toggle button in card view?)", „Search in notes", „Export",
„Widget for board quick access"). Die QoL-Karten 1 bis 4 und 7 stehen dort noch nicht, sie kamen
erst mit PR #48.

Die Datei `TODO.md` im Wurzelverzeichnis führt dieselbe Liste auf Englisch und ist im selben Zug
nachgezogen.
