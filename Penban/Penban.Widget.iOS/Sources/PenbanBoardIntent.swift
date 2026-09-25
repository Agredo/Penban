import AppIntents
import WidgetKit

/// One board the widget can be pointed at, as the configuration sheet lists it.
struct PenbanBoardEntity: AppEntity {
    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Board"

    static var defaultQuery = PenbanBoardQuery()

    /// The board's identity, as the app writes it - a UUID in text form.
    let id: String

    let title: String

    var displayRepresentation: DisplayRepresentation {
        DisplayRepresentation(title: "\(title)")
    }
}

/// Offers the boards the app has written, for the widget's configuration sheet.
struct PenbanBoardQuery: EntityQuery {
    func entities(for identifiers: [String]) async throws -> [PenbanBoardEntity] {
        boards().filter { identifiers.contains($0.id) }
    }

    func suggestedEntities() async throws -> [PenbanBoardEntity] {
        boards()
    }

    /// What the widget shows before a board is chosen: the first of the overview.
    func defaultResult() async -> PenbanBoardEntity? {
        boards().first
    }

    private func boards() -> [PenbanBoardEntity] {
        (WidgetStore.loadSnapshot()?.boards ?? []).map { PenbanBoardEntity(id: $0.id, title: $0.title) }
    }
}

/// The one choice the widget asks for when it is placed: which board it shows.
struct PenbanBoardIntent: WidgetConfigurationIntent {
    static var title: LocalizedStringResource = "Board"

    static var description = IntentDescription("The board the widget shows.")

    /// Deliberately optional: a widget that is placed before the app has ever run - and one whose
    /// board was deleted since - falls back to the first board of the overview instead of staying
    /// empty.
    @Parameter(title: "Board")
    var board: PenbanBoardEntity?
}
