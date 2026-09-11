#!/bin/sh
# ============================================================
#  AI2P - i18n/loc.sh  (T-65-S0)
#  Message catalogue of the build and install scripts for POSIX sh.
#  Source it and call L:
#       . "$DIR/i18n/loc.sh"
#       ai2p_set_lang "$LANG_OPT"        # may be empty - see the ladder below
#       L scr.inst.20 "$TARGET_DIR"
#
#  POSIX sh ONLY: all the scripts here start with #!/bin/sh (dash on Ubuntu), so
#  there are no associative arrays, no ${!var} and no gettext $"..." - the keys
#  become plain variables M_<key with dots replaced by underscores>.
#
#  LANGUAGE LADDER, the first non-empty wins:
#      1) the --lang xx switch of the script;
#      2) the AI2P_LANG environment variable;
#      3) en - THE DEFAULT AND THE BASE LANGUAGE (T-65-S0).
#  The "language" of config.json is DELIBERATELY NOT asked: the language of the
#  application interface and the language of an installation console are different
#  things, and the scripts answer in English on any machine until they are told
#  otherwise. Whoever wants another one says so: --lang or AI2P_LANG.
#  A key missing from the chosen catalogue is taken from scripts.en.txt (the EM_
#  prefix), and a key missing from both prints as the key itself.
#
#  THIS FILE IS ASCII ONLY, and printing goes through printf, not echo: the
#  behaviour of echo with escape sequences differs between shells.
# ============================================================

AI2P_LOC_DIR="${AI2P_LOC_DIR:-}"
AI2P_LOC_LANG="${AI2P_LOC_LANG:-}"

ai2p_loc_varname() {
    printf '%s' "$1" | tr -c 'A-Za-z0-9' '_'
}

ai2p_loc_load() {   # $1 = catalogue file, $2 = variable prefix
    [ -f "$1" ] || return 0
    while IFS= read -r __line || [ -n "$__line" ]; do
        case "$__line" in
            ''|'#'*) continue ;;
        esac
        __key="${__line%%=*}"
        if [ "$__key" = "$__line" ]; then continue; fi
        __val="${__line#*=}"
        eval "$2$(ai2p_loc_varname "$__key")=\$__val"
    done < "$1"
}

ai2p_set_lang() {
    __lang="${1:-}"
    [ -n "$__lang" ] || __lang="${AI2P_LANG:-}"
    [ -n "$__lang" ] || __lang="en"
    AI2P_LOC_LANG="$(printf '%s' "$__lang" | tr 'A-Z' 'a-z')"
    ai2p_loc_load "$AI2P_LOC_DIR/scripts.en.txt" "EM_"
    if [ "$AI2P_LOC_LANG" != "en" ]; then
        ai2p_loc_load "$AI2P_LOC_DIR/scripts.$AI2P_LOC_LANG.txt" "M_"
    fi
}

# text of a key with {0}, {1}, ... replaced by the arguments; no trailing newline
ai2p_text() {
    __key="$1"
    __k="$(ai2p_loc_varname "$1")"
    shift
    eval "__t=\${M_$__k:-}"
    [ -n "$__t" ] || eval "__t=\${EM_$__k:-}"
    if [ -z "$__t" ]; then printf '%s' "$__key"; return 0; fi
    __i=0
    for __a in "$@"; do
        while :; do
            case "$__t" in
                *"{$__i}"*)
                    __pre="${__t%%\{$__i\}*}"
                    __post="${__t#*\{$__i\}}"
                    __t="$__pre$__a$__post" ;;
                *) break ;;
            esac
        done
        __i=$((__i + 1))
    done
    printf '%s' "$__t"
}

L() {
    ai2p_text "$@"
    printf '\n'
}

# --help of a script: a WHOLE FILE i18n/help/<script>.<lang>.txt, not per-line keys
ai2p_help() {
    __f="$AI2P_LOC_DIR/help/$1.$AI2P_LOC_LANG.txt"
    [ -f "$__f" ] || __f="$AI2P_LOC_DIR/help/$1.en.txt"
    if [ -f "$__f" ]; then cat "$__f"; else printf 'No help file for %s\n' "$1"; fi
}
