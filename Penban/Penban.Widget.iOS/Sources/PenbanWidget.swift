import SwiftUI
import UIKit
import WidgetKit

/// One board as the widget shows it, with the preview the app rendered for it.
///
/// The image is deliberately not resolved here: which preview belongs on screen depends on the
/// widget's own size and on the current appearance, and only the view knows both.
struct PenbanEntry: TimelineEntry, Sendable {
    let date: Date

    /// The chosen board, or the first of the overview; nil while the app has written nothing.
    let board: WidgetBoard?

    /// Whether the app has ever written a snapshot. A widget that has never seen the app says so
    /// instead of looking broken.
    let hasSnapshot: Bool
}

/// The preview for one widget size and appearance.
private extension WidgetImages {
    func fileName(for family: WidgetFamily, dark: Bool) -> String {
        switch family {
        case .systemSmall:
            return dark ? squareDark : squareLight
        case .systemLarge:
            return dark ? tallDark : tallLight
        default:
            // The wide widget, and anything a later iOS adds to the family that is not one of the
            // three the app draws for.
            return dark ? wideDark : wideLight
        }
    }
}

struct PenbanProvider: AppIntentTimelineProvider {
    func placeholder(in context: Context) -> PenbanEntry {
        entry(for: nil)
    }

    func snapshot(for configuration: PenbanBoardIntent, in context: Context) async -> PenbanEntry {
        entry(for: configuration)
    }

    func timeline(for configuration: PenbanBoardIntent, in context: Context) async -> Timeline<PenbanEntry> {
        // The app asks for a reload whenever the boards change, so this is only a safety net for a
        // widget the system has not been told about - and for the day a date on a card ages.
        Timeline(entries: [entry(for: configuration)], policy: .after(Date().addingTimeInterval(3600)))
    }

    /// Resolves the board the widget shows: the chosen one, or the first of the overview when none
    /// was chosen or the chosen one is gone.
    private func entry(for configuration: PenbanBoardIntent?) -> PenbanEntry {
        let snapshot = WidgetStore.loadSnapshot()

        let chosen = configuration?.board.flatMap { wanted in
            snapshot?.boards.first { $0.id == wanted.id }
        }

        return PenbanEntry(date: Date(), board: chosen ?? snapshot?.boards.first, hasSnapshot: snapshot != nil)
    }
}

struct PenbanWidgetView: View {
    @Environment(\.widgetFamily) private var family
    @Environment(\.colorScheme) private var colorScheme

    let entry: PenbanEntry

    var body: some View {
        card
            .containerBackground(for: .widget) {
                // The preview carries the whole card, background included, so the widget itself adds
                // nothing behind it. Only the empty state needs a surface of its own.
                entry.board == nil ? Color(uiColor: .systemBackground) : Color.clear
            }
    }

    @ViewBuilder
    private var card: some View {
        if let board = entry.board,
           let image = WidgetStore.loadImage(named: board.images.fileName(for: family, dark: colorScheme == .dark)) {
            Image(uiImage: image)
                .resizable()
                .aspectRatio(contentMode: .fill)
                .clipped()
        } else {
            emptyState
        }
    }

    private var emptyState: some View {
        VStack(spacing: 8) {
            Image(systemName: "rectangle.on.rectangle.angled")
                .font(.title2)
                .foregroundStyle(.secondary)

            Text(message)
                .font(.caption)
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)
        }
        .padding()
    }

    private var message: LocalizedStringKey {
        entry.hasSnapshot ? "No boards yet." : "Open Penban to fill this widget."
    }
}

/// The board card of the overview on the home screen, in the three sizes the app draws previews for.
struct PenbanWidget: Widget {
    /// Identity of the widget. The app embeds the built extension under exactly this name, and the
    /// name is also what the app's reload asks for.
    static let kind = "PenbanWidget"

    var body: some WidgetConfiguration {
        AppIntentConfiguration(kind: Self.kind, intent: PenbanBoardIntent.self, provider: PenbanProvider()) { entry in
            PenbanWidgetView(entry: entry)
        }
        .configurationDisplayName("Penban")
        .description("A board from Penban, without the buttons of the overview.")
        .supportedFamilies([.systemSmall, .systemMedium, .systemLarge])
        // The previews are drawn to fill the widget edge to edge - the card of the overview sits on
        // a background of its own - so the system's own margin around the content would only shrink
        // the picture and break its proportions.
        .contentMarginsDisabled()
    }
}

@main
struct PenbanWidgetBundle: WidgetBundle {
    var body: some Widget {
        PenbanWidget()
    }
}
