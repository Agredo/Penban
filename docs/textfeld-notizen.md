# Karten mit Textfeld

Plan für Karten, die getippten Text tragen können. Stand: nach 0.5.4 (PR #54), `main` = `895f914`.
Der Plan ist noch nicht gebaut. Die Entscheidungen stehen unten unter „Entschieden"; offen ist nichts
mehr außer der Frage, wann gebaut wird.

## Was gebaut werden soll

1. Eine Karte kann **getippten Text** tragen: Titel (freiwillig) und Body, mehrere Zeilen.
2. Ein Schalter in der Zeichenleiste schaltet zwischen **Stift** und **Textfeld** um. Im Textfeldmodus
   schreibt die Tastatur, im Stiftmodus wieder der Stift. Umgeschaltet wird nichts gelöscht: der Text
   steht in beiden Modi auf der Notiz, die Tinte ist im Textfeldmodus ausgeblendet (abschaltbar).
3. Mit dem Textfeldmodus kommt eine **Text-Leiste** (Schriftgröße, Farbe, fett, kursiv), die sich an
   derselben Stelle wieder auf die normale Leiste zurückschalten lässt.
4. Beides ist **durchsuchbar** — geschriebene und getippte Notizen landen in derselben Suche.
5. Eine Einstellung sagt, **womit eine neue Notiz startet**.

## Modell

`Penban.Models/Card.cs` bekommt einen Modus und sechs Felder:

```csharp
public enum CardContentMode { Ink = 0, Text = 1 }

/// <summary>Was die Karte zeigt. 0 = Tinte, damit jede vor 0.5.5 geschriebene Karte unverändert bleibt.</summary>
public CardContentMode Mode { get; set; }

/// <summary>Titel der Textnotiz, freiwillig. null/leer = es gibt keinen.</summary>
public string? TextTitle { get; set; }

/// <summary>Der eigentliche Text, mehrzeilig. null = nie getippt.</summary>
public string? TextBody { get; set; }

/// <summary>Schriftgröße des Bodys in Dokumenteinheiten, wie eine Strichbreite.</summary>
public float TextSize { get; set; }

/// <summary>Schriftfarbe als #RRGGBB, wie die einer Feder.</summary>
public string? TextColorHex { get; set; }

/// <summary>Fett für die ganze Notiz - Titel und Body zusammen.</summary>
public bool TextBold { get; set; }

/// <summary>Kursiv für die ganze Notiz.</summary>
public bool TextItalic { get; set; }
```

Warum so:

- **`Mode` mit `Ink = 0`**: Der Standardwert einer `enum` ist 0, also liest jede bestehende Karte
  weiterhin „Tinte". Keine Migration, kein Feld, das auf `null` geprüft werden muss.
- **`TextSize` in Dokumenteinheiten**: genau die Einheit, in der auch `InkStroke.Thickness` liegt
  (`InkDocument.Size = 1000`). Nur so kann der Editor die Größe auf seine Fläche umrechnen und die
  Board-Vorschau mit einem einzigen Faktor dasselbe zeigen — die Begründung steht schon bei
  `InkDocument` und gilt hier genauso.
- **`TextColorHex` als `#RRGGBB`**: dieselbe Form, die `PenColor` schreibt und liest; `PenColor.TryNormalize`
  prüft sie, ohne dass eine zweite Farb-Konvention entsteht.
- **Titelgröße nicht gespeichert**: der Titel wird mit einem festen Vielfachen des Bodys gezeichnet
  (1,5×, halbfett), siehe `CardText.TitleScale`.
- **Fett und kursiv je Notiz, nicht je Stück**: es gibt kein Speicherformat für Abschnitte, und damit
  auch nichts, was die Suche und die Board-Vorschau erst auseinandernehmen müssten. Getippter Text
  bleibt schlicht Text.

Dazu ein kleiner Helfer `Penban.Models/CardText.cs` (analog zu `PenThickness`):

- `Minimum`/`Maximum`/`Default` der Schriftgröße (20 / 160 / 48 Dokumenteinheiten), `Clamp`, `Step`
  für den Regler;
- `TitleScale` (1.5) und `DefaultColorHex` (`#000000`);
- `FontPointsFor(TextSize, sideInPixels)` — die eine Stelle, an der Dokumenteinheit in Punkte
  umgerechnet wird, damit Board und Editor nicht zwei Formeln haben.

## Editor (`Penban.Maui.Views/Pages/CardInkEditorPage.xaml`)

Die Notizfläche bleibt die quadratische Karte in ihrer Papierfarbe (`NoteFrame` → `NoteSurface`).
Der Text ist ein zweites Kind derselben Fläche und **immer sichtbar**; die Tinte wird im Textfeldmodus
ausgeblendet:

```
StickyNoteBorder NoteSurface
├─ InkCanvasHostView InkHost            (IsVisible = Mode == Ink || !HideInkInTextMode)
└─ ScrollView TextSurface               (immer da, wenn es Text gibt)
   └─ VerticalStackLayout
      ├─ Entry  TextTitleEntry          (Placeholder "Titel (freiwillig)")
      └─ Editor TextBodyEditor          (AutoSize=TextChanges, mehrere Zeilen)
```

- **Beides bleibt erhalten** — der Modus blendet nur aus, er löscht nichts. In dieser Fassung:
  - **Stiftmodus**: Tinte *und* Text sichtbar. `TextSurface` steht dann auf `IsReadOnly = true` und
    `InputTransparent = true`, damit der Text nur aussieht und die Berührungen wirklich auf der
    Tinte landen. Das ist derselbe Renderingpfad wie im Textmodus — es gibt keine zweite Darstellung,
    die auseinanderlaufen könnte.
  - **Textfeldmodus**: Tinte aus (`HideInkInTextMode`, Standard **an**), Text editierbar. Ist die
    Einstellung aus, bleibt die Tinte als blasser Hintergrund stehen.
- **Mehrzeilig** ist der `Editor` von Haus aus; er bringt Tastatur, Zeilenumbruch, Cursor und Auswahl
  mit. Das ist der Grund, warum hier kein selbstgezeichnetes Textfeld entsteht: ein eigener Editor mit
  Cursor und Auswahl wäre ein zweites Projekt.
- **Schriftgröße**: `CardText.FontPointsFor(TextSize, NoteSurface.Width)` — dieselbe Umrechnung wie
  die Tinte, neu berechnet bei `SizeChanged` der Notizfläche. So sitzt „Schriftgröße 48" auf dem iPad
  und auf dem Board an derselben relativen Stelle.
- **Langer Text rollt** innerhalb der Notiz (`ScrollView`), statt die Karte zu sprengen — die Notiz ist
  auf dem Board ein Quadrat fester Größe. Im Stiftmodus ist `TextSurface` nicht bedienbar, ein Text,
  der nicht in die Notiz passt, steht dort also nur bis zum unteren Rand sichtbar.
- **Fett und kursiv** setzen `FontAttributes` (`Bold`/`Italic`/`Bold, Italic`) auf Entry und Editor.
  Damit die Zeilenhöhe stimmt, wird die Schriftgröße je Attribut einmal gemessen, bevor sie gesetzt
  wird (`Measure`), sonst springt der Text beim Umschalten.
- **Tastatur**: Der Fokus wird erst gesetzt, wenn der Benutzer hineintippt, nicht beim Umschalten —
  sonst springt bei jedem Moduswechsel die Tastatur auf. Beim Zurückschalten auf Stift wird der Fokus
  abgegeben (`Unfocus()`), damit die Tastatur zugeht.
- Beim Verlassen der Seite (dieselbe Stelle, die heute die Tinte sichert:
  `OnDisappearing`/`SaveInk`) werden Titel, Body, Modus, Größe, Farbe, fett und kursiv in einem Zug
  gespeichert.
- Die zweite Notiz des Boards (`Board.NoteStrokes`, die Titelkarte) bleibt in diesem Schritt bei
  Tinte; sie ist keine Kanban-Karte. Falls sie später auch Text tragen soll, ist es dieselbe Mechanik
  an `Board` statt an `Card`.

## Leiste (`CardInkEditorPage.xaml` + Code-Behind)

Die Zeile über der Notiz bekommt einen zweiten Zustand statt einer zweiten Zeile:

- **Neuer Knopf am Kopf der Zeile**, vor `DrawModeButton`: `TextModeButton`, Glyphe `U+F4B8`
  (Keyboard; `U+E765` ist die *line*-Variante und im Fluent-Font nicht vorhanden), Beschriftung über
  `SemanticProperties.Description` aus `Strings`. Wie `DrawModeButton` trägt er den Zustand: hell =
  Stift, hervorgehoben = Textfeld.
- **`DrawingToolBar`** = die heutige `HorizontalStackLayout` in `ToolBar` (Stifte, Radierer, Lasso,
  Rückgängig, Dicken, Farben, Druck, Papierfarben).
- **`TextToolBar`** = eine zweite, ebenso scrollbare `HorizontalStackLayout`, `IsVisible` genau dann,
  wenn der Modus Text ist. Darin:
  - derselbe `TextModeButton` (an derselben Stelle, damit der Weg zurück nicht wandert),
  - Regler + Zahl für die Schriftgröße (Muster von `ToolbarThicknessSlider`/`ToolbarThicknessValue`,
    Bereich aus `CardText`),
  - die Farbfelder der Papier-/Federpalette, die hier `TextColorHex` schreiben statt die Feder,
  - zwei Umschaltknöpfe für **fett** und **kursiv**, die `TextBold`/`TextItalic` setzen und ihren
    Zustand tragen (Muster von `PressureButton`).
- **Beim Umschalten** räumt der Code-Behind den Zeichenzustand auf: eine getragene Lasso-Auswahl wird
  abgelegt (`FinishLasso()`), der Ring wird geschlossen, das Flyout zu. Sonst bliebe ein
  aufgenommener Stapel in der Hand, während die Tastatur schreibt.
- Der Ring (`RadialMenuView`) bleibt das Zeichenmenü für den Stift; der Textknopf sitzt nur in der
  Leiste.

- **Der Kopf jeder der beiden Zeilen trägt das andere Werkzeug**: in `DrawingToolBar` der
  Keyboard-Knopf (`TextModeButton`), in `TextToolBar` derselbe Knopf mit dem Stift-Glyph
  (`U+E8D8`, ohne Namen). Beide hängen an `OnTextModeClicked`; jeder zeigt also an, wohin das
  Drücken führt, und der Weg zurück wandert nicht. `TextModeButton.IsVisible` folgt
  `textTarget is not null` — eine Board-Notiz (`BoardNoteViewModel`) hat kein Textfeld.

Die neuen Texte kommen in `Penban.Maui/Resources/Strings` (de/en) wie alle anderen:
`TextMode`, `TextModeOn/Off`, `TextTitlePlaceholder`, `TextSizeTitle`, `TextColorTitle`,
`TextBoldTitle`, `TextItalicTitle`, `TextModeDefault`, `TextHideInkTitle`. (`CardStartsWith` stand
im Entwurf für die Board-Vorschau; die Karte zeigt statt einer Zeile „beginnt mit …" Titel und Rumpf
über der Tinte, deshalb gibt es den Text nicht.)

## Einstellungen

- Neuer Schlüssel in `Penban.Services.Abstractions/PreferenceKeys.cs`:
  `CardDefaultContentMode = "Card.DefaultContentMode"`, Werte `"ink"` (Standard) und `"text"`.
- Zweiter Schlüssel: `TextHideInkInTextMode = "Draw.HideInkInTextMode"`, Standard **an** — ob die
  Tinte im Textfeldmodus ausgeblendet wird. Ohne diese Einstellung wäre eine Karte, auf der beides
  steht, im Textmodus unleserlich; mit ihr kann man sie als Hintergrund behalten.
- `SettingsViewModel`: `ObservableProperty` für beide mit `ReadBool`/`Persist` wie
  `ShapeRecognitionEnabled` (:83, :189). Der Modus wird über `CardText.FormatMode`/`ParseMode` als
  `"ink"`/`"text"` geschrieben und gelesen, `Persist` hat dafür eine `string`-Überladung.
- `SettingsPage.xaml`: zwei Umschalter im Notiz-Bereich, Textvorschlag „Neue Notiz startet mit:
  Stift/Textfeld" und „Tinte ausblenden, während getippt wird". Gebaut sind „Neue Notizen starten mit
  der Tastatur" (`TextModeDefault`) und „Tinte beim Tippen ausblenden" (`TextHideInkTitle`); ein
  Schalter statt einer Auswahl, weil der Notiz-Bereich durchweg aus Schaltern besteht.
- **Wo der Startmodus gelesen wird**: in `CardService.CreateCardAsync` (neben `ReadLastNoteColor`) —
  dort entsteht die Karte, und der Dienst hat `IPreferences` schon im Konstruktor. Eine leere Karte,
  die der Benutzer dann doch mit dem Stift beginnt, schaltet ihren Modus beim ersten Umschalten um;
  der Startmodus ist nur der Start.
- **Wirkung**: Die Einstellung entscheidet nur, in welchem Modus eine *neue* (noch leere) Karte
  beginnt. Eine Karte, die schon Text oder Tinte trägt, öffnet in ihrem gespeicherten `Mode` — sonst
  würde eine Einstellung alten Notizen den Inhalt verdecken.

## Suche

Heute hält `LiteDbRecognitionStore` **eine** `RecognitionRow` je Karte, deren `NormalizedText` aus der
Handschrift kommt; `SearchAsync` vergleicht nur diese Spalte. Eine Textkarte hat keine Tinte und
bekommt deshalb heute gar keine Zeile — sie wäre unsichtbar. Der Plan schließt das:

1. `StoredRecognition` und `RecognitionRow` bekommen `TypedText` und `NormalizedTypedText`
   (beide `""` bei Handschrift-Karten, damit alte Zeilen unverändert gelesen werden).
2. `IRecognitionStore` bekommt `SaveTextAsync(cardId, rawText, normalizedText, ct)` — ohne Modell,
   ohne Warteschlange.
3. `CardService.SaveCardAsync` schreibt danach: `TypedText` = `TextTitle` + Leerzeile + `TextBody`,
   normalisiert mit demselben `TextNormalizer.Normalize`. Damit hat auch eine Karte mit null Strichen
   eine Zeile in der Suche.
4. `LiteDbRecognitionStore.SearchAsync` trifft, wenn `NormalizedText` **oder** `NormalizedTypedText`
   die Anfrage enthält; die Sortierung (zuletzt geschrieben zuerst) bleibt.
5. `SearchResultViewModel` bekommt den getroffenen Text statt fest `RawText` (neues Feld `MatchedText`
   plus `IsTyped`), damit ein Treffer auf einer Textkarte den getippten Text zeigt und einen Hinweis
   „getippt" tragen kann. Die Trefferkarte selbst (Tinte in Papierfarbe, Board, Tipp öffnet die Notiz)
   bleibt.
6. `RecognitionQueue.PendingCount` und „Vorhandene Notizen lesen" dürfen Textkarten nicht als
   ausstehend zählen — eine Textkarte ist nie „noch nicht gelesen".
7. `TextNormalizer` bleibt unangetastet; getippter Text geht durch dieselbe Normalisierung wie
   gelesener, sonst fände „Müller" das eine und nicht das andere.

`docs/texterkennung.md` bekommt einen Absatz und im Mermaid-Diagramm einen zweiten Eingang
(`TextTitle/TextBody → TextNormalizer → InkRecognitionService`).

## Board (`Penban.Maui.Views/Pages/BoardPage.xaml`)

In der Kartenvorlage (ab :260) kommt der Text **zu** der Tinte, denn auf dem Board ist eine Notiz
das, was auf ihr steht (das ist die Ansicht des Stiftmodus):

- `InkPreviewView` mit `Card.InkCanvas.Strokes` wie heute — bei einer Karte ohne Striche bleibt sie
  einfach leer.
- Darüber ein `Label`-Stapel (Titel halbfett mit 1,5× Größe, dann Body) in derselben Papierfarbe,
  Zeilenumbruch an, `LineBreakMode` mit Kürzung, damit eine lange Notiz die Karte nicht sprengt.
  Die Schriftgröße kommt aus derselben `CardText.FontPointsFor`-Umrechnung wie im Editor, bezogen auf
  die Kartengröße auf dem Board — dadurch sieht die Notiz auf dem Board aus wie im Editor.
- `TextBold`/`TextItalic` setzen auch hier `FontAttributes`, damit Board und Editor gleich aussehen.
- `CardViewModel` stellt dafür `HasText`, `TextTitle`, `TextBody`, die berechnete Größe und die
  Attribute bereit.

Damit braucht die Vorschau keine zweite Renderer-Implementierung: es sind dieselben MAUI-Labels wie im
Editor, nur ohne Eingabe (`InputTransparent = true`).

Unberührt bleiben Sortierung, Ziehen und Ablegen, der Auswahlhaken, Tags und die Papierfarbe. Zu
prüfen ist `AutoSizeCards` („Notizen folgen ihrem Inhalt"): heute misst es den Tinten-Rahmen; für eine
Textkarte müsste es aus der Zeilenzahl rechnen. In diesem Schritt bleibt die Textkarte auf der normalen
Kartengröße; das Nachführen kommt später, wenn es gebraucht wird.

## Widget

Das Widget zeigt die Notiz **des Boards** (`Board.NoteStrokes`), nicht die Karten — eine Textkarte
erreicht es also nicht. Bleibt außen vor. Sollte die Titelnotiz später Text tragen, braucht der
Widget-Schnappschuss (`InkRenderer`, `Penban.Recognition`) ein `ICanvas.DrawString`; das ist ein
eigener kleiner Schritt.

## Austausch, Speicherung, Sync

- `Penban.Services/DataTransferService.cs`, `CloneCard` (:258) kopiert eine feste Feldliste. Ohne
  `Mode`, `TextTitle`, `TextBody`, `TextSize`, `TextColorHex`, `TextBold` und `TextItalic` käme eine
  exportierte Textkarte leer zurück. Das ist die eine Stelle, die man beim Modell leicht vergisst.
  Auch `Board.SortOrder` gehört dazu: ohne das landet ein importiertes Board hinter allen anderen,
  egal wo es auf der anderen Seite stand.
- Der **Suchindex** wird beim Speichern über `CardService` gefüllt, ein Import schreibt aber direkt
  über die Repositories. Tinte heilt sich selbst (das Öffnen eines Boards stellt jede Karte mit
  Strichen in die Queue), getippter Text hat nichts zu lesen — eine importierte Textkarte wäre in der
  Suche unsichtbar, bis sie einmal geöffnet und gespeichert wird. Eine wiederhergestellte Sicherung
  käme auf einem neuen Gerät also ohne durchsuchbare Textnotizen an. `IndexTypedTextAsync` schreibt
  deshalb nach jedem Import für jede importierte Karte dieselbe Zeile, die ein Speichern schreibt —
  leer oder nicht, wie `CardService.SaveCardAsync` es auch tut.
- `PenbanFile.CurrentVersion` bleibt **1**: die neuen Felder sind optional, eine Datei aus einer
  älteren Version hat sie einfach nicht, und ein älterer Leser überliest sie (unbekannte
  JSON-Felder). Kein Versionssprung nötig.
- Seit 0.5.5 wird die Datei **gzip-komprimiert** geschrieben (`ExportAsync`, `OpenPayloadAsync`). Eine
  `.penban`-Datei ist fast nur Tinte, und Tinte ist fast nur Wiederholung: dieselben sechs
  Schlüsselnamen (`X`, `Y`, `Pressure`, `Tilt`, `Azimuth`, `TimestampMs`) und Zeitstempel mit
  gemeinsamem Präfix. Gemessen an einer echten Datei mit 55 Karten: 9,66 MB → 1,12 MB, also 8,6×, in
  90 ms (`CompressionLevel.Optimal`; `SmallestSize` war nicht kleiner, nur 3× langsamer). Brotli
  `Optimal` wäre 2 % kleiner gewesen — die Entscheidung fiel trotzdem auf gzip, weil gzip eine
  Magic Number hat (`1F 8B`) und Brotli nicht: der Leser erkennt das Format dadurch selbst, ohne
  Marker von uns, ohne Migration, ohne Zusatzfeld. `CurrentVersion` bleibt deshalb bei 1.
- Der Leser nimmt **beide Formate**. Er liest zwei Bytes und schaut auf `1F 8B`; dafür öffnet er die
  Datei über `PickedFile.OpenRead` ein zweites Mal, was dessen Vertrag ausdrücklich zulässt („opens a
  fresh stream every time it is called") und deutlich billiger ist, als eine ganze Sicherung im
  Speicher zu halten, um zwei Bytes ansehen zu können. `InvalidDataException` steht mit im `catch`:
  eine Datei, die behauptet gzip zu sein und es nicht ist, landet in derselben Meldung
  („The file is not a readable Penban file.") statt als Absturz.
- Die eine Richtung, die nicht gehen kann: eine Installation **vor** 0.5.5 kann eine danach
  geschriebene Datei nicht lesen. Sie meldet „not a readable Penban file", verliert also nichts und
  tut nichts Falsches — die Sicherung bleibt vollständig, nur das Einlesen scheitert. Deshalb der
  Schnitt bei 0.5.5 und nicht früher.
- LiteDB braucht keine Migration: fehlende Felder liest LiteDB als `null`/`0`, und `Mode = 0` ist
  genau „Tinte".
- `SyncableEntity` bleibt, wie es ist — `UpdatedAtUtc` wird beim Speichern gesetzt wie heute.

## Reihenfolge

| # | Schritt | Kern |
| --- | --- | --- |
| 1 | Modell | `Card`-Felder, `CardText`-Helfer, `CloneCard` nachziehen |
| 2 | Suche | Store, `SaveTextAsync`, `CardService`, `SearchViewModel`, Treffer-Text, Pending-Zählung |
| 3 | Editor | Textfläche, Fokus/Tastatur, Speichern, Schriftgrößen-Umrechnung |
| 4 | Leiste | Textknopf, Text-Leiste, Aufräumen beim Umschalten, neue Strings |
| 5 | Board | Textvorschau in der Kartenvorlage, `CardViewModel` |
| 6 | Einstellungen | Schlüssel, Schalter, Wirkung auf neue Karten |
| 7 | Doku | `docs/texterkennung.md`, `TODO.md`, `docs/feedback-qol-cool-stuff.md`, diese Datei |
| 8 | Auslieferung | Version, Release Notes, TestFlight (wie bei 0.5.4) |

Ein Testprojekt gibt es in `Penban.slnx` nicht; geprüft wird wie bisher auf dem iPad (Dev-Deploy) und
über TestFlight, mit den Prüfpunkten aus dieser Liste.

**Stand:** Die Schritte 1 bis 6 sind gebaut (`b9d9f66` Schritt 1+2, `e89ae8b` Schritt 3+4,
`38ee2f1` Schritt 5+6), Schritt 7 ist diese Datei und ihre Nachbarn, Schritt 8 ist die Auslieferung
als 0.5.5. Zwei Punkte sind gebaut, aber noch nicht auf dem Gerät geprüft: dass eine Textkarte nicht
als „ausstehend" gezählt wird und dass die Board-Vorschau nach dem Zurückkommen aus dem Editor
nachzieht.

## Entschieden

| Frage | Entscheidung |
| --- | --- |
| Verhalten beim Umschalten | Im Textfeldmodus wird die Tinte ausgeblendet (abschaltbar), im Stiftmodus sieht man **beides**. Der Modus löscht nichts. |
| Titelgröße | Fest 1,5× der Schriftgröße, halbfett. Kein eigener Regler. |
| Schrift | Systemschrift — auf Windows und iOS ohne mitgelieferte Datei, keine Lizenzfrage, kein zweiter Messpfad. |
| Startmodus | Gilt nur für **neue, leere** Karten; eine Karte mit Inhalt öffnet in ihrem gespeicherten Modus. |
| Langer Text | Rollt innerhalb der Notiz. Die Karte behält ihre Größe, auch auf dem Board. |
| Auszeichnung | Kein Rich-Text-Editor. Zusätzlich zu Größe und Farbe gibt es **fett** und **kursiv** als Schalter für die ganze Notiz. |
| Textschalter im Ring | Nur in der Leiste. Der Ring bleibt das reine Zeichenmenü für den Stift. |
| Ausblenden der Tinte | Eigener Schalter, Standard **an**, damit eine Karte, auf der beides steht, im Textfeldmodus lesbar bleibt. |

Damit ist der Plan entschieden und kann so gebaut werden.
