#!/usr/bin/env bash
#
# Penban – iOS Release-Build (IPA) und TestFlight-Upload.
#
#   scripts/ios-testflight.sh info              Signing-/SDK-Status anzeigen (Standard)
#   scripts/ios-testflight.sh build             Release-IPA für den App Store bauen
#   scripts/ios-testflight.sh upload <ipa>      vorhandene IPA hochladen
#   scripts/ios-testflight.sh all               bauen und direkt hochladen
#   scripts/ios-testflight.sh clean             veraltete obj/bin-Artefakte (iOS) löschen
#
# Konfiguration über Umgebungsvariablen (alle optional, Defaults kommen aus dem
# Projekt bzw. dem Schlüsselbund):
#
#   VERSION=0.1                 Marketing-Version (CFBundleShortVersionString)
#   BUILD=42                    Build-Nummer (CFBundleVersion), muss pro Upload steigen
#   CODESIGN_KEY                Signatur-Identität, Default: "Apple Distribution: ..."
#   CODESIGN_PROVISION          UUID oder Name des App-Store-Provisioning-Profils
#   ASC_API_KEY / ASC_API_ISSUER / ASC_P8_FILE   App-Store-Connect-API-Key für altool
#   ASC_USERNAME / ASC_PASSWORD                  Alternative: Apple-ID + App-Passwort
#   IOS_TFM=net10.0-ios         Zielframework
#
# Einmalige Voraussetzungen:
#   1. Apple Developer Program, App-ID com.agredoapplication.panban, App in App Store Connect
#   2. "Apple Distribution"-Zertifikat (Xcode → Settings → Accounts → Manage Certificates)
#   3. App-Store-Provisioning-Profil für die App-ID
#   4. App-Store-Connect-API-Key (.p8) in ~/.appstoreconnect/private_keys/ oder Apple-ID + App-Passwort
#
# Zum Build: vor jedem Release-Build werden obj/bin für $IOS_TFM geleert und das
# erzeugte Bundle wird geprüft. Ein inkrementeller iOS-Build nach einem Wechsel
# von SDK, Workload oder iOS-Paket kann eine Microsoft.iOS.dll in das Bundle
# kopieren, deren Runtime-Hash nicht zum Executable passt; die App stürzt dann
# direkt beim Start ab ("The static registrar map ... is invalid").
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$REPO_ROOT/Penban/Penban.Maui/Penban.Maui.csproj"
# global.json liegt in Penban/ – die SDK-Auflösung richtet sich nach dem
# Arbeitsverzeichnis, deshalb muss ab hier geprüft/gebaut werden.
PROJ_DIR="$(dirname "$PROJ")"
IOS_TFM="${IOS_TFM:-net10.0-ios}"
RID="ios-arm64"
SCRATCH_DIR="$(mktemp -d "${TMPDIR:-/tmp}/penban-ios.XXXXXX")"
BUILD_LOG="$SCRATCH_DIR/build.log"

cleanup() { rm -rf "$SCRATCH_DIR"; }
trap cleanup EXIT

log()  { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[33mHinweis:\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[31mFehler:\033[0m %s\n' "$*" >&2; exit 1; }

csproj_value() { sed -n "s/.*<$1>\(.*\)<\/$1>.*/\1/p" "$PROJ" | head -1 | tr -d '[:space:]'; }

BUNDLE_ID="${BUNDLE_ID:-$(csproj_value ApplicationId)}"
VERSION="${VERSION:-$(csproj_value ApplicationDisplayVersion)}"
BUILD="${BUILD:-$(csproj_value ApplicationVersion)}"

# ---------------------------------------------------------------- SDK-Auswahl --
# Penban/global.json pinnt eine SDK-Version, die lokal ggf. nicht installiert ist.
# Ist sie vorhanden, wird im Repo gebaut; sonst in einem temporären Verzeichnis mit
# gepinnter SDK-Version (identische Projektdateien, nur die SDK-Auflösung weicht ab).
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

DOTNET_SDKS="$(cd "$SCRATCH_DIR" && dotnet --list-sdks 2>/dev/null || true)"
[ -n "$DOTNET_SDKS" ] || die "'dotnet' wurde nicht gefunden. Bitte das .NET SDK installieren."

sdk_has_maui() { # <sdk-version> <sdk-pfad>
  local root band
  root="${2#[}"; root="${root%]}"; root="$(dirname "$root")"
  band="$(band_of "$1")"
  # Es genügt ein iOS-Workload (maui-ios); das vollständige 'maui' wird nur gebraucht,
  # wenn auch Android/MacCatalyst gebaut werden.
  [ -f "$root/metadata/workloads/$band/InstalledWorkloads/maui" ] \
    || [ -f "$root/metadata/workloads/$band/InstalledWorkloads/maui-ios" ]
}

select_build_dir() {
  local resolved="" version sdk_path requested major_minor best=""

  # Im Projektverzeichnis prüfen: löst dotnet dort ein SDK mit MAUI-Workload auf?
  if resolved="$(cd "$PROJ_DIR" && dotnet --version 2>/dev/null)"; then
    while read -r version sdk_path; do
      [ "$version" = "$resolved" ] || continue
      if sdk_has_maui "$version" "$sdk_path"; then
        BUILD_DIR="$PROJ_DIR"
        SDK_PINNED="$resolved"
        return
      fi
    done <<<"$DOTNET_SDKS"
  else
    resolved=""
  fi

  requested="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
    "$REPO_ROOT/Penban/global.json" | head -1)"
  major_minor="${requested%.*}"

  while read -r version sdk_path; do
    [ -n "$version" ] || continue
    sdk_has_maui "$version" "$sdk_path" || continue
    case "$version" in "$major_minor".*) best="$version"; break;; esac
  done <<<"$DOTNET_SDKS"

  if [ -z "$best" ]; then
    while read -r version sdk_path; do
      [ -n "$version" ] || continue
      sdk_has_maui "$version" "$sdk_path" && best="$version"
    done <<<"$DOTNET_SDKS"
  fi
  [ -n "$best" ] || die "Kein installiertes .NET SDK mit MAUI-Workload gefunden (erwartet: $major_minor)."

  if [ -n "$resolved" ]; then
    warn "Im Projekt löst dotnet auf SDK $resolved auf, dafür ist kein MAUI-Workload installiert – es wird mit SDK $best gebaut."
  else
    warn "global.json verlangt SDK $requested – das ist lokal nicht installiert – es wird mit SDK $best gebaut."
  fi

  printf '{\n  "sdk": { "version": "%s", "rollForward": "latestPatch" }\n}\n' "$best" >"$SCRATCH_DIR/global.json"
  BUILD_DIR="$SCRATCH_DIR"
  SDK_PINNED="$best"
}

# ------------------------------------------------------- Signing + Provisioning --
profile_name=""
profile_uuid=""

# Sucht ein Nicht-Development-Profil für die Bundle-ID (exakter Treffer vor Wildcard).
find_distribution_profile() {
  local dir profile decoded appid taskallow all_devices name uuid exact="" wildcard=""
  for dir in "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles" \
             "$HOME/Library/MobileDevice/Provisioning Profiles"; do
    [ -d "$dir" ] || continue
    for profile in "$dir"/*.mobileprovision; do
      [ -e "$profile" ] || continue
      decoded="$SCRATCH_DIR/profile.plist"
      security cms -D -i "$profile" >"$decoded" 2>/dev/null || continue
      appid="$(plutil -extract Entitlements.application-identifier raw -o - "$decoded" 2>/dev/null || true)"
      taskallow="$(plutil -extract Entitlements.get-task-allow raw -o - "$decoded" 2>/dev/null || true)"
      all_devices="$(plutil -extract ProvisionsAllDevices raw -o - "$decoded" 2>/dev/null || true)"
      [ "$taskallow" = "true" ] && continue          # Development- oder Ad-hoc-Profil
      [ "$all_devices" = "true" ] && continue        # Enterprise-Profil
      uuid="$(plutil -extract UUID raw -o - "$decoded" 2>/dev/null || true)"
      name="$(plutil -extract Name raw -o - "$decoded" 2>/dev/null || true)"
      case "$appid" in
        "*.$BUNDLE_ID") exact="$uuid|$name";;
        *) wildcard="$wildcard$uuid|$name;";;
      esac
    done
  done
  if [ -n "$exact" ]; then printf '%s' "$exact"; return 0; fi
  [ -n "$wildcard" ] && { printf '%s' "${wildcard%;}"; return 0; }
  return 1
}

detect_signing() {
  if [ -n "${CODESIGN_KEY:-}" ]; then
    SIGNING_KEY="$CODESIGN_KEY"
  else
    SIGNING_KEY="$(security find-identity -v -p codesigning 2>/dev/null \
      | sed -n 's/.*"\(Apple Distribution[^"]*\)".*/\1/p' | head -1 || true)"
  fi

  if [ -n "${CODESIGN_PROVISION:-}" ]; then
    profile_uuid="$CODESIGN_PROVISION"   # Name oder UUID – beides akzeptiert msbuild
    profile_name="manuell gesetzt"
    return
  fi
  local found
  if found="$(find_distribution_profile)"; then
    profile_uuid="${found%%|*}"
    profile_name="${found##*|}"
  fi
}

# ------------------------------------------------- Build-Artefakte + Bundle-Prüfung --
# Ein inkrementeller iOS-Build nach einem Wechsel von SDK, Workload oder iOS-Paket
# kann eine Microsoft.iOS.dll in das Bundle kopieren, deren Runtime-Hash nicht zu
# dem des Executables passt. Zur Laufzeit findet die ObjC-Brücke dann keine
# Managed-Tokens mehr und die App stürzt direkt beim Start ab:
#   "The static registrar map for Microsoft.iOS (...) is invalid" ->
#   ArgumentNullException ('obj') in ObjCRuntime.Class.ResolveTokenReference.
clean_ios_outputs() {
  local path removed=0
  while IFS= read -r path; do
    rm -rf "$path"
    removed=$((removed + 1))
  done < <(find "$REPO_ROOT/Penban" -type d \
             \( -path "*/obj/Release/$IOS_TFM" -o -path "*/bin/Release/$IOS_TFM" \) -prune -print)
  echo "Veraltete Build-Artefakte entfernt: $removed Verzeichnis(se)"
}

# 40-stellige Hex-Hashes (u. a. der Runtime-Hash des Registrar-Map) eines Binaries.
binary_runtime_hashes() {
  strings -a "$1" 2>/dev/null | grep -oE '\b[0-9a-f]{40}\b' | sort -u || true
}

# Bundle nur freigeben, wenn Executable und Microsoft.iOS.dll denselben Runtime-Hash
# tragen – sonst startet die App auf dem Gerät nicht.
verify_bundle_runtime_hash() { # <pfad/zur/App.app>
  local app="$1" exe dll exe_hashes dll_hashes hash
  exe="$(plutil -extract CFBundleExecutable raw -o - "$app/Info.plist" 2>/dev/null || true)"
  exe="$app/${exe:-$(basename "$app" .app)}"
  dll="$app/Microsoft.iOS.dll"

  if [ ! -f "$exe" ] || [ ! -f "$dll" ]; then
    warn "Bundle-Prüfung übersprungen: $app enthält kein Executable oder keine Microsoft.iOS.dll."
    return 0
  fi

  exe_hashes="$(binary_runtime_hashes "$exe")"
  dll_hashes="$(binary_runtime_hashes "$dll")"
  if [ -z "$dll_hashes" ]; then
    warn "Bundle-Prüfung übersprungen: in Microsoft.iOS.dll wurde kein Runtime-Hash gefunden."
    return 0
  fi

  while IFS= read -r hash; do
    [ -n "$hash" ] || continue
    if printf '%s\n' "$exe_hashes" | grep -qx "$hash"; then
      echo "Runtime-Hash OK: ${hash:0:8} (Executable und Microsoft.iOS.dll stimmen überein)"
      return 0
    fi
  done <<<"$dll_hashes"

  die "Bundle ist inkonsistent: Microsoft.iOS.dll und das Executable passen nicht zusammen.
    Microsoft.iOS.dll: $(printf '%s ' $dll_hashes)
    Executable:        $(printf '%s ' $exe_hashes)
    Ursache: gemischte Build-Artefakte (Runtime-Hash des Registrar-Map weicht ab);
    eine so signierte App stürzt direkt beim Start ab.
    Lösung: '$0 clean' ausführen und erneut bauen."
}

# ------------------------------------------------------------------- Kommandos --
cmd_info() {
  select_build_dir
  detect_signing
  local xcode
  xcode="$(xcodebuild -version 2>/dev/null | sed -n 1p || true)"
  printf '\nProjekt:          %s\n' "$PROJ"
  printf 'Bundle-ID:        %s\n' "$BUNDLE_ID"
  printf 'Version:          %s (%s)\n' "$VERSION" "$BUILD"
  printf 'Build-SDK:        %s\n' "$SDK_PINNED"
  printf 'Build-Verzeichnis: %s\n' "$BUILD_DIR"
  printf 'Xcode:            %s\n' "${xcode:-nicht gefunden}"
  printf 'Signierung:       %s\n' "${SIGNING_KEY:-noch kein Apple-Distribution-Zertifikat gefunden}"
  if [ -n "$profile_uuid" ]; then
    printf 'App-Store-Profil: %s (%s)\n' "$profile_name" "$profile_uuid"
  else
    printf 'App-Store-Profil: noch kein Distribution-Profil gefunden (nur Development vorhanden)\n'
  fi
  if [ -n "${ASC_API_KEY:-}" ]; then
    printf 'Upload-Auth:      API-Key %s\n' "$ASC_API_KEY"
  elif [ -n "${ASC_USERNAME:-}" ]; then
    printf 'Upload-Auth:      Apple-ID %s\n' "$ASC_USERNAME"
  else
    printf 'Upload-Auth:      kein API-Key/Login in der Umgebung gesetzt\n'
  fi
}

cmd_build() {
  select_build_dir
  detect_signing

  [ -n "${SIGNING_KEY:-}" ] || die 'Kein "Apple Distribution"-Zertifikat im Schlüsselbund gefunden.
    Xcode → Settings → Accounts → Team NTMYS336K2 → Manage Certificates → + → Apple Distribution'
  [ -n "$profile_uuid" ] || die "Kein App-Store-Provisioning-Profil gefunden.
    developer.apple.com → Certificates, Identifiers & Profiles → Profiles → + → App Store → App-ID $BUNDLE_ID"

  log "Bereinige alte iOS-Build-Artefakte (obj/bin/$IOS_TFM)"
  clean_ios_outputs

  log "Baue Release-IPA: $BUNDLE_ID $VERSION ($BUILD)"
  echo "Signierung: ${SIGNING_KEY}"
  echo "Profil:     ${profile_name} (${profile_uuid})"
  echo "SDK:        ${SDK_PINNED}"

  local -a args=(publish "$PROJ" -f "$IOS_TFM" -c Release
    -p:RuntimeIdentifier="$RID"
    -p:EnableWindowsTargeting=true
    -p:BuildIpa=true
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

  IPA_PATH="$(find "$REPO_ROOT/Penban/Penban.Maui/bin/Release/$IOS_TFM/$RID" -name '*.ipa' -print -quit 2>/dev/null || true)"
  [ -n "$IPA_PATH" ] || die "Build erfolgreich, aber keine .ipa unter bin/Release/$IOS_TFM/$RID gefunden."
  log "IPA erstellt: $IPA_PATH"

  APP_PATH="${IPA_PATH%.ipa}.app"
  if [ ! -d "$APP_PATH" ]; then
    APP_PATH="$(find "$REPO_ROOT/Penban/Penban.Maui/bin/Release/$IOS_TFM/$RID" \
      -maxdepth 3 -type d -name '*.app' -print -quit 2>/dev/null || true)"
  fi
  [ -n "$APP_PATH" ] && [ -d "$APP_PATH" ] || die "Kein .app-Bundle zum Prüfen gefunden (IPA: $IPA_PATH)."

  log "Prüfe Bundle auf konsistente Runtime-Hashes"
  verify_bundle_runtime_hash "$APP_PATH"

  printf 'Nächster Schritt: %s upload "%s"\n' "$0" "$IPA_PATH"
}

cmd_upload() {
  local ipa="${1:-}"
  [ -n "$ipa" ] || die "Aufruf: $0 upload <pfad/zur/app.ipa>"
  [ -f "$ipa" ] || die "Datei nicht gefunden: $ipa"

  local -a auth=()
  if [ -n "${ASC_API_KEY:-}" ] && [ -n "${ASC_API_ISSUER:-}" ]; then
    auth=(--api-key "$ASC_API_KEY" --api-issuer "$ASC_API_ISSUER")
    [ -n "${ASC_P8_FILE:-}" ] && auth+=(--p8-file-path "$ASC_P8_FILE")
  elif [ -n "${ASC_USERNAME:-}" ] && [ -n "${ASC_PASSWORD:-}" ]; then
    auth=(-u "$ASC_USERNAME" -p "$ASC_PASSWORD")
  else
    die "Keine App-Store-Connect-Zugangsdaten gesetzt.
    API-Key:  ASC_API_KEY=<Key-ID> ASC_API_ISSUER=<Issuer-ID> [ASC_P8_FILE=<pfad/AuthKey_x.p8>]
              Key erstellen: App Store Connect → Users and Access → Integrations → App Store Connect API
    Apple-ID: ASC_USERNAME=<apple-id> ASC_PASSWORD=<app-spezifisches-passwort>"
  fi

  log "Lade zu App Store Connect hoch: $ipa"
  xcrun altool --upload-app -f "$ipa" -t ios "${auth[@]}"
  log "Upload abgeschlossen. Der Build erscheint nach der Verarbeitung in App Store Connect → TestFlight."
}

case "${1:-info}" in
  info)            cmd_info;;
  build)           cmd_build;;
  clean)           clean_ios_outputs;;
  upload)          shift; cmd_upload "${1:-}";;
  all)             cmd_build && cmd_upload "$IPA_PATH";;
  -h|--help|help)  sed -n '2,33p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//';;
  *)               die "Unbekanntes Kommando '$1' (info | build | upload <ipa> | clean | all)";;
esac
