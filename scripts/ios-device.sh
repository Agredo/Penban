#!/usr/bin/env bash
#
# Penban – iOS-Build für ein angeschlossenes Gerät, samt Widget.
#
#   scripts/ios-device.sh info             Geräte und Signierung anzeigen (Standard)
#   scripts/ios-device.sh build            Bundle fürs Gerät bauen (inkl. Widget)
#   scripts/ios-device.sh install [udid]   gebautes Bundle auf dem Gerät installieren
#   scripts/ios-device.sh all [udid]       bauen und installieren
#   scripts/ios-device.sh clean            obj/bin des Geräte-Builds (iOS) löschen
#
# Konfiguration über Umgebungsvariablen (alle optional, Defaults kommen aus dem Projekt
# bzw. dem Schlüsselbund):
#
#   UDID=<udid>                 Zielgerät (Hardware-UDID aus Xcode oder devicectl-Kennung)
#   CONFIG=Debug                Konfiguration; Debug ist das, was "Run" in Xcode entspricht
#   CODESIGN_KEY                Signatur-Identität, Default: "Apple Development: ..."
#   CODESIGN_PROVISION          UUID oder Name des Development-Profils
#   VERSION / BUILD             Marketing-Version / Build-Nummer
#   IOS_TFM=net10.0-ios         Zielframework
#
# Warum es dieses Skript gibt:
# Der MAUI-Build bettet die Widget-Extension nur ein, wenn sie in
# Penban/Penban.Widget.iOS/build/<Konfiguration>-iphoneos liegt (AdditionalAppExtensions in
# Penban.Maui.csproj). Fehlt sie, warnt er bloß und die App landet ohne Widget auf dem Gerät.
# Die Konfiguration entscheidet außerdem über die Signatur: Release wird mit dem App-Store-Profil
# gebaut, und eine so signierte .appex nimmt iOS auf einem Entwicklungsgerät nicht an – das Widget
# erscheint dann nicht in der Galerie. Wer die App also mit 'dotnet build' aufs Gerät lädt, ohne
# die Extension in derselben Konfiguration zu bauen, bekommt eine App ohne Widget, ohne
# Fehlermeldung. Dieses Skript baut deshalb immer beides zusammen und prüft danach am fertigen
# Bundle, dass die Extension dabei und development-signiert ist. Für App Store Connect ist
# 'scripts/ios-testflight.sh' zuständig – das baut dieselbe Extension in Release.
#
# Voraussetzung auf dem Gerät: Developer Mode (Einstellungen → Datenschutz & Sicherheit) und ein
# Development-Profil, das genau dieses Gerät und die App Group enthält.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$REPO_ROOT/Penban/Penban.Maui/Penban.Maui.csproj"
# global.json liegt in Penban/ – die SDK-Auflösung richtet sich nach dem Arbeitsverzeichnis.
PROJ_DIR="$(dirname "$PROJ")"
WIDGET_SCRIPT="$REPO_ROOT/scripts/ios-widget.sh"
IOS_TFM="${IOS_TFM:-net10.0-ios}"
RID="ios-arm64"
CONFIG="${CONFIG:-Debug}"
SCRATCH_DIR="$(mktemp -d "${TMPDIR:-/tmp}/penban-device.XXXXXX")"
BUILD_LOG="$SCRATCH_DIR/build.log"
DEVICES_JSON="$SCRATCH_DIR/devices.json"

cleanup() { rm -rf "$SCRATCH_DIR"; }
trap cleanup EXIT

log()  { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[33mHinweis:\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[31mFehler:\033[0m %s\n' "$*" >&2; exit 1; }

csproj_value() { sed -n "s/.*<$1>\(.*\)<\/$1>.*/\1/p" "$PROJ" | head -1 | tr -d '[:space:]'; }

BUNDLE_ID="${BUNDLE_ID:-$(csproj_value ApplicationId)}"
# Muss zu Penban.Widget.iOS/project.yml und Platforms/iOS/Entitlements.plist passen.
APP_GROUP="${APP_GROUP:-group.com.agredoapplication.penban}"
VERSION="${VERSION:-$(csproj_value ApplicationDisplayVersion)}"
BUILD="${BUILD:-$(csproj_value ApplicationVersion)}"

# ---------------------------------------------------------------- SDK-Auswahl --
# Dieselbe Auswahl wie in scripts/ios-testflight.sh: das dort gepinnte SDK ist lokal oft nicht
# installiert. In dem Fall wird in einem temporären Verzeichnis gebaut, in dem ein global.json auf
# das beste installierte SDK mit MAUI-Workload zeigt – die Projektdateien bleiben dieselben.
band_of() { # 10.0.103 -> 10.0.100 ; 11.0.100-preview.7.26381.103 -> 11.0.100-preview.7
  local v="$1" major minor patch rest base
  IFS='.' read -r major minor patch rest <<<"$v"
  base="${patch%%-*}"
  if [ "$patch" != "$base" ]; then
    printf '%s.%s.%s-%s.%s\n' "$major" "$minor" "${base:0:1}00" "${patch#*-}" "${rest%%.*}"
  else
    printf '%s.%s.%s\n' "$major" "$minor" "${base:0:1}00"
  fi
}

sdk_has_maui() { # <sdk-version> <sdk-pfad>
  local root band
  root="${2#[}"; root="${root%]}"; root="$(dirname "$root")"
  band="$(band_of "$1")"
  [ -f "$root/metadata/workloads/$band/InstalledWorkloads/maui" ] \
    || [ -f "$root/metadata/workloads/$band/InstalledWorkloads/maui-ios" ]
}

select_build_dir() {
  local sdks resolved="" version sdk_path best=""
  sdks="$(cd "$SCRATCH_DIR" && dotnet --list-sdks 2>/dev/null || true)"
  [ -n "$sdks" ] || die "'dotnet' wurde nicht gefunden. Bitte das .NET SDK installieren."

  # Im Projektverzeichnis prüfen: löst dotnet dort ein SDK mit MAUI-Workload auf?
  if resolved="$(cd "$PROJ_DIR" && dotnet --version 2>/dev/null)"; then
    while read -r version sdk_path; do
      [ "$version" = "$resolved" ] || continue
      if sdk_has_maui "$version" "$sdk_path"; then
        BUILD_DIR="$PROJ_DIR"
        SDK_PINNED="$resolved"
        return
      fi
    done <<<"$sdks"
  fi

  while read -r version sdk_path; do
    [ -n "$version" ] || continue
    sdk_has_maui "$version" "$sdk_path" || continue
    case "$version" in 10.*) best="$version"; break;; *) [ -n "$best" ] || best="$version";; esac
  done <<<"$sdks"
  [ -n "$best" ] || die "Kein installiertes .NET SDK mit MAUI-Workload gefunden (erwartet: 10.0.x).
    'dotnet workload install maui' im gepinnten SDK aus Penban/global.json ausführen."

  warn "Penban/global.json verlangt ein hier nicht installiertes SDK – es wird mit $best gebaut."
  printf '{\n  "sdk": { "version": "%s", "rollForward": "latestPatch" }\n}\n' "$best" \
    >"$SCRATCH_DIR/global.json"
  BUILD_DIR="$SCRATCH_DIR"
  SDK_PINNED="$best"
}

# ------------------------------------------------------- Signing + Gerät --
profile_uuid=""
profile_name=""

# Eine Datei aus einem .mobileprovision lesen; leer, wenn der Schlüssel fehlt.
profile_value() { # <pfad> <schlüssel>
  security cms -D -i "$1" 2>/dev/null | plutil -extract "$2" raw -o - - 2>/dev/null || true
}

# Development-Profil für die App-ID: get-task-allow, kein Enterprise-Profil, App-ID exakt.
# Ein Gerät aus $1 wird bevorzugt – sonst schlägt die Installation erst auf dem Gerät fehl.
find_development_profile() { # <hardware-udid oder leer>
  local hw="${1:-}" dir profile decoded appid taskallow all_devices uuid name
  local match="" fallback="" devices
  for dir in "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles" \
             "$HOME/Library/MobileDevice/Provisioning Profiles"; do
    [ -d "$dir" ] || continue
    for profile in "$dir"/*.mobileprovision; do
      [ -e "$profile" ] || continue
      appid="$(profile_value "$profile" Entitlements.application-identifier)"
      # Nur die Anführungszeichen um die Variable: ein quotiertes Muster wäre in "case" literal
      # und das Sternchen damit kein Platzhalter mehr. Die Wildcard-Profile (Team.*) fallen hier
      # heraus, weil sie die App-ID nicht nennen – die App Group braucht ohnehin eine explizite.
      case "$appid" in *."$BUNDLE_ID") ;; *) continue;; esac
      taskallow="$(profile_value "$profile" Entitlements.get-task-allow)"
      [ "$taskallow" = "true" ] || continue
      all_devices="$(profile_value "$profile" ProvisionsAllDevices)"
      [ "$all_devices" = "true" ] && continue

      uuid="$(profile_value "$profile" UUID)"
      name="$(profile_value "$profile" Name)"
      [ -n "$uuid" ] || continue
      [ -n "$fallback" ] || fallback="$uuid|$name"
      [ -n "$hw" ] || continue
      devices="$(security cms -D -i "$profile" 2>/dev/null | plutil -p - 2>/dev/null || true)"
      case "$devices" in
        *"$hw"*) [ -n "$match" ] || match="$uuid|$name";;
      esac
    done
  done
  if [ -n "$match" ]; then printf '%s' "$match"; return 0; fi
  if [ -n "$fallback" ]; then printf '%s' "$fallback"; return 1; fi
  return 2
}

find_development_identity() {
  security find-identity -v -p codesigning 2>/dev/null \
    | sed -n 's/.*"\(Apple Development[^"]*\)".*/\1/p' | head -1 || true
}

detect_signing() { # <hardware-udid oder leer>
  if [ -n "${CODESIGN_KEY:-}" ]; then
    SIGNING_KEY="$CODESIGN_KEY"
  else
    SIGNING_KEY="$(find_development_identity)"
  fi

  if [ -n "${CODESIGN_PROVISION:-}" ]; then
    profile_uuid="$CODESIGN_PROVISION"
    profile_name="manuell gesetzt"
    return
  fi

  local hw="${1:-}" found rc=0
  found="$(find_development_profile "$hw")" || rc=$?
  if [ -n "$found" ]; then
    profile_uuid="${found%%|*}"
    profile_name="${found##*|}"
    if [ "$rc" -ne 0 ] && [ -n "$hw" ]; then
      warn "Das Development-Profil '$profile_name' listet das Gerät $hw nicht.
    Installation schlägt fehl, bis das Gerät im Profil steht (Xcode → Signing & Capabilities)."
    fi
  fi
  return 0
}

# Geräteliste als JSON; die Angaben stehen unter result.devices[<i>].
list_devices() {
  xcrun devicectl list devices --json-output "$DEVICES_JSON" >/dev/null 2>&1 \
    || die "'xcrun devicectl list devices' schlug fehl – ist Xcode installiert?"
}

device_field() { # <index> <schlüsselpfad>
  plutil -extract "result.devices.$1.$2" raw -o - "$DEVICES_JSON" 2>/dev/null || true
}

# Setzt DEVICE, DEVICE_HW_UDID und DEVICE_NAME. Ein übergebenes Ziel wird unverändert
# weitergereicht: devicectl nimmt Hardware-UDID und Kennung.
resolve_device() { # <udid oder leer>
  local want="${1:-${UDID:-}}" i=0 id hw name state connected=0 found="" fallback=""
  # Schon aufgelöst (etwa 'all', das baut und dann installiert): das Ziel bleibt dasselbe.
  if [ -n "${DEVICE:-}" ] && { [ -z "$want" ] || [ "$want" = "$DEVICE" ] || [ "$want" = "$DEVICE_HW_UDID" ]; }; then
    printf 'Gerät: %s (%s)\n' "$DEVICE_NAME" "$DEVICE_HW_UDID"
    return 0
  fi
  list_devices

  while :; do
    id="$(device_field "$i" identifier)"
    [ -n "$id" ] || break
    hw="$(device_field "$i" hardwareProperties.udid)"
    name="$(device_field "$i" deviceProperties.name)"
    state="$(device_field "$i" connectionProperties.tunnelState)"
    [ "$state" = "connected" ] && connected=$((connected + 1))
    if [ -n "$want" ] && { [ "$id" = "$want" ] || [ "$hw" = "$want" ]; }; then
      found="$id"; DEVICE_HW_UDID="$hw"; DEVICE_NAME="$name"
    fi
    [ -z "$want" ] && [ "$state" = "connected" ] && { fallback="$id"; DEVICE_HW_UDID="$hw"; DEVICE_NAME="$name"; }
    i=$((i + 1))
  done

  if [ -n "$want" ]; then
    if [ -z "$found" ]; then
      device_error="Gerät '$want' ist nicht bekannt. Verbundene Geräte:
$(device_table)"
      return 1
    fi
    DEVICE="$found"
  else
    if [ "$connected" -ne 1 ]; then
      device_error="$connected Geräte sind verbunden – bitte eines angeben:
$(device_table)"
      return 1
    fi
    DEVICE="$fallback"
  fi
  [ -n "$DEVICE" ] || { device_error='Kein Gerät ausgewählt.'; return 1; }
  printf 'Gerät: %s (%s)\n' "$DEVICE_NAME" "$DEVICE_HW_UDID"
}

# Wie resolve_device, aber ein fehlendes oder mehrdeutiges Ziel bricht ab.
require_device() {
  resolve_device "$@" || die "${device_error:-Kein Gerät ausgewählt.}"
}

device_table() {
  local i=0 id hw name state
  list_devices
  while :; do
    id="$(device_field "$i" identifier)"
    [ -n "$id" ] || break
    hw="$(device_field "$i" hardwareProperties.udid)"
    name="$(device_field "$i" deviceProperties.name)"
    state="$(device_field "$i" connectionProperties.tunnelState)"
    printf '    %s  %-24s %s\n' "$hw" "$name" "$state"
    i=$((i + 1))
  done
}

# ------------------------------------------------------------ Build-Artefakte --
# Nach einem Wechsel von SDK, Workload oder iOS-Paket kann ein inkrementeller Build eine
# Microsoft.iOS.dll ins Bundle kopieren, die nicht zum Executable passt; die App stürzt dann beim
# Start ab. Für dieses Skript reicht es, die Artefakte der eigenen Konfiguration zu löschen.
clean_ios_outputs() {
  local path removed=0
  while IFS= read -r path; do
    rm -rf "$path"
    removed=$((removed + 1))
  done < <(find "$REPO_ROOT/Penban" -type d \
             \( -path "*/obj/$CONFIG/$IOS_TFM" -o -path "*/bin/$CONFIG/$IOS_TFM" \) -prune -print)
  echo "Build-Artefakte entfernt: $removed Verzeichnis(se)"
}

# Die Extension muss in derselben Konfiguration gebaut werden wie die App – nur so liegt sie in
# dem Ordner, den Penban.Maui.csproj einbettet, und nur so trägt sie Signatur und Profil, die ein
# Entwicklungsgerät annimmt (Development statt App Store).
build_widget_extension() {
  [ -x "$WIDGET_SCRIPT" ] || die "Widget-Skript nicht gefunden oder nicht ausführbar: $WIDGET_SCRIPT
    Ohne die Extension im Bundle hätte die App keine Widgets."

  log "Baue Widget-Extension ($CONFIG)"
  VERSION="$VERSION" BUILD="$BUILD" bash "$WIDGET_SCRIPT" build "$CONFIG"
}

entitlements_of() { # <bundle>
  codesign -d --entitlements - --xml "$1" 2>/dev/null || true
}

# Beide Teile des Bundles müssen dieselbe App Group nennen – sonst sieht das Widget den geteilten
# Ordner nicht und bleibt leer – und die Extension muss für ein Gerät signiert sein.
verify_device_app() { # <pfad/zur/App.app>
  local app="$1" appex key app_version app_build ext_version ext_build debug

  printf '%s' "$(plutil -extract CFBundleIdentifier raw -o - "$app/Info.plist" 2>/dev/null)" \
    | grep -qx "$BUNDLE_ID" || die "$app hat nicht die Bundle-ID $BUNDLE_ID."

  appex="$(find "$app/PlugIns" -maxdepth 1 -type d -name '*.appex' -print -quit 2>/dev/null || true)"
  [ -n "$appex" ] || die "Im Bundle steckt keine Widget-Extension ($app/PlugIns).
    Der MAUI-Build bettet sie nur ein, wenn '$WIDGET_SCRIPT build $CONFIG' gelaufen ist und
    Penban/Penban.Widget.iOS/build/$CONFIG-iphoneos/PenbanWidget.appex existiert. Eine App ohne
    Widget kommt aufs Gerät, ohne dass der Build fehlschlägt – deshalb bricht dieses Skript hier ab."
  echo "Widget-Extension: $(basename "$appex")"

  app_version="$(plutil -extract CFBundleShortVersionString raw -o - "$app/Info.plist" 2>/dev/null || true)"
  app_build="$(plutil -extract CFBundleVersion raw -o - "$app/Info.plist" 2>/dev/null || true)"
  ext_version="$(plutil -extract CFBundleShortVersionString raw -o - "$appex/Info.plist" 2>/dev/null || true)"
  ext_build="$(plutil -extract CFBundleVersion raw -o - "$appex/Info.plist" 2>/dev/null || true)"
  [ "$ext_version" = "$app_version" ] && [ "$ext_build" = "$app_build" ] \
    || die "Die Extension hat eine andere Version als die App (Extension: $ext_version ($ext_build),
    App: $app_version ($app_build)). Sie stammt aus einem früheren Build – '$WIDGET_SCRIPT build
    $CONFIG' ausführen."
  echo "Version:          $ext_version ($ext_build)"

  for key in "$app|die App" "$appex|die Widget-Extension"; do
    printf '%s' "$(entitlements_of "${key%%|*}")" | grep -q "$APP_GROUP" \
      || die "${key##*|} nennt die App Group '$APP_GROUP' nicht. Das Widget findet den geteilten
    Ordner dann nicht und bleibt bei 'Öffne Penban…' stehen."
  done
  echo "App Group:        $APP_GROUP"

  debug="$(entitlements_of "$appex" | plutil -extract get-task-allow raw -o - - 2>/dev/null || true)"
  if [ "$debug" != "true" ] && [ "$debug" != "1" ]; then
    die "Die Extension ist ohne 'get-task-allow' signiert, also mit dem App-Store-Profil gebaut.
    Ein Entwicklungsgerät nimmt sie nicht an und zeigt das Widget nicht an.
    Lösung: '$WIDGET_SCRIPT build $CONFIG' mit CODESIGN_PROVISION=<Development-Profil>
    ausführen, dann '$0 clean' und erneut bauen."
  fi
  echo "Signatur:         Development (get-task-allow)"
}

# Nach der Installation am Gerät nachsehen: ohne App Group in den Entitlements ist die App keine
# Hilfe für das Widget. Die Angabe steht nur in der ausführlichen Ausgabe.
verify_installed_app() { # <udid>
  local block
  block="$(xcrun devicectl device info apps --device "$1" --verbose 2>/dev/null | awk -v id="$BUNDLE_ID" '
    /CoreDeviceClientJSONSupport.AppInfo/ { b = "" }
    { b = b $0 "\n" }
    /bundleIdentifier: Optional\("/ && index($0, "\"" id "\"") { printf "%s", b; exit }
  ')" || true
  [ -n "$block" ] || { warn "Die installierte App ließ sich nicht abfragen – bitte auf dem Gerät
    prüfen, ob das Widget in der Galerie erscheint."; return 0; }

  local version
  version="$(printf '%s' "$block" | sed -n 's/^ *- version: Optional("\(.*\)")$/\1/p' | head -1)"
  [ -n "$version" ] && echo "Installiert:      $BUNDLE_ID $version"

  # devicectl nennt in "info apps" kein Feld, das die App Group verlässlich zeigt
  # (appGroupIdentifiers ist dort auch für Apps mit App Group nil). Belastbar ist der Container
  # selbst: existiert er auf dem Gerät, hat das installierte Bundle die App Group.
  local probe
  probe="$(mktemp -d)"
  if xcrun devicectl device copy from --device "$1" --domain-type appGroupDataContainer \
       --domain-identifier "$APP_GROUP" --source . --destination "$probe/group" \
       >"$probe/log" 2>&1; then
    echo "App Group:        $APP_GROUP vorhanden"
  else
    warn "Der App-Group-Container $APP_GROUP ließ sich nicht abfragen. Sicher ist der Fix erst,
    wenn das Widget auf dem Home-Bildschirm in der Galerie auftaucht."
  fi
  rm -rf "$probe"
}

# ------------------------------------------------------------------- Kommandos --
cmd_info() {
  select_build_dir
  local want="${1:-${UDID:-}}" hw=""
  resolve_device "$want" >/dev/null 2>&1 && hw="$DEVICE_HW_UDID" || true
  detect_signing "$hw"

  printf '\nProjekt:          %s\n' "$PROJ"
  printf 'Bundle-ID:        %s\n' "$BUNDLE_ID"
  printf 'Version:          %s (%s)\n' "$VERSION" "$BUILD"
  printf 'Konfiguration:    %s\n' "$CONFIG"
  printf 'Build-SDK:        %s\n' "$SDK_PINNED"
  printf 'Build-Verzeichnis: %s\n' "$BUILD_DIR"
  printf 'Signierung:       %s\n' "${SIGNING_KEY:-kein "Apple Development"-Zertifikat gefunden}"
  if [ -n "$profile_uuid" ]; then
    printf 'Development-Profil: %s (%s)\n' "$profile_name" "$profile_uuid"
  else
    printf 'Development-Profil: keines für %s gefunden\n' "$BUNDLE_ID"
  fi

  local appex="$REPO_ROOT/Penban/Penban.Widget.iOS/build/$CONFIG-iphoneos/PenbanWidget.appex"
  if [ -d "$appex" ]; then
    printf 'Widget-Extension: vorhanden (%s)\n' "$appex"
  else
    printf 'Widget-Extension: fehlt – der Build würde die App ohne Widget bauen\n'
  fi

  printf '\nGeräte:\n'
  device_table
}

cmd_build() {
  local -a args
  local hw=""
  select_build_dir
  # Das Gerät wird, wenn möglich, schon hier bestimmt: welches Development-Profil passt, hängt
  # davon ab, ob es das Gerät listet. Ohne verbundenes Gerät wird trotzdem gebaut.
  if resolve_device "${UDID:-}" >/dev/null 2>&1; then hw="$DEVICE_HW_UDID"; fi
  detect_signing "$hw"

  [ -n "${SIGNING_KEY:-}" ] || die 'Kein "Apple Development"-Zertifikat im Schlüsselbund gefunden.
    Xcode → Settings → Accounts → Team NTMYS336K2 → Manage Certificates → + → Apple Development'
  [ -n "$profile_uuid" ] || die "Kein Development-Provisioning-Profil für $BUNDLE_ID gefunden.
    developer.apple.com → Certificates, Identifiers & Profiles → Profiles → + → iOS App Development
    → App-ID $BUNDLE_ID, Zertifikat und Gerät auswählen, Capability App Groups muss an der App-ID
    aktiviert sein."

  log "Bereinige alte iOS-Build-Artefakte (obj/bin/$CONFIG/$IOS_TFM)"
  clean_ios_outputs

  build_widget_extension

  log "Baue $CONFIG-Bundle: $BUNDLE_ID $VERSION ($BUILD)"
  echo "Signierung: ${SIGNING_KEY}"
  echo "Profil:     ${profile_name} (${profile_uuid})"
  echo "SDK:        ${SDK_PINNED}"

  args=(build "$PROJ" -f "$IOS_TFM" -c "$CONFIG"
    -p:RuntimeIdentifier="$RID"
    -p:EnableWindowsTargeting=true
    -p:ApplicationDisplayVersion="$VERSION"
    -p:ApplicationVersion="$BUILD"
    -p:CodesignKey="$SIGNING_KEY"
    -p:CodesignProvision="$profile_uuid"
    -v minimal)

  local rc=0 attempt=1
  while :; do
    rc=0
    if [ "$attempt" -eq 1 ]; then
      (cd "$BUILD_DIR" && dotnet "${args[@]}") 2>&1 | tee "$BUILD_LOG" || rc=$?
    else
      (cd "$BUILD_DIR" && dotnet "${args[@]}" -p:ValidateXcodeVersion=false) 2>&1 | tee "$BUILD_LOG" || rc=$?
    fi
    [ "$rc" -eq 0 ] && break
    if [ "$attempt" -eq 1 ] && grep -q "requires Xcode" "$BUILD_LOG"; then
      warn "Das installierte .NET-für-iOS-Paket erwartet eine andere Xcode-Version; die Prüfung wird übersprungen (ValidateXcodeVersion=false)."
      attempt=2
      continue
    fi
    die "Build fehlgeschlagen – siehe Ausgabe oben."
  done

  APP_PATH="$(find_app)"
  [ -n "$APP_PATH" ] \
    || die "Build erfolgreich, aber kein .app-Bundle unter bin/$CONFIG/$IOS_TFM gefunden."
  log "Bundle erstellt: $APP_PATH"

  log "Prüfe die eingebettete Widget-Extension"
  verify_device_app "$APP_PATH"

  printf 'Nächster Schritt: %s all %s\n' "$0" "${DEVICE_HW_UDID:-<udid>}"
}

# Das zuletzt gebaute Bundle. 'build' und 'install' laufen in getrennten Prozessen, deshalb wird
# hier gesucht statt gemerkt.
find_app() {
  find "$REPO_ROOT/Penban/Penban.Maui/bin/$CONFIG/$IOS_TFM" \
    -maxdepth 3 -type d -name '*.app' -print -quit 2>/dev/null || true
}

cmd_install() {
  [ -n "${APP_PATH:-}" ] && [ -d "$APP_PATH" ] || APP_PATH="$(find_app)"
  [ -n "$APP_PATH" ] && [ -d "$APP_PATH" ] \
    || die "Noch kein Bundle gebaut – zuerst '$0 build' ausführen."
  require_device "${1:-}"
  log "Installiere auf $DEVICE_NAME"
  xcrun devicectl device install app --device "$DEVICE" "$APP_PATH" \
    || die "Installation fehlgeschlagen. Häufigste Ursachen: Developer Mode ist aus, das Gerät ist
    gesperrt, oder das Development-Profil ($profile_uuid) enthält dieses Gerät nicht."
  log "Prüfe die installierte App"
  verify_installed_app "$DEVICE"
  log "Fertig. Widget auf dem Home-Bildschirm hinzufügen: lange drücken → + → Penban."
}

case "${1:-info}" in
  info)            cmd_info;;
  build)           cmd_build;;
  clean)           clean_ios_outputs;;
  install)         shift; cmd_install "${1:-}";;
  all)             shift; cmd_build && cmd_install "${1:-}";;
  -h|--help|help)  sed -n '2,35p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//';;
  *)               die "Unbekanntes Kommando '$1' (info | build | install [udid] | clean | all [udid])";;
esac
