import Foundation
import UIKit

/// The folder the app and the widget share, and the one document in it.
///
/// A widget runs in its own process and reaches none of the app's own folders, so both sides meet in
/// an App Group container: the app writes the snapshot and the previews there, the widget reads
/// them. The group has to be registered in the Apple Developer Portal and claimed by both targets;
/// without it the container does not exist and nothing is found at all.
enum WidgetStore {
    /// Identifier of the App Group, as registered in the Apple Developer Portal. Mirrors
    /// `WidgetSharedStorage.GroupId` in the app - the container is not handed out if the two differ.
    static let appGroupIdentifier = "group.com.agredoapplication.panban"

    /// Root of the shared folder, or nil while the App Group is not set up on this device.
    static var containerURL: URL? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroupIdentifier)
    }

    /// The document the app writes, or nil when there is none yet or it cannot be read.
    static func loadSnapshot() -> WidgetSnapshot? {
        guard let url = containerURL?.appendingPathComponent(WidgetSnapshot.fileName),
              let data = try? Data(contentsOf: url),
              let snapshot = try? JSONDecoder().decode(WidgetSnapshot.self, from: data),
              snapshot.schemaVersion == WidgetSnapshot.currentSchemaVersion
        else {
            return nil
        }

        return snapshot
    }

    /// One of the previews the document names, or nil when the file is not there.
    ///
    /// The images are cached: a widget is asked for its content more than once per refresh - for the
    /// gallery, for the home screen, for each appearance - and reading a few hundred kilobytes from
    /// disk every time is work the process cannot afford.
    static func loadImage(named name: String) -> UIImage? {
        if let cached = images.object(forKey: name as NSString) {
            return cached
        }

        guard let url = containerURL?.appendingPathComponent(name),
              let image = UIImage(contentsOfFile: url.path)
        else {
            return nil
        }

        images.setObject(image, forKey: name as NSString)
        return image
    }

    private static let images = NSCache<NSString, UIImage>()
}
