#!/usr/bin/env bash
#
# Penban – Widget-Extension (PenbanWidget.appex) bauen.
#
#   scripts/ios-widget.sh build [Debug|Release]   Extension bauen und ablegen (Standard: Release)
#   scripts/ios-widget.sh clean                   Build-Ordner der Extension löschen
#
# Voraussetzung: macOS mit Xcode. Nur Xcode kann eine .appex erzeugen – die MAUI-Toolchain
# bettet sie anschließend nur ein. Das Ergebnis landet unter
#
#   Penban/Penban.Widget.iOS/build/<Configuration>-iphoneos/PenbanWidget.appex
#
# also genau dort, wo Penban.Maui.csproj die Extension über AdditionalAppExtensions sucht.
# Danach baut scripts/ios-testflight.sh build die App samt Widget.
#
# Das Xcode-Projekt wird aus project.yml erzeugt (XcodeGen). Ist PenbanWidget.xcodeproj schon
# vorhanden, wird nichts erzeugt und direkt gebaut; wer XcodeGen nicht installieren will, legt
# das Ziel einmalig von Hand an – die Anleitung steht in Penban/Penban.Widget.iOS/README.md.
#
#   brew install xcodegen
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WIDGET_DIR="$REPO_ROOT/Penban/Penban.Widget.iOS"
PROJECT="$WIDGET_DIR/PenbanWidget.xcodeproj"
SCHEME="PenbanWidget"

# Muss zu Penban.Maui.csproj passen: AdditionalAppExtensions sucht genau dieses Bundle, und
# ein Extension-Bundle muss die Bundle-ID der App als Präfix tragen.
EXTENSION_BUNDLE_ID="com.agredoapplication.panban.WidgetExtension"
APP_GROUP="group.com.agredoapplication.panban"

log()  { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[33mHinweis:\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[31mFehler:\033[0m %s\n' "$*" >&2; exit 1; }

# Der Build läuft nur auf macOS; hier oben abfangen, damit der Fehler klar benannt ist.
require_macos() {
  [ "$(uname -s)" = "Darwin" ] || die 'Die Widget-Extension lässt sich nur auf macOS bauen (Xcode).
    Die Swift-Quellen und die Projektdefinition liegen bereit; ausgeführt werden muss dieses
    Skript auf dem Mac.'
}

cmd_generate() {
  [ -d "$PROJECT" ] && return 0
  command -v xcodegen >/dev/null 2>&1 || die "XcodeGen fehlt. Installieren mit 'brew install xcodegen',
    oder das Xcode-Ziel einmalig von Hand anlegen (siehe Penban/Penban.Widget.iOS/README.md)."
  log "Erzeuge PenbanWidget.xcodeproj aus project.yml"
  (cd "$WIDGET_DIR" && xcodegen generate)
}

# Prüft, ob die gebaute Extension überhaupt das ist, was die App erwartet. Ein falsches
# Bundle-ID-Präfix oder eine fehlende App Group fällt sonst erst auf dem Gerät auf: die
# Extension erscheint dann einfach nicht in der Widget-Galerie.
verify_appex() { # <pfad/zur/PenbanWidget.appex>
  local appex="$1" bundle_id entitlements
  plutil -lint "$appex/Info.plist" >/dev/null || die "$appex/Info.plist ist keine gültige plist."

  bundle_id="$(plutil -extract CFBundleIdentifier raw -o - "$appex/Info.plist" 2>/dev/null || true)"
  [ "$bundle_id" = "$EXTENSION_BUNDLE_ID" ] \
    || die "Die Extension hat die Bundle-ID '$bundle_id', erwartet wird '$EXTENSION_BUNDLE_ID'.
    Bundle-ID und Signierung in Xcode anpassen (PenbanWidget-Ziel → Signing & Capabilities)."

  # codesign schreibt die Entitlements je nach macOS-Version nach stdout oder stderr –
  # deshalb beide Ströme einsammeln.
  entitlements="$(codesign -d --entitlements :- "$appex" 2>&1 || true)"
  if ! printf '%s' "$entitlements" | grep -q "$APP_GROUP"; then
    die "Der Extension fehlt die App Group '$APP_GROUP' – ohne sie erreicht sie den geteilten
    Ordner der App nicht und zeigt nichts an. Capability 'App Groups' im PenbanWidget-Ziel
    aktivieren (Xcode → Signing & Capabilities) und die Gruppe im Developer Portal anlegen."
  fi

  echo "Bundle-ID:  $bundle_id"
  echo "App Group:  $APP_GROUP"
}

cmd_build() {
  local configuration="${1:-Release}"
  local out_dir="$WIDGET_DIR/build/$configuration-iphoneos"
  local appex="$out_dir/PenbanWidget.appex"

  require_macos
  command -v xcodebuild >/dev/null 2>&1 || die "xcodebuild nicht gefunden. Bitte Xcode installieren
    und einmalig 'xcode-select --install' bzw. Xcode öffnen, damit die Lizenz akzeptiert wird."

  cmd_generate

  log "Baue $SCHEME ($configuration)"
  # CONFIGURATION_BUILD_DIR statt -derivedDataPath: so landet das .appex direkt in dem Ordner,
  # den der MAUI-Build erwartet, ohne Zwischenkopie.
  (cd "$WIDGET_DIR" && xcodebuild \
    -project "$PROJECT" \
    -scheme "$SCHEME" \
    -configuration "$configuration" \
    -destination 'generic/platform=iOS' \
    -allowProvisioningUpdates \
    CONFIGURATION_BUILD_DIR="$out_dir" \
    build)

  [ -d "$appex" ] || die "Build lief durch, aber $appex wurde nicht erzeugt."

  log "Prüfe die Extension"
  verify_appex "$appex"

  # Beim Einbetten in die App löscht der MAUI-Build die Signatur von Xcode und signiert die
  # Extension neu – mit genau dieser Datei (CodesignEntitlements in Penban.Maui.csproj). Fehlt sie
  # oder nennt sie die Gruppe nicht, scheitert erst der App-Build oder später das Widget.
  local plist="$WIDGET_DIR/Resources/PenbanWidget.entitlements"
  [ -f "$plist" ] || die "Die Entitlements-Datei $plist fehlt. Penban.Maui.csproj signiert die
    eingebettete Extension damit (AdditionalAppExtensions → CodesignEntitlements)."
  plutil -lint "$plist" >/dev/null || die "$plist ist keine gültige plist."
  grep -q "$APP_GROUP" "$plist" || die "$plist nennt die App Group '$APP_GROUP' nicht – die
    eingebettete Extension hätte dann keinen Zugriff auf den geteilten Ordner."
  echo "Entitlements: $plist"

  log "Fertig: $appex"
  printf 'Nächster Schritt: scripts/ios-testflight.sh build\n'
}

cmd_clean() {
  rm -rf "$WIDGET_DIR/build"
  echo "Build-Ordner der Widget-Extension entfernt: $WIDGET_DIR/build"
}

case "${1:-build}" in
  build)           shift || true; cmd_build "${1:-Release}";;
  clean)           cmd_clean;;
  -h|--help|help)  sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//';;
  *)               die "Unbekanntes Kommando '$1' (build [Debug|Release] | clean)";;
esac
