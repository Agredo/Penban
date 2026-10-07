# Projekte

Projekte sammeln Boards unter sich. Gebaut in den Schritten 0–6 unter „Reihenfolge", aufsetzend auf
0.5.7 (Build 21), `main` = `806c091`. Die Entscheidungen stehen unten unter „Entschieden", das
Gebaute und die Abweichungen vom Plan unter „Gebaut".

## Was gebaut werden soll

1. Ein **Projekt** sammelt Boards unter sich und bekommt eine **eigene Seite**.
2. Ob die App auf dem **Dashboard**, den **Projekten** oder den **Boards** startet, sagt eine
   Einstellung. Voreinstellung ist das Dashboard.
3. Beim Anlegen eines Boards lässt sich ein **erweiterter Modus** aufklappen (Expander): Tags,
   Start- und Enddatum und die Projektzugehörigkeit.
4. Ein **Dashboard** zeigt die letzten drei Projekte in einer waagerechten Reihe, darunter die
   zuletzt bearbeiteten Boards als senkrechte Liste, jeder Block mit einem Weg auf „alle".
5. Eine **Shell mit Flyout** macht alles von überall erreichbar.

## Modell

`Penban.Models/Project.cs` ist neu und erbt von `SyncableEntity`:

```csharp
public class Project : SyncableEntity
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Reihenfolge in der Liste, gleiche Bedeutung wie bei Board: 0 = noch nie verschoben.</summary>
    public int SortOrder { get; set; }

    public List<string> Tags { get; set; } = new();
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>Eigene Notiz des Projekts, wie die eines Boards. Leer = es gibt keine.</summary>
    public List<InkStroke> NoteStrokes { get; set; } = new();

    /// <summary>Vom Nutzer gewählte Papierfarbe der Notiz. null = aus der Id abgeleitet.</summary>
    public int? NoteColorIndex { get; set; }
}
```

`Penban.Models/Board.cs` bekommt nur Felder dazu:

```csharp
/// <summary>Projekt, zu dem das Board gehört. null = ohne Projekt.</summary>
public Guid? ProjectId { get; set; }

public List<string> Tags { get; set; } = new();
public DateTimeOffset? StartDate { get; set; }
public DateTimeOffset? EndDate { get; set; }
```

**Nichts muss migriert werden.** LiteDB ist schemalos, jedes vor 0.6 geschriebene Board hat kein
`ProjectId`-Feld und liest sich als `null` — genau der Zustand, den „ohne Projekt" meint. `SortOrder`
bedeutet schon bei Boards „0 = noch nie verschoben, dann nach Datum", die Projekte erben das ohne
neue Regel. `ProjectId` hängt am **Board**, nicht als Board-Liste am Projekt: eine Quelle der
Wahrheit, keine Waisen, und ein gelöschtes Projekt lässt seine Boards einfach stehen.

## Entschieden

| # | Frage | Entscheidung |
|---|---|---|
| 1 | Menü | **Shell Flyout** |
| 2 | Navigation | `AgredoApplication.MVVM.Services` 1.1.0 adoptieren (Navigation, Routen, Popups) |
| 3 | Startseite | **Dashboard / Projekte / Boards**, Voreinstellung **Dashboard** |
| 4 | Projekt löschen | Boards bleiben, `ProjectId` wird `null` |
| 5 | Tags | **Freitext**, mehrere, filterbar, **neutrale einfarbige Chips** ohne Emoji |
| 6 | Dashboard-Umfang | 3 Projekte / 3 Boards; „alle" führt auf die Projekte- bzw. Boards-Seite |
| 7 | Filter-Chips | sofort, vorgezogen auf Schritt 3 |
| 8 | Export/Import | Backup enthält Projekte; der Einzel-Board-Export bleibt flach (`ProjectId = null`) |
| 9 | Dashboard-Layout | **Projekt-Reihe waagerecht**, **Board-Liste senkrecht** |
| 10 | Projekt-Notiz | wie bei Boards, freiwillig; ohne Notiz ein **Monogramm** |
| 11 | Neues Board im Projekt | der „+"-Knopf belegt `ProjectId` **vor**, im Expander änderbar |
| 12 | „Ohne Projekt" | **kein** eigener Eintrag — dafür ist die Boards-Seite im Flyout da |
| 13 | Kopf der Bereichsseiten | das Penban-Zeichen weicht einem **Menü-Knopf**, das Zeichen steht im **Flyout-Kopf** |
| 14 | Zwei Menü-Knöpfe | **beide bleiben** — der der Shell (WinUI-Titelleiste) und der eigene im Kopf |
| 15 | Menüeinträge | neben den drei Bereichen **Einstellungen, Hilfe, Feedback, Datenschutz, Über Penban** |
| 16 | Board-Karte | **ein** Steuerelement (`BoardCard`) für alle Listen, mit Fächer, in beiden Größen |

### Zu 9 — warum nur die Projekt-Reihe waagerecht scrollt

Die beiden Blöcke sind sich ähnlich, aber die Board-Karten tragen mehr: Notiz-Vorschau, Tags,
Projektname, Datum, Ladebalken. Die Boards-Seite hat dafür schon zwei Anordnungen, umgeschaltet bei
`BoardCard.WidePageWidth = 700`. In einer senkrechten Liste passen sie unverändert; waagerecht
bräuchte es eine **dritte**, sonst frisst auf dem Desktop eine einzige breite Karte die ganze Reihe.
Senkrecht sieht das Dashboard außerdem aus wie die Boards-Seite, statt zwei Scroll-Richtungen auf
einer Seite zu mischen.

### Zu 10 — die Projekt-Karte

Ein Projekt ohne Notiz bekommt keine leere Notiz, sondern ein **Monogramm**: Papierfarbe und Neigung
kommen wie bei Boards aus der Id (`NoteStyle.PaperIndexFor` / `NoteStyle.TiltFor`), darauf stehen die
Anfangsbuchstaben der ersten beiden Wörter in großer Schrift — „Urlaub Planung" wird zu „UP". Bei
einem einzigen Wort steht nur der eine Buchstabe. Sobald eine echte Notiz existiert, ersetzt sie das
Monogramm. Die Vorschau zeigt **nur** die eigene Projekt-Notiz, keinen Fächer aus den Boards darunter.

`BoardNotePreview` hat für „eine Notiz, die keine Karte ist" schon den passenden Konstruktor
`(Guid id, IReadOnlyList<InkStroke> strokes, int? colorIndex, int index, int count)` — das Projekt
erbt ihn, ein neuer Typ ist nicht nötig. Dasselbe Muster wie bei einem Board ohne Notiz, siehe
`BoardViewModel` (dort wird eine leere Notiz in der abgeleiteten Farbe angehängt).

### Zu 13 und 14 — die zwei Menü-Knöpfe

`Shell.FlyoutBehavior` ist `Flyout`, also blendet die Shell unter WinUI ihren eigenen Knopf in die
Titelleiste (`MauiNavigationView.UpdatePaneDisplayModeFromFlyoutBehavior` setzt
`IsPaneToggleButtonVisible = true`). Dazu kommt der eigene Knopf im Kopf der drei Bereichsseiten,
weil deren `Shell.NavBarIsVisible="False"` ist: die Shell zeichnet dort keine Leiste, und ohne den
Knopf käme man auf einem Tablet nicht ans Menü. Beide öffnen dasselbe Flyout und bleiben deshalb —
der eine sitzt in der Titelleiste, der andere im Kopf der Seite.

## Dateien je Schicht

| Schicht | Neu | Geändert |
|---|---|---|
| `Penban.Models` | `Project.cs` | `Board.cs` |
| `Penban.Data` | `LiteDbProjectRepository.cs` (Collection `"projects"`) | — |
| `Penban.Services.Abstractions` | `AppRoutes.cs`, `IProjectRepository.cs`, `IProjectService.cs`, `ProjectDetails.cs`, `BoardDetails.cs`, `QueryParameters.cs` | `IBoardService.cs`, `PreferenceKeys.cs`, `PenbanFile.cs`, `TransferModels.cs` |
| `Penban.Services` | `ProjectService.cs` | `BoardService.cs`, `DataTransferService.cs` |
| `Penban.ViewModels` | `DashboardViewModel`, `ProjectsViewModel`, `ProjectViewModel`, `ProjectPageViewModel`, `ProjectNoteViewModel`, `ProjectDetailsViewModel`, `BoardDetailsViewModel`, `ProjectOption`, `ProjectStats`, `RowList`, `TagFilterViewModel` | `BoardsViewModel`, `SettingsViewModel`, `BoardNotePreview` |
| `Penban.Maui.Views` | `DashboardPage`, `ProjectsPage`, `ProjectPage`, `ProjectDetailsPage`, `BoardDetailsPage`, `Controls/BoardCard`, `Services/BoardCardActions`, `Services/BoardPageFactory`, `Services/ShellMenu` | `BoardsPage`, `SettingsPage`, `HelpPage`, `AboutPage`, `BoardPage`, `CardInkEditorPage`, `FeedbackPage`, `PrivacyPage`, `IconFont`, `TranslateExtension`, `Theme.xaml`, `TransferCoordinator` |
| `Penban.Maui` | `ServiceRouteFactory.cs` | `AppShell.xaml(.cs)`, `App.xaml.cs`, `MauiProgram.cs`, `Penban.Maui.csproj` |
| `Penban.Util` | `Monogram.cs`, `TagList.cs` | `Strings.resx` + `Strings.de.resx` |

`LiteDbProjectRepository` folgt `LiteDbBoardRepository` (`DatabaseWork.RunAsync`,
`context.Collection<Project>("projects")`).

## Navigation

Die Frage, wie Routen registriert werden und wie das in MVVM läuft, ist der heikelste Teil, deshalb
ausführlich:

* **Oberste Bereiche** (Dashboard, Projekte, Boards) sind `FlyoutItem` + `ShellContent`, die
  `AppShell.xaml.cs` programmatisch aus der DI erzeugt — zwei Tabellen (`Areas`, `MenuPages`) und
  zwei Schleifen. Grund: `{DataTemplate pages:X}` benutzt `Activator.CreateInstance` und kann keine
  Seiten mit Konstruktor-Injektion bauen. Der Wechsel zwischen den Bereichen ist ein Wechsel von
  `Shell.CurrentItem`, **kein** `GoToAsync`. Die Route steht nur am `ShellContent`, der `FlyoutItem`
  trägt bloß Titel und Symbol — sonst findet `//projects` nichts.
* **Detailseiten** (Projekt, Board-Details) kommen über
  `Routing.RegisterRoute(name, new ServiceRouteFactory<TPage>(services))` dazu; die Factory löst die
  Seite aus der DI auf. `RouteFactory` hat in MAUI 10 **zwei** abstrakte Mitglieder
  (`GetOrCreate()` und `GetOrCreate(IServiceProvider)`); beide sind überschrieben.
* **Routennamen** stehen in `AppRoutes` (`Penban.Services.Abstractions`, nur `const string`, damit
  kein Seitentyp in die Abstraktionen gerät). Die **Startseite** liest `AppShell` einmal beim Bauen
  aus `PreferenceKeys.StartPage` und setzt `CurrentItem` entsprechend (`StartRoute`); eine Änderung
  in den Einstellungen greift also beim nächsten Kaltstart. `IShellRoutingService` des Pakets wird
  **nicht** benutzt: sein `GetRoute` schlägt über den Typnamen nach und kennt die Einstellung nicht.
* **Parameter** übergibt das ViewModel als `navigation.ShellNavigationTo(route, new Dictionary<string, object> { ["BoardId"] = id })`
  und nimmt sie über das **framework-freie** `IQueryAttributable` des Pakets entgegen. Damit steht
  kein Seitentyp und kein `Shell.Current` in `Penban.ViewModels`.
* **Menüeinträge** (Einstellungen, Hilfe, Feedback, Datenschutz, Über Penban) sind `MenuItem`s in
  `Shell.Items`; die implizite Umwandlung in `MenuShellItem` (`ShellItem.cs`) macht daraus
  Flyout-Einträge, die wie die Bereiche in **einer** Gruppe untereinander landen. Ein `MenuItem`
  navigiert nicht und schließt das Flyout nicht — beides macht der Handler (`OnMenuEntryClicked`).

### Der Windows-Fehler, der hierher gehört

In `BoardsPage.xaml.cs` steht seit 0.1 ein Kommentar, dass `GoToAsync` unter Windows „Pending
Navigations still processing" wirft. Die Ursache steckt in Commit `9763042`: `AppShell` registrierte
eine Route auf eine `BoardHostPage`, die das Board lud und dann in `OnAppearing` **selbst**
`Navigation.PushAsync(new BoardPage(...))` und `RemovePage(this)` aufrief. Das war keine kaputte
Shell-Navigation, sondern eine **Mischung aus zwei Navigationsmodellen** — die Shell blieb mit
gesetzter „pending navigation" zurück und der Abschnittsstapel passte nicht mehr zusammen.

Die `BoardHostPage` gab es nur, weil `BoardViewModel` ein schon geladenes `Board` brauchte, was die
Shell bei der Konstruktion nicht liefern kann. Genau das löst das framework-freie
`IQueryAttributable` des Pakets — der Auslöser verschwindet. Der **eigene Kopf bleibt trotzdem**,
denn die Shell zeigt unter Windows keinen Zurück-Pfeil (`Shell.NavBarIsVisible="False"`); es ändert
sich nur der Zurück-Knopf von `Navigation.PopAsync()` auf `navigation.NavigateBack()`.

## Reihenfolge

Dashboard vor Flyout getauscht: das Flyout braucht `DashboardPage`, sonst zeigt der Startseiten-Eintrag
ins Leere.

| Schritt | Inhalt | Prüfung | Stand |
|---|---|---|---|
| 0 | Paket einbinden + **Windows-Spike**: eine Route, ein Push, ein Zurück | von Hand unter Windows | gebaut (`0f5f066`) |
| 1 | Modell + Repository + Services + Strings, ohne UI | Build | gebaut (`f2e78c2`) |
| 2 | `BoardDetailsPage` mit Expander (Anlegen und Bearbeiten) | Windows | gebaut (`e0414bc`) |
| 3 | `ProjectsPage` + `ProjectPage` + **Filter-Chips** | Windows | gebaut (`f2c2a68`) |
| 4 | Dashboard + **geteilte Board-Karte** | Windows | gebaut (`dbda268`) |
| 5 | `AppShell` mit Flyout, Menüeinträgen und Startseiten-Einstellung | Windows | gebaut (`dbda268`) |
| 6 | Export/Import der Projekte, erweiterte Hilfe, Transfer auf der Projekte-Seite | Rundlauf | gebaut (`ecefad2`) |
| 7 | `TODO.md` und diese Datei nachziehen | — | gebaut |

## Risiken

1. **`GoToAsync` unter Windows.** **Erledigt:** der Spike in Schritt 0 lief sauber durch. Die
   Ursache war nicht `GoToAsync`, sondern die Mischung aus `GoToAsync` und `PushAsync`/`RemovePage`
   in `OnAppearing` (siehe oben). Die Paket-Implementierung bleibt, `AppRoutes` und die ViewModels
   sind davon unberührt.
2. **Ziel-Framework-Falle.** **Erledigt:** `Penban.Maui.csproj` zielt ausschließlich auf
   Plattform-TFMs, es gibt kein `net10.0`-Asset zu treffen und damit auch kein `Penban.Maui.Services`
   — die Paketreferenz braucht keine `Condition`.
3. **Namensgleichheiten.** Das Paket bringt ein eigenes `IPreferences` (`Get(key, default, scope)`)
   und `IDialogService` (ohne `DisplayPromptAsync`) mit, die von den Penban-Varianten abweichen.
   Angefasst wurde nur die Navigation; wo beide in einer Datei liegen, trennt ein
   `using IPreferences = Penban.Services.Abstractions.IPreferences;` sie. Die Speicher- und
   Dialog-Migration steht weiter aus.
4. **Popups unter WinUI.** `PromptDialogPage` gibt es, weil `DisplayPromptAsync` mit einem Eingabefeld
   dort abstürzt (0xc000027b in `Microsoft.UI.Xaml.dll`). Das Paket bringt `IPopupNavigationService`;
   ob das den Ersatz überflüssig macht, muss eigens geprüft werden. **Noch offen.**
5. **Kein Testprojekt.** Geprüft wird von Hand. Ein `Penban.Tests` (xUnit) für die Services wäre
   sinnvoll; Finanzio hat eines. **Noch offen.**
6. **Board öffnen bleibt auf dem alten Weg.** `BoardPage` kommt weiter über `PushAsync` aus
   `BoardPageFactory` — die Routen-Umstellung endet bei den Formularseiten. Ein `GoToAsync` auf
   dieselbe Seite von einem Knopf aus ist der nächste Schritt, aber er muss die Board-Id mitgeben,
   wofür `BoardPage` noch kein `IQueryAttributable` ist.

## Gebaut — und was anders kam

Alles aus „Was gebaut werden soll" steht. Die Abweichungen vom Plan:

* **Schritt 2 ist eine eigene Seite, kein Dialog.** `BoardDetailsPage` liegt als Route
  (`AppRoutes.BoardDetails`) auf demselben Stapel wie die Projektseiten; `RenameBoardAsync` ist aus
  `IBoardService` verschwunden, weil das Formular das Umbenennen mit erledigt.
* **Eine Board-Karte statt zwei.** `Controls/BoardCard` ist ein `ContentView` mit Notizfächer,
  Ladestreifen und den beiden Größen (`Narrow`/`Wide`, Umbruch bei `WidePageWidth = 700`). Die
  Boards-Seite hängt die drei Aktionsknöpfe an (`ShowActions="True"` plus die Kommandos aus
  `Services/BoardCardActions`), das Dashboard lässt sie weg (`ShowActions="False"`) — Fächer und
  beide Größen hat es trotzdem. Die `Border` und die Gesten bleiben in der Vorlage der jeweiligen
  Seite, weil die Handler dort liegen.
* **Kein Umsortieren auf dem Dashboard.** Die Board-Karten dort tragen nur das Tippen, kein Ziehen.
* **`ProjectPage` hat noch ihre eigene Zeile** (`ProjectBoardRow`: kein Fächer, Stift und Löschen,
  eine Größe). Sie könnte als Nächstes auf `BoardCard` wandern.
* **`ProjectsPage` hat keinen Suchknopf**, anders als die Boards-Seite.
* **Die Projekte-Seite bietet das Datenbank-Backup mit an.** Ein Backup schließt die Projekte ein,
  also gehört der Knopf auf die Seite, die zu ihnen führt — und nicht nur hinter sie.
* **Die Startseiten-Einstellung greift beim nächsten Kaltstart.** `AppShell` liest sie einmal beim
  Bauen; ein laufendes Fenster umzuhängen wäre ein eigener Mechanismus.
* **Der Zurück-Weg bleibt `NavigateBack()`** auf den Formularseiten, der eigene Kopf bleibt überall:
  die Shell zeichnet unter Windows keinen Zurück-Pfeil.
* **Die Hilfe ist gewachsen** (je ein Abschnitt zu Projekten, Tags und Filtern, der Startseite und
  dem Menü, und der Import-/Export-Abschnitt nennt die Projekte) und `TODO.md` ist nachgezogen.

## Offen

* `BoardPage` öffnen weiterhin über `PushAsync` statt über eine Route (Risiko 6).
* `ProjectPage` auf `BoardCard` umstellen.
* `Penban.Tests` (Risiko 5), Popups aus dem Paket (Risiko 4).
