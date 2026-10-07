#!/usr/bin/env bash
#
# Penban – Widget-Extension (PenbanWidget.appex) bauen.
#
#   scripts/ios-widget.sh build [Debug|Release]   Extension bauen und ablegen (Standard: Release)
#   scripts/ios-widget.sh check <Appex>           Version einer fertigen Extension gegen die App prüfen
#   scripts/ios-widget.sh clean                   Build-Ordner der Extension löschen
#
# Die Version der Extension muss der der App entsprechen, sonst lehnt App Store Connect das Paket
# ab (90473). Sie wird deshalb aus Penban.Maui.csproj gelesen (ApplicationDisplayVersion /
# ApplicationVersion) und in den Build gereicht - die App ist die Quelle, project.yml führt nur
# noch einen Notnagel für einen Build von Hand in Xcode. VERSION=... und BUILD=... in der Umgebung
# stechen diese Werte, damit scripts/ios-testflight.sh genau die Version durchgeben kann, die es in
# die App schreibt.
#
# Voraussetzung: macOS mit Xcode. Nur Xcode kann eine .appex erzeugen – die MAUI-Toolchain
# bettet sie anschließend nur ein. Das Ergebnis landet unter
#
#   Penban/Penban.Widget.iOS/build/<Configuration>-iphoneos/PenbanWidget.appex
#
# also genau dort, wo Penban.Maui.csproj die Extension über AdditionalAppExtensions sucht.
# Danach baut scripts/ios-testflight.sh build die App samt Widget.
#
# Das Xcode-Projekt wird aus project.yml erzeugt (XcodeGen). Ist XcodeGen vorhanden, wird es bei
# jedem Lauf neu erzeugt – sonst bliebe eine Änderung an project.yml (etwa die Version) in einem
# alten Projekt liegen. Wer XcodeGen nicht installieren will, legt das Ziel einmalig von Hand an –
# die Anleitung steht in Penban/Penban.Widget.iOS/README.md.
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

# Version und Build-Nummer dürfen von der App nicht abweichen – App Store Connect weist ein Paket
# mit 90473 zurück. Gelesen wird deshalb die App: Penban.Maui.csproj nennt die Version an genau
# einer Stelle, und wer hier baut, bekommt automatisch dieselbe. VERSION/BUILD aus der Umgebung
# stechen sie – so reicht scripts/ios-testflight.sh die Werte durch, die es in die App schreibt.
PROJ="$REPO_ROOT/Penban/Penban.Maui/Penban.Maui.csproj"
# Der erste Treffer reicht: beide Eigenschaften stehen in Penban.Maui.csproj genau einmal.
csproj_value() { sed -n "s/.*<$1>\(.*\)<\/$1>.*/\1/p" "$PROJ" | head -1 | tr -d '[:space:]'; }

VERSION="${VERSION:-$(csproj_value ApplicationDisplayVersion)}"
BUILD="${BUILD:-$(csproj_value ApplicationVersion)}"

log()  { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[33mHinweis:\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[31mFehler:\033[0m %s\n' "$*" >&2; exit 1; }

[ -n "$VERSION" ] && [ -n "$BUILD" ] || die "Version und Build-Nummer stehen nicht in $PROJ
    (ApplicationDisplayVersion / ApplicationVersion). Ohne sie bekäme die Extension eine Version,
    die nicht zur App passt - App Store Connect lehnt das Paket dann mit 90473 ab."

# Der Build läuft nur auf macOS; hier oben abfangen, damit der Fehler klar benannt ist.
require_macos() {
  [ "$(uname -s)" = "Darwin" ] || die 'Die Widget-Extension lässt sich nur auf macOS bauen (Xcode).
    Die Swift-Quellen und die Projektdefinition liegen bereit; ausgeführt werden muss dieses
    Skript auf dem Mac.'
}

cmd_generate() {
  # project.yml ist die Quelle der Wahrheit. Ist XcodeGen vorhanden, wird das Projekt bei jedem
  # Lauf neu erzeugt: sonst bliebe eine Änderung an project.yml - etwa eine neue Swift-Datei - in
  # einem schon vorhandenen Projekt liegen und der Build führe still die alten Werte mit.
  if command -v xcodegen >/dev/null 2>&1; then
    log "Erzeuge PenbanWidget.xcodeproj aus project.yml"
    (cd "$WIDGET_DIR" && xcodegen generate)
    return 0
  fi

  [ -d "$PROJECT" ] && return 0
  die "XcodeGen fehlt. Installieren mit 'brew install xcodegen',
    oder das Xcode-Ziel einmalig von Hand anlegen (siehe Penban/Penban.Widget.iOS/README.md)."
}

# Prüft, ob die gebaute Extension überhaupt das ist, was die App erwartet. Ein falsches
# Bundle-ID-Präfix oder eine fehlende App Group fällt sonst erst auf dem Gerät auf: die
# Extension erscheint dann einfach nicht in der Widget-Galerie.
verify_appex() { # <pfad/zur/PenbanWidget.appex>
  local appex="$1" bundle_id entitlements executable
  plutil -lint "$appex/Info.plist" >/dev/null || die "$appex/Info.plist ist keine gültige plist."

  # Ohne CFBundleExecutable nimmt das Gerät das Bundle gar nicht erst an ("has missing or invalid
  # CFBundleExecutable in its Info.plist"). Der Build erzeugt die plist nicht selbst, also muss der
  # Name in Resources/Info.plist stehen - und der Build muss ihn auch eingesetzt haben.
  executable="$(plutil -extract CFBundleExecutable raw -o - "$appex/Info.plist" 2>/dev/null || true)"
  [ -n "$executable" ] \
    || die "$appex/Info.plist nennt kein CFBundleExecutable. Resources/Info.plist muss
    'CFBundleExecutable' = '\$(EXECUTABLE_NAME)' enthalten."
  [ -x "$appex/$executable" ] \
    || die "$appex enthält keine ausführbare Datei '$executable'. PRODUCT_NAME und
    CFBundleExecutable passen nicht zusammen."

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

  # Eine abweichende Version fällt sonst erst beim Upload auf (90473).
  local version build
  version="$(plutil -extract CFBundleShortVersionString raw -o - "$appex/Info.plist" 2>/dev/null || true)"
  build="$(plutil -extract CFBundleVersion raw -o - "$appex/Info.plist" 2>/dev/null || true)"
  [ "$version" = "$VERSION" ] \
    || die "Die Extension wurde als Version '$version' gebaut, die App steht auf '$VERSION'.
    Ohne VERSION in der Umgebung nimmt dieses Skript die Version der App; wurde sie hier gesetzt,
    muss sie zu Penban.Maui.csproj passen."
  [ "$build" = "$BUILD" ] \
    || die "Die Extension wurde mit Build-Nummer '$build' gebaut, die App steht auf '$BUILD'.
    Ohne BUILD in der Umgebung nimmt dieses Skript die Build-Nummer der App; wurde sie hier
    gesetzt, muss sie zu Penban.Maui.csproj passen."
  echo "Version:    $version ($build)"
}

# Die Datei, mit der der MAUI-Build die eingebettete Extension neu signiert. Xcode setzt beim
# Signieren mit dem Profil selbst application-identifier, Team-ID und beta-reports-active; die
# Datei im Repo nennt deshalb nur die App Group. Der MAUI-Build signiert aber mit genau dem, was
# in seiner Entitlements-Datei steht – die drei Schlüssel fehlen dann, und App Store Connect lehnt
# das Paket ab (90075 "application-identifier entitlement is missing"). Darum wird diese Datei hier
# aus der fertig signierten Extension gezogen: sie enthält damit genau das, was das Profil hergibt.
#
# Abgelegt wird sie neben dem .appex, also in build/<Configuration>-iphoneos – Penban.Maui.csproj
# liest sie dort (CodesignEntitlements). Eine Datei je Konfiguration ist Pflicht, nicht Ordnung:
# Debug wird mit dem Development-Profil signiert und bringt get-task-allow mit. Läge beides in
# einer Datei, trüge nach einem Debug-Build auch das nächste Release-Paket get-task-allow, und
# App Store Connect weist es dann ab (90164) – ohne dass am Release-Build selbst etwas falsch wäre.
write_entitlements() { # <pfad/zur/PenbanWidget.appex> <zielordner>
  local appex="$1" dir="$2" file="$2/PenbanWidget.entitlements" appid
  mkdir -p "$dir"
  codesign -d --entitlements - --xml "$appex" >"$file" 2>/dev/null || true
  plutil -lint "$file" >/dev/null 2>&1 \
    || die "Die Entitlements von $appex ließen sich nicht auslesen ($file)."

  appid="$(plutil -extract application-identifier raw -o - "$file" 2>/dev/null || true)"
  [ -n "$appid" ] \
    || die "Der Extension fehlt 'application-identifier'. Wird sie ohne Provisioning-Profil
    gebaut? Ein Upload zu App Store Connect scheitert daran (90075)."

  echo "Entitlements: $file"
  echo "App-ID:     $appid"
}

# Sucht ein Verteilungsprofil (kein Development-, kein Enterprise-Profil) für die Bundle-ID.
# Liefert den Namen, den xcodebuild als PROVISIONING_PROFILE_SPECIFIER erwartet.
find_distribution_profile() { # <bundle-id>
  local bundle_id="$1" dir profile appid taskallow all_devices scratch
  scratch="${TMPDIR:-/tmp}/penban-widget-profile.plist"
  for dir in "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles" \
             "$HOME/Library/MobileDevice/Provisioning Profiles"; do
    [ -d "$dir" ] || continue
    for profile in "$dir"/*.mobileprovision; do
      [ -e "$profile" ] || continue
      security cms -D -i "$profile" >"$scratch" 2>/dev/null || continue
      appid="$(plutil -extract Entitlements.application-identifier raw -o - "$scratch" 2>/dev/null || true)"
      taskallow="$(plutil -extract Entitlements.get-task-allow raw -o - "$scratch" 2>/dev/null || true)"
      all_devices="$(plutil -extract ProvisionsAllDevices raw -o - "$scratch" 2>/dev/null || true)"
      [ "$taskallow" = "true" ] && continue
      [ "$all_devices" = "true" ] && continue
      # Die Anführungszeichen nur um die Variable: ein quotiertes Muster wäre in "case" literal.
      case "$appid" in *."$bundle_id")
        plutil -extract Name raw -o - "$scratch" 2>/dev/null || true
        return 0;;
      esac
    done
  done
  return 1
}

# Das Verteilungszertifikat aus dem Schlüsselbund; leer, wenn keines vorhanden ist.
find_distribution_identity() {
  security find-identity -v -p codesigning 2>/dev/null \
    | sed -n 's/.*"\(Apple Distribution[^"]*\)".*/\1/p' | head -1 || true
}

cmd_build() {
  local configuration="${1:-Release}"
  local out_dir="$WIDGET_DIR/build/$configuration-iphoneos"
  local appex="$out_dir/PenbanWidget.appex"

  require_macos
  command -v xcodebuild >/dev/null 2>&1 || die "xcodebuild nicht gefunden. Bitte Xcode installieren
    und einmalig 'xcode-select --install' bzw. Xcode öffnen, damit die Lizenz akzeptiert wird."

  cmd_generate

  # Release wird fest mit dem App-Store-Profil signiert. Automatisch signiert wählt Xcode hier
  # das Development-Profil, und das landet als embedded.mobileprovision im Bundle: der MAUI-Build
  # signiert die Extension zwar später neu, tauscht das eingebettete Profil aber nicht aus, und
  # App Store Connect lehnt das Paket dann ab (90161 "Invalid Provisioning Profile").
  local -a sign=()
  if [ "$configuration" = "Release" ]; then
    local identity profile
    identity="$(find_distribution_identity)"
    if profile="$(find_distribution_profile "$EXTENSION_BUNDLE_ID")" && [ -n "$identity" ]; then
      log "Signiere mit Verteilungsprofil: $profile"
      sign=(CODE_SIGN_STYLE=Manual CODE_SIGN_IDENTITY="$identity" PROVISIONING_PROFILE_SPECIFIER="$profile")
    else
      warn "Kein Apple-Distribution-Zertifikat oder App-Store-Profil für $EXTENSION_BUNDLE_ID
    gefunden – die Extension wird mit dem Development-Profil gebaut. Ein Upload zu App Store
    Connect scheitert damit (90161 'Invalid Provisioning Profile')."
    fi
  fi

  log "Baue $SCHEME ($configuration)"
  # Die Version steht in project.yml nur als Notnagel für einen Build von Hand in Xcode; hier wird
  # immer die der App mitgegeben, damit beide nicht auseinanderlaufen können.
  local -a version=(MARKETING_VERSION="$VERSION" CURRENT_PROJECT_VERSION="$BUILD")
  # CONFIGURATION_BUILD_DIR statt -derivedDataPath: so landet das .appex direkt in dem Ordner,
  # den der MAUI-Build erwartet, ohne Zwischenkopie.
  (cd "$WIDGET_DIR" && xcodebuild \
    -project "$PROJECT" \
    -scheme "$SCHEME" \
    -configuration "$configuration" \
    -destination 'generic/platform=iOS' \
    -allowProvisioningUpdates \
    CONFIGURATION_BUILD_DIR="$out_dir" \
    ${version[@]+"${version[@]}"} \
    ${sign[@]+"${sign[@]}"} \
    build)

  [ -d "$appex" ] || die "Build lief durch, aber $appex wurde nicht erzeugt."

  log "Prüfe die Extension"
  verify_appex "$appex"
  write_entitlements "$appex" "$out_dir"

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

# Prüft eine fertige .appex gegen die Version der App. Der MAUI-Build bettet ein, was gerade in
# build/<Configuration>-iphoneos liegt - eine .appex aus einem früheren Build fällt sonst erst beim
# Upload auf (90473) oder auf dem Gerät daran, dass das Widget gar nicht angeboten wird.
cmd_check() { # <pfad/zur/PenbanWidget.appex>
  local appex="${1:-}" version build
  [ -n "$appex" ] || die "Aufruf: scripts/ios-widget.sh check <Pfad zur .appex>"
  [ -f "$appex/Info.plist" ] || die "$appex/Info.plist gibt es nicht - dort liegt keine Extension."

  version="$(plutil -extract CFBundleShortVersionString raw -o - "$appex/Info.plist" 2>/dev/null || true)"
  build="$(plutil -extract CFBundleVersion raw -o - "$appex/Info.plist" 2>/dev/null || true)"
  if [ "$version" != "$VERSION" ] || [ "$build" != "$BUILD" ]; then
    die "Die Widget-Extension ist $version ($build), die App ist $VERSION ($BUILD).
    App Store Connect weist so ein Paket mit 90473 zurück, und auf dem Gerät wird das Widget nicht
    angeboten. Die .appex ist ein Überbleibsel eines früheren Builds - neu bauen mit
    'scripts/ios-widget.sh build ${CONFIGURATION:-Release}'."
  fi
  echo "Widget-Extension: $version ($build) - passt zur App ($VERSION ($BUILD))."
}

case "${1:-build}" in
  build)           shift || true; cmd_build "${1:-Release}";;
  check)           shift || true; cmd_check "${1:-}";;
  clean)           cmd_clean;;
  -h|--help|help)  sed -n '2,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//';;
  *)               die "Unbekanntes Kommando '$1' (build [Debug|Release] | check <Appex> | clean)";;
esac
