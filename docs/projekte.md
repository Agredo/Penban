# Projekte

Plan für Projekte, die Boards unter sich sammeln. Stand: nach 0.5.7 (Build 21), `main` = `806c091`.
Der Plan ist noch nicht gebaut. Die Entscheidungen stehen unten unter „Entschieden"; offen ist nichts
mehr außer der Frage, wann gebaut wird.

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

### Zu 9 — warum nur die Projekt-Reihe waagerecht scrollt

Die beiden Blöcke sind sich ähnlich, aber die Board-Karten tragen mehr: Notiz-Vorschau, Tags,
Projektname, Datum, Ladebalken. Die Boards-Seite hat dafür schon zwei Vorlagen (`WideBoardCard`,
`NarrowBoardCard`), umgeschaltet bei `WideCardPageWidth = 700`. In einer senkrechten Liste passen
sie unverändert; waagerecht bräuchte es eine **dritte** Vorlage, sonst frisst auf dem Desktop eine
einzige Wide-Karte die ganze Reihe. Senkrecht sieht das Dashboard außerdem aus wie die Boards-Seite,
statt zwei Scroll-Richtungen auf einer Seite zu mischen.

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

## Dateien je Schicht

| Schicht | Neu | Geändert |
|---|---|---|
| `Penban.Models` | `Project.cs` | `Board.cs` |
| `Penban.Data` | `LiteDbProjectRepository.cs` (Collection `"projects"`) | — |
| `Penban.Services.Abstractions` | `IProjectRepository.cs`, `IProjectService.cs`, `AppRoutes.cs` | `IBoardService.cs`, `PreferenceKeys.cs`, `PenbanFile.cs`, `TransferModels.cs` |
| `Penban.Services` | `ProjectService.cs` | `BoardService.cs` |
| `Penban.ViewModels` | `ProjectsViewModel`, `ProjectViewModel`, `DashboardViewModel`, `BoardDetailsViewModel` | `BoardsViewModel`, `SettingsViewModel` |
| `Penban.Maui.Views` | `ProjectsPage`, `ProjectPage`, `DashboardPage`, `BoardDetailsPage` | `BoardsPage`, `SettingsPage` |
| `Penban.Maui` | `AppRoutes.cs`, `ServiceRouteFactory.cs`, 4 Navigations-SVGs | `AppShell.xaml(.cs)`, `MauiProgram.cs` |
| `Penban.Util` | — | `Strings.resx` + `Strings.de.resx` |

`LiteDbProjectRepository` folgt `LiteDbBoardRepository` (`DatabaseWork.RunAsync`,
`context.Collection<Project>("projects")`).

## Navigation

Die Frage, wie Routen registriert werden und wie das in MVVM läuft, ist der heikelste Teil, deshalb
ausführlich:

* **Oberste Bereiche** (Dashboard, Projekte, Boards, Einstellungen) sind `ShellContent`-Einträge, die
  `AppShell.xaml.cs` programmatisch aus der DI erzeugt. Grund: `{DataTemplate pages:X}` benutzt
  `Activator.CreateInstance` und kann keine Seiten mit Konstruktor-Injektion bauen. Der Flyout-Wechsel
  ist ein Wechsel von `Shell.CurrentItem`, **kein** `GoToAsync`.
* **Detailseiten** (Projekt, Board-Details) kommen über
  `Routing.RegisterRoute(name, new ServiceRouteFactory<TPage>(services))` dazu; die Factory löst die
  Seite aus der DI auf.
* **Routennamen und Startseite** kommen aus dem Paket: `AppRoutes : IRoutes` hält `const string`-Namen
  für die Detailseiten und die Bereiche, registriert wird mit
  `routing.AddRoute(nameof(AppRoutes), "//projects")`. Die Startseite ergibt sich aus
  `IShellRoutingService.GetRoute(routes)` → `"//dashboard" | "//projects" | "//boards"`.
* **Parameter** übergibt das ViewModel als `navigation.ShellNavigationTo(route, new Dictionary<string, object> { ["Project"] = project })`
  und nimmt sie über das **framework-freie** `IQueryAttributable` des Pakets entgegen. Damit steht
  kein Seitentyp und kein `Shell.Current` in `Penban.ViewModels`.

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

| Schritt | Inhalt | Prüfung |
|---|---|---|
| 0 | Paket einbinden + **Windows-Spike**: eine Route, ein Push, ein Zurück, Startseiten-Wechsel | von Hand unter Windows |
| 1 | Modell + Repository + Services + Strings, ohne UI | Build |
| 2 | `BoardDetailsPage` mit Expander (Anlegen und Bearbeiten) | Windows |
| 3 | `ProjectsPage` + `ProjectPage` + **Filter-Chips** | Windows |
| 4 | `AppShell` mit Flyout + Startseiten-Einstellung | Windows |
| 5 | Dashboard | Windows |
| 6 | Export/Import | Rundlauf |
| 7 | `TODO.md` und diese Datei nachziehen | — |

## Risiken

1. **`GoToAsync` unter Windows.** Der Spike in Schritt 0 entscheidet. Fällt er durch, wird
   `PenbanNavigationService : INavigationService` auf `PushAsync` gebaut — Interface, `AppRoutes`,
   ViewModels und Seiten bleiben unangetastet. Deshalb „Paket-Interface ja, Paket-Implementierung mit
   Vorbehalt".
2. **Ziel-Framework-Falle.** `AgredoApplication.MVVM.Services.Maui` hat kein `net10.0`-Asset,
   `Penban.Maui.Services.csproj` zielt aber auch darauf. Die Paketreferenz braucht deshalb
   `Condition="'$(TargetFramework)' != '$(PenbanNetVersion)'"`.
3. **Namensgleichheiten.** Das Paket bringt ein eigenes `IPreferences` (`Get(key, default, scope)`)
   und `IDialogService` (ohne `DisplayPromptAsync`) mit, die von den Penban-Varianten abweichen. In
   Schritt 0 wird nur die Navigation angefasst, die Speicher- und Dialog-Migration kommt getrennt.
4. **Popups unter WinUI.** `PromptDialogPage` gibt es, weil `DisplayPromptAsync` mit einem Eingabefeld
   dort abstürzt (0xc000027b in `Microsoft.UI.Xaml.dll`). Das Paket bringt `IPopupNavigationService`;
   ob das den Ersatz überflüssig macht, muss eigens geprüft werden.
5. **Kein Testprojekt.** Geprüft wird von Hand. Ein `Penban.Tests` (xUnit) für die Services wäre
   sinnvoll; Finanzio hat eines.

## Offen

Nichts Inhaltliches. Die Frage ist nur, wann gebaut wird.
