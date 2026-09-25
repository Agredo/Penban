import Foundation

/// Everything the home screen widget shows, in the shape the app writes it.
///
/// The property names are part of the contract and mirror `WidgetSnapshot.cs` in
/// `Penban.Maui.Views/Widget` one to one; renaming one here without renaming it there quietly
/// empties the widget, so both files move together.
///
/// The ink itself is not in the document: the app renders the card of the overview - without its
/// share, rename and delete buttons - into one PNG per widget size and appearance, and the widget
/// shows that image. That keeps a second renderer of the app's card, with its fonts and its ink,
/// out of this extension.
struct WidgetSnapshot: Decodable, Sendable {
    /// Layout this reader understands. A document from another version is ignored rather than
    /// half-read.
    static let currentSchemaVersion = 1

    /// Name of the document in the shared folder.
    static let fileName = "widget.json"

    let schemaVersion: Int

    /// When the app last wrote this; kept as text because only the app reads it.
    let updatedAtUtc: String

    /// Fingerprint of the board data this was captured from.
    let signature: String

    /// All boards, in overview order. The widget picks one of them.
    let boards: [WidgetBoard]
}

/// One board as the widget shows it: the card of the overview, minus the buttons.
struct WidgetBoard: Decodable, Identifiable, Sendable {
    /// Identity of the board; the widget remembers the chosen one by this.
    let id: String

    let title: String

    /// Caption under the title, exactly as the overview words it.
    let summary: String

    let cardCount: Int

    /// Whether the board carries a note of its own; that note leads the stack.
    let hasNote: Bool

    let noteColorIndex: Int

    let lastEditedUtc: String

    /// One entry per lane, drawn as the load bar of the overview.
    let columns: [WidgetColumn]

    /// Notes of the thumbnail, back to front, each with its place in the fan. Only their placement
    /// is in the document - the ink of the notes is in the rendered image.
    let notes: [WidgetNote]

    /// Rendered previews of this board, one per widget size and appearance.
    let images: WidgetImages
}

/// One lane of the load bar: how full it is, relative to the other lanes.
struct WidgetColumn: Decodable, Sendable {
    let title: String
    let count: Int
    let colorIndex: Int

    /// Relative width of the lane's chip.
    let share: Int

    /// How strongly the chip is painted; an empty lane is faded.
    let opacity: Double
}

/// One note of the thumbnail, with its place in the fan.
struct WidgetNote: Decodable, Sendable {
    let colorIndex: Int

    /// Rotation in degrees.
    let tilt: Double

    let offsetX: Double
    let offsetY: Double
}

/// The rendered previews of one board, one file name per widget size and appearance.
///
/// A preview is named `w-{boardId}-{square|wide|tall}-{light|dark}.png`; the board id is what keeps
/// the boards of one shared folder apart.
struct WidgetImages: Decodable, Sendable {
    let squareLight: String
    let squareDark: String
    let wideLight: String
    let wideDark: String
    let tallLight: String
    let tallDark: String
}
