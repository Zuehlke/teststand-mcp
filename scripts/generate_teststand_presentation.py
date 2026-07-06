#!/usr/bin/env python3
"""Generate a modern, interactive HTML presentation for a TestStand sequence file.

Usage:
    py generate_teststand_presentation.py <data.json> <output.html>
        [--shots-dir <dir>] [--teststand-dir <dir>] [--template <html>]

Produces ONE self-contained .html (dark glassmorphism theme, Setup/Main/Cleanup
phase cards, clickable subsequences with a detail overlay, and a "Code & Flowchart"
compare view). Everything is embedded as base64 — the original TestStand step icons
(full color, taken from the local installation) — so the file can be shared as-is.

The right ("editor") pane of the compare view shows a rendered TestStand-style step
listing (real icons, flow indentation, Setup/Main/Cleanup groups) built from the data.
NI TestStand 2026's Sequence Editor renders its UI with embedded Chromium (CEF), which
is opaque to Windows UI automation, so per-sequence screenshots cannot be captured
reliably/automatically — the rendered listing is the default and always works.
Optionally, if you pass --shots-dir pointing at a folder with a _manifest.json
(mapping "<SequenceName>" -> "<file.png>", e.g. captured manually), those screenshots
are embedded and shown instead of the listing for the matching sequences.

Requirements: Pillow (for the icons; without it the nodes render without icon images).
The TestStand installation is auto-discovered under
"Program Files*\\National Instruments\\TestStand*" (newest wins); override with
--teststand-dir or the TS_DOC_TESTSTAND_DIR environment variable.

--------------------------------------------------------------------------------
data.json contract (UTF-8) — produced by the teststand-presentation-generator agent
--------------------------------------------------------------------------------
{
  "title": "TFW_ExampleModule",           # header title (usually file name w/o ext)
  "language": "de",                        # "de" or "en"
  "generated": "2026-07-02",
  "main_sequence": "MainSequence",         # which sequence drives the Setup/Main/Cleanup cards
  "file": { "path": "C:\\..\\X.seq", "description": "...", "version": "1.0.0.0" },
  "sequences": [
    { "name": "MainSequence", "description": "...", "enabled": true,
      "steps": {
        "Setup":   [ { "name": "Init", "type": "SequenceCall", "target": "Init_HW",
                       "detail": "→ Init_HW", "enabled": true } ],
        "Main":    [ { "name": "While_x", "type": "NI_Flow_While", "detail": "Locals.i < 3" },
                     { "name": "Do", "type": "SequenceCall", "target": "Measure" },
                     { "name": "End", "type": "NI_Flow_End" },
                     { "name": "Open", "type": "SequenceCall",
                       "ext_file": "Driver.seq", "ext_sequence": "Open" },
                     { "name": "Later", "type": "SequenceCall", "unresolved": true } ],
        "Cleanup": [] } }
  ]
}

Per step:
  name (str), type (raw TestStand step type — drives icon + node kind), enabled (bool),
  detail (optional extra text / flow condition), and for SequenceCall exactly one of:
    target        -> internal sub name (node becomes clickable)
    ext_file/ext_sequence -> external call (teal "external" tag, clickable to an ext leaf)
    unresolved:true       -> unlinked placeholder
"""
import argparse
import base64
import io
import json
import os
import sys

try:
    from PIL import Image
except ImportError:  # without Pillow the icons are omitted (nodes still render)
    Image = None

sys.setrecursionlimit(5000)

# --------------------------------------------------------------------------
# Reused from generate_teststand_doc.py: real TestStand icon files + finder
# --------------------------------------------------------------------------
ICON_FILES = {
    "NI_Flow_If": r"Icons\FlowControl\NI_If.ico",
    "NI_Flow_ElseIf": r"Icons\FlowControl\NI_ElseIf.ico",
    "NI_Flow_Else": r"Icons\FlowControl\NI_Else.ico",
    "NI_Flow_End": r"Icons\FlowControl\NI_End.ico",
    "NI_Flow_While": r"Icons\FlowControl\NI_While.ico",
    "NI_Flow_DoWhile": r"Icons\FlowControl\NI_DoWhile.ico",
    "NI_Flow_For": r"Icons\FlowControl\NI_For.ico",
    "NI_Flow_ForEach": r"Icons\FlowControl\NI_ForEach.ico",
    "NI_Flow_SweepLoop": r"Icons\FlowControl\NI_Sweep.ico",
    "NI_Flow_StreamLoop": r"Icons\FlowControl\NI_StreamLoop.ico",
    "NI_Flow_Select": r"Icons\FlowControl\NI_Select.ico",
    "NI_Flow_Case": r"Icons\FlowControl\NI_Case.ico",
    "NI_Flow_Break": r"Icons\FlowControl\NI_Break.ico",
    "NI_Flow_Continue": r"Icons\FlowControl\NI_Continue.ico",
    "SequenceCall": r"Icons\SeqAdp.ico",
    "Statement": r"Icons\statement.ico",
    "NI_Wait": r"StepTypes\SyncSteps\res\wait.ico",
    "MessagePopup": r"Icons\MsgBox.ico",
    "CallExecutable": r"Icons\execstep.ico",
    "Action": r"Icons\NoneAdp_action.ico",
    "PassFailTest": r"Icons\NoneAdp_test.ico",
    "NumericLimitTest": r"Icons\NoneAdp_test.ico",
    "StringValueTest": r"Icons\NoneAdp_test.ico",
    "NI_MultipleNumericLimitTest": r"Icons\NoneAdp_test.ico",
    "Goto": r"Icons\goto.ico",
    "Label": r"Icons\label.ico",
}
DEFAULT_ICON = r"Icons\Generic.ico"


def find_teststand_components(override=None):
    """Locate <TestStand>\\Components (icons live below it). Newest install wins."""
    import glob as _glob
    for cand in (override, os.environ.get("TS_DOC_TESTSTAND_DIR")):
        if not cand:
            continue
        c = cand if os.path.basename(cand).lower() == "components" \
            else os.path.join(cand, "Components")
        if os.path.isdir(c):
            return c
    hits = []
    for root in (r"C:\Program Files", r"C:\Program Files (x86)"):
        hits += _glob.glob(os.path.join(root, "National Instruments",
                                        "TestStand*", "Components"))
    return sorted(hits)[-1] if hits else None


# --------------------------------------------------------------------------
# Step-type classification (kind -> color/tag) and flow-control sets
# --------------------------------------------------------------------------
LOOP_OPENERS = {"NI_Flow_While", "NI_Flow_DoWhile", "NI_Flow_For", "NI_Flow_ForEach",
                "NI_Flow_SweepLoop", "NI_Flow_StreamLoop"}
FLOW_OPENERS = LOOP_OPENERS | {"NI_Flow_If", "NI_Flow_Select", "NI_Flow_Case"}
FLOW_MID = {"NI_Flow_ElseIf", "NI_Flow_Else"}
FLOW_END = {"NI_Flow_End"}

TYPE_DISPLAY = {
    "NI_Flow_If": "If", "NI_Flow_ElseIf": "Else If", "NI_Flow_Else": "Else",
    "NI_Flow_End": "End", "NI_Flow_While": "While", "NI_Flow_DoWhile": "Do While",
    "NI_Flow_For": "For", "NI_Flow_ForEach": "For Each",
    "NI_Flow_SweepLoop": "Sweep Loop", "NI_Flow_StreamLoop": "Stream Loop",
    "NI_Flow_Select": "Select", "NI_Flow_Case": "Case",
    "NI_Flow_Break": "Break", "NI_Flow_Continue": "Continue",
    "NI_Wait": "Wait", "SequenceCall": "Sequence Call",
    "MessagePopup": "Message Popup", "CallExecutable": "Call Executable",
    "PassFailTest": "Pass/Fail Test", "NumericLimitTest": "Numeric Limit Test",
    "StringValueTest": "String Value Test",
    "NI_MultipleNumericLimitTest": "Multiple Numeric Limit Test",
    "Statement": "Statement", "Action": "Action",
}


def type_display(t):
    return TYPE_DISPLAY.get(t, t)


def classify(t):
    if t == "SequenceCall":
        return "call"
    if t == "PassFailTest":
        return "test"
    if t in ("NumericLimitTest", "NI_MultipleNumericLimitTest", "StringValueTest"):
        return "numtest"
    if t == "MessagePopup":
        return "popup"
    if t == "NI_Wait":
        return "wait"
    if t in ("NI_Flow_If", "NI_Flow_ElseIf", "NI_Flow_Else", "NI_Flow_Select", "NI_Flow_Case"):
        return "dec"
    if t in FLOW_END:
        return "end"
    if t == "Action":
        return "act"
    return "stmt"


# TYPE meta (kind -> {c: color, tag}); tags localized per language below.
TYPE_COLORS = {
    "call": "#818cf8", "test": "#34d399", "numtest": "#10d29a", "popup": "#fbbf24",
    "wait": "#c084fc", "dec": "#f472b6", "stmt": "#94a3b8", "act": "#38bdf8",
    "end": "#7c8aa6", "ext": "#2dd4bf",
}


# --------------------------------------------------------------------------
# Labels (de / en)
# --------------------------------------------------------------------------
def build_labels(lang, meta):
    de = {
        "brand": "Zühlke", "headerTitle": "TestStand MCP",
        "headerSub": f"Prüfsequenz · interaktives Sequenz-Flowchart",
        "chipSequence": "Sequenz", "chipSource": "Quelle",
        "hideDisabled": "deaktivierte ausblenden", "autoDemo": "Auto-Demo",
        "legend": "Legende", "open": "öffnen", "openSub": "Subsequenz öffnen",
        "active": "aktiv", "inactive": "deaktiviert",
        "loop": "Schleife", "retry": "Retry bei Fail → Schleifenanfang",
        "skipped": "übersprungen", "emptyGroup": "keine Schritte", "noSteps": "keine Schritte",
        "ifTrue": "wahr", "elseLabel": "sonst", "elseIf": "sonst wenn",
        "mainSeqCrumb": "Hauptsequenz", "subsequence": "Subsequenz",
        "externalFile": "Externe Datei", "externalSeq": "Externe Sequenz",
        "notLinked": "(nicht verlinkt)",
        "stepsShort": "Schritte", "code": "Code", "codeAndFlow": "Code & Flowchart",
        "compareTitle": "Flowchart und generierten TestStand-Code dieser Phase nebeneinander anzeigen",
        "compareSub": "Flowchart und TestStand Sequence Editor",
        "phase": "Phase", "back": "Zurück",
        "flowchart": "Flowchart", "editorHdr": "TestStand · Schrittansicht",
        "capScreenshot": "Screenshot direkt aus dem TestStand Sequence Editor",
        "capFallback": "Schrittansicht – automatisch aus der Sequenz erzeugt",
        "fallbackNote": "",
        "attractText": "Auto-Demo läuft · Maus bewegen, um selbst zu steuern",
        "tag_call": "SequenceCall", "tag_test": "Pass/Fail", "tag_numtest": "Limit-Test",
        "tag_popup": "MessagePopup", "tag_wait": "Wait", "tag_dec": "If / Select",
        "tag_stmt": "Statement", "tag_act": "Action", "tag_end": "End", "tag_ext": "Externe Sequenz",
        "footer": (f"Generiert mit dem <b>Zühlke TestStand MCP-Server</b> · Hauptsequenz "
                   f"<b>{meta['mainSeq']}</b> mit {meta['subCount']}&nbsp;Subsequenzen "
                   f"&amp; {meta['extCount']}&nbsp;externen Sequenz-Aufrufen · "
                   f"<span style='color:var(--ok)'>●</span> aktiv&nbsp;&nbsp;"
                   f"<span style='color:var(--off)'>◦</span>&nbsp;deaktiviert"),
    }
    en = {
        "brand": "Zühlke", "headerTitle": "TestStand MCP",
        "headerSub": f"Test sequence · interactive sequence flowchart",
        "chipSequence": "Sequence", "chipSource": "Source",
        "hideDisabled": "hide disabled", "autoDemo": "Auto-demo",
        "legend": "Legend", "open": "open", "openSub": "Open subsequence",
        "active": "active", "inactive": "disabled",
        "loop": "Loop", "retry": "Retry on fail → loop start",
        "skipped": "skipped", "emptyGroup": "no steps", "noSteps": "no steps",
        "ifTrue": "true", "elseLabel": "else", "elseIf": "else if",
        "mainSeqCrumb": "Main sequence", "subsequence": "Subsequence",
        "externalFile": "External file", "externalSeq": "External sequence",
        "notLinked": "(not linked)",
        "stepsShort": "steps", "code": "Code", "codeAndFlow": "Code & Flowchart",
        "compareTitle": "Show this phase's flowchart next to the generated TestStand code",
        "compareSub": "Flowchart and TestStand Sequence Editor",
        "phase": "Phase", "back": "Back",
        "flowchart": "Flowchart", "editorHdr": "TestStand · step view",
        "capScreenshot": "Screenshot taken directly from the TestStand Sequence Editor",
        "capFallback": "Step view rendered from the sequence",
        "fallbackNote": "",
        "attractText": "Auto-demo running · move the mouse to take control",
        "tag_call": "SequenceCall", "tag_test": "Pass/Fail", "tag_numtest": "Limit test",
        "tag_popup": "MessagePopup", "tag_wait": "Wait", "tag_dec": "If / Select",
        "tag_stmt": "Statement", "tag_act": "Action", "tag_end": "End", "tag_ext": "External sequence",
        "footer": (f"Generated with the <b>Zühlke TestStand MCP server</b> · main sequence "
                   f"<b>{meta['mainSeq']}</b> with {meta['subCount']}&nbsp;subsequences "
                   f"&amp; {meta['extCount']}&nbsp;external sequence calls · "
                   f"<span style='color:var(--ok)'>●</span> active&nbsp;&nbsp;"
                   f"<span style='color:var(--off)'>◦</span>&nbsp;disabled"),
    }
    return de if lang != "en" else en


# --------------------------------------------------------------------------
# Icons -> base64 data URIs (full color, real TestStand icons)
# --------------------------------------------------------------------------
class IconLoader:
    def __init__(self, components):
        self.components = components
        self.cache = {}
        self.ok = bool(components) and Image is not None

    def datauri(self, itype):
        if itype in self.cache:
            return self.cache[itype]
        data = None
        if self.ok:
            rel = ICON_FILES.get(itype, DEFAULT_ICON)
            src = os.path.join(self.components, rel)
            if not os.path.isfile(src):
                src = os.path.join(self.components, DEFAULT_ICON)
            if os.path.isfile(src):
                try:
                    im = Image.open(src)
                    # .ico files carry several sizes — pick the largest, then downscale crisply.
                    if getattr(im, "ico", None) is not None:
                        try:
                            biggest = max(im.ico.sizes())
                            im = im.ico.getimage(biggest)
                        except Exception:
                            pass
                    im = im.convert("RGBA").resize((40, 40), Image.LANCZOS)
                    buf = io.BytesIO()
                    im.save(buf, "PNG")
                    data = "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode("ascii")
                except Exception:
                    data = None
        self.cache[itype] = data
        return data


# --------------------------------------------------------------------------
# Build blocks (nested tree) from the flat step list
# --------------------------------------------------------------------------
def _type(s):
    return (s.get("type") or "").strip()


def _cond(s):
    return (s.get("detail") or s.get("condition") or "").strip()


class Builder:
    def __init__(self, sub_names, ext_registry, L):
        self.sub_names = sub_names          # set of internal (clickable) sequence names
        self.ext_registry = ext_registry    # key -> {file, seq}
        self.L = L
        self.itypes = set()                  # every icon type actually used

    # ---- leaf node ----
    def leaf(self, step):
        t = _type(step)
        self.itypes.add(t)
        kind = classify(t)
        node = {"kind": kind, "itype": t,
                "label": step.get("name") or type_display(t),
                "enabled": step.get("enabled", True) is not False}
        detail = (step.get("detail") or "").strip()
        if t == "SequenceCall":
            tgt = step.get("target")
            if tgt and tgt in self.sub_names:
                node["target"] = tgt
                node["sub"] = detail or ("→ " + tgt)
            elif step.get("ext_file") or step.get("ext_sequence"):
                f = step.get("ext_file") or ""
                seq = step.get("ext_sequence") or ""
                key = seq or (os.path.basename(f) if f else "external")
                self.ext_registry[key] = {"file": f, "seq": seq}
                node["target"] = key
                node["ext"] = True
                base = os.path.basename(f) if f else ""
                node["sub"] = "→ " + base + ((" → " + seq) if seq else "")
            elif step.get("unresolved"):
                node["sub"] = detail or self.L["notLinked"]
            elif detail:
                node["sub"] = detail
        elif detail:
            node["sub"] = detail
        return node

    def dec_node(self, step):
        t = _type(step)
        self.itypes.add(t)
        node = {"kind": "dec", "itype": t, "label": step.get("name") or type_display(t)}
        c = _cond(step)
        if c:
            node["sub"] = c
        return node

    def end_node(self, step):
        self.itypes.add("NI_Flow_End")
        label = (step.get("name") if step else None) or self.L.get("tag_end", "End")
        return {"kind": "end", "itype": "NI_Flow_End", "label": label}

    # ---- recursive descent ----
    def parse_body(self, steps, i):
        blocks = []
        n = len(steps)
        while i < n:
            t = _type(steps[i])
            if t in FLOW_END or t in FLOW_MID or t == "NI_Flow_Case":
                break
            if t == "NI_Flow_If":
                blk, i = self.parse_if(steps, i)
                blocks.append(blk)
            elif t in LOOP_OPENERS:
                blk, i = self.parse_loop(steps, i)
                blocks.append(blk)
            elif t == "NI_Flow_Select":
                blk, i = self.parse_select(steps, i)
                blocks.append(blk)
            else:
                blocks.append({"type": "node", "node": self.leaf(steps[i])})
                i += 1
        return blocks, i

    def parse_if(self, steps, i):
        op = steps[i]
        i += 1
        paths = []
        body, i = self.parse_body(steps, i)
        paths.append({"label": _cond(op) or self.L["ifTrue"], "blocks": body})
        while i < len(steps) and _type(steps[i]) in FLOW_MID:
            cl = steps[i]
            i += 1
            b, i = self.parse_body(steps, i)
            if _type(cl) == "NI_Flow_Else":
                lbl = self.L["elseLabel"]
            else:
                lbl = _cond(cl) or self.L["elseIf"]
            paths.append({"label": lbl, "blocks": b})
        merge = None
        if i < len(steps) and _type(steps[i]) in FLOW_END:
            merge = steps[i]
            i += 1
        if len(paths) == 1:  # lone If -> add an empty else lane for visual balance
            paths.append({"label": self.L["elseLabel"], "blocks": []})
        return {"type": "branch", "dec": self.dec_node(op), "paths": paths,
                "merge": self.end_node(merge)}, i

    def parse_loop(self, steps, i):
        op = steps[i]
        t = _type(op)
        self.itypes.add(t)
        i += 1
        body, i = self.parse_body(steps, i)
        if i < len(steps) and _type(steps[i]) in FLOW_END:
            i += 1
        return {"type": "loop", "itype": t,
                "label": op.get("name") or type_display(t),
                "cond": _cond(op), "blocks": body}, i

    def parse_select(self, steps, i):
        op = steps[i]
        i += 1
        paths = []
        while i < len(steps) and _type(steps[i]) == "NI_Flow_Case":
            case = steps[i]
            i += 1
            b, i = self.parse_body(steps, i)
            if i < len(steps) and _type(steps[i]) in FLOW_END:
                i += 1  # closes this case
            paths.append({"label": _cond(case) or case.get("name") or "Case", "blocks": b})
        merge = None
        if i < len(steps) and _type(steps[i]) in FLOW_END:
            merge = steps[i]
            i += 1  # closes the select
        if not paths:
            paths.append({"label": "Case", "blocks": []})
        return {"type": "branch", "dec": self.dec_node(op), "paths": paths,
                "merge": self.end_node(merge)}, i

    def parse(self, steps):
        blocks, _ = self.parse_body(steps or [], 0)
        return blocks


# --------------------------------------------------------------------------
# Flat step-listing (compare-view fallback), flow-aware indentation
# --------------------------------------------------------------------------
def flat_rows(steps, itypes):
    depth, rows = 0, []
    for st in steps or []:
        t = _type(st)
        itypes.add(t)
        if t in FLOW_END:
            depth = max(0, depth - 1)
            d = depth
        elif t in FLOW_MID:
            d = max(0, depth - 1)
        else:
            d = depth
        rows.append({"icon": t, "name": st.get("name") or type_display(t),
                     "tdisp": type_display(t), "depth": d,
                     "dis": st.get("enabled", True) is False})
        if t in FLOW_OPENERS:
            depth += 1
    return rows


# --------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("data_json")
    ap.add_argument("output_html")
    ap.add_argument("--shots-dir", default=None,
                    help="optional: folder with SeqEdit screenshots + _manifest.json "
                         "(usually omitted — the editor pane is then a rendered step listing)")
    ap.add_argument("--teststand-dir", default=None)
    ap.add_argument("--template", default=None)
    args = ap.parse_args()

    with open(args.data_json, "r", encoding="utf-8") as f:
        data = json.load(f)

    lang = data.get("language", "de")
    seqs = data.get("sequences", [])
    by_name = {s["name"]: s for s in seqs}
    main_name = data.get("main_sequence") or ("MainSequence" if "MainSequence" in by_name
                                              else (seqs[0]["name"] if seqs else ""))
    sub_seqs = [s for s in seqs if s["name"] != main_name]
    sub_names = {s["name"] for s in sub_seqs}

    # ---- meta / labels ----
    meta = {
        "title": data.get("title") or os.path.splitext(os.path.basename(
            data.get("file", {}).get("path", "sequence.seq")))[0],
        "source": os.path.basename(data.get("file", {}).get("path", "")) or (main_name + ".seq"),
        "mainSeq": main_name,
        "generated": data.get("generated", ""),
        "subCount": len(sub_seqs),
        "extCount": 0,  # filled after build
    }
    L = build_labels(lang, meta)

    ext_registry = {}
    builder = Builder(sub_names, ext_registry, L)

    def groups(seq):
        st = seq.get("steps", {}) or {}
        return st.get("Setup", []) or [], st.get("Main", []) or [], st.get("Cleanup", []) or []

    # ---- MAIN phases ----
    main_seq = by_name.get(main_name, {"steps": {}})
    s_setup, s_main, s_clean = groups(main_seq)
    MAIN = [
        {"name": "Setup", "title": "Setup", "accent": "blue", "blocks": builder.parse(s_setup)},
        {"name": "Main", "title": "Main", "accent": "violet", "blocks": builder.parse(s_main)},
        {"name": "Cleanup", "title": "Cleanup", "accent": "amber", "blocks": builder.parse(s_clean)},
    ]

    # ---- SUBS ----
    SUBS = {}
    LISTING = {}
    itypes = builder.itypes

    def concat_groups(seq):
        a, b, c = groups(seq)
        return list(a) + list(b) + list(c)

    for s in sub_seqs:
        SUBS[s["name"]] = {
            "title": s["name"],
            "subtitle": (s.get("description") or "").strip(),
            "enabled": s.get("enabled", True) is not False,
            "external": False,
            "body": builder.parse(concat_groups(s)),
        }
        # listing: keep group markers when more than one group carries steps
        a, b, c = groups(s)
        present = [(n, g) for n, g in (("Setup", a), ("Main", b), ("Cleanup", c)) if g]
        rows = []
        for gname, g in present:
            if len(present) > 1:
                rows.append({"group": gname})
            rows.extend(flat_rows(g, itypes))
        LISTING[s["name"]] = rows

    # main-sequence phase listings
    for key, g in (("phase:Setup", s_setup), ("phase:Main", s_main), ("phase:Cleanup", s_clean)):
        LISTING[key] = flat_rows(g, itypes)

    # ---- external targets as leaf subs (registered while parsing) ----
    for key, info in ext_registry.items():
        f = info.get("file") or ""
        SUBS[key] = {
            "title": key,
            "subtitle": ("@ " + os.path.basename(f)) if f else L["externalSeq"],
            "enabled": True,
            "external": True,
            "body": [{"type": "node", "node": {
                "kind": "ext", "itype": "SequenceCall", "ext": True,
                "label": L["externalSeq"],
                "sub": (os.path.basename(f) + ((" → " + info["seq"]) if info.get("seq") else "")) if f else info.get("seq", ""),
            }}],
        }
        itypes.add("SequenceCall")
    meta["extCount"] = len(ext_registry)
    L = build_labels(lang, meta)  # rebuild footer with real extCount

    # ---- optional screenshots (base64) from a capture manifest ----
    # Default flow omits --shots-dir: every editor pane is a rendered step listing.
    # If a shots dir with _manifest.json is supplied (e.g. manually captured), embed them.
    manifest = {}
    if args.shots_dir:
        mpath = os.path.join(args.shots_dir, "_manifest.json")
        if os.path.isfile(mpath):
            try:
                with open(mpath, "r", encoding="utf-8") as f:
                    manifest = json.load(f)
            except Exception:
                manifest = {}
    SHOTS = {}
    shot_count = 0
    shot_keys = [main_name] + [s["name"] for s in sub_seqs]
    for name in shot_keys:
        fn = manifest.get(name)
        uri = None
        if fn and args.shots_dir:
            p = os.path.join(args.shots_dir, fn)
            if os.path.isfile(p):
                with open(p, "rb") as fh:
                    uri = "data:image/png;base64," + base64.b64encode(fh.read()).decode("ascii")
                shot_count += 1
        SHOTS[name] = uri

    # ---- icons (base64) ----
    components = find_teststand_components(args.teststand_dir)
    icons = IconLoader(components)
    ICONS = {}
    itypes.add("NI_Flow_End")
    legend = [
        ["call", "SequenceCall", "SequenceCall"],
        ["test", "PassFailTest", "Pass/Fail-Test" if lang != "en" else "Pass/Fail test"],
        ["numtest", "NumericLimitTest", "Limit-Test" if lang != "en" else "Limit test"],
        ["popup", "MessagePopup", "MessagePopup"],
        ["wait", "NI_Wait", "Wait"],
        ["dec", "NI_Flow_If", "If / Select"],
        ["stmt", "Statement", "Statement"],
        ["act", "Action", "Action"],
        ["ext", "SequenceCall", L["externalSeq"]],
    ]
    for _, it, _lbl in legend:
        itypes.add(it)
    icon_count = 0
    for it in sorted(itypes):
        uri = icons.datauri(it)
        if uri:
            ICONS[it] = uri
            icon_count += 1
    ICONS["__default__"] = icons.datauri("__default__") or ICONS.get("Statement")

    # ---- TYPE meta (localized tags) ----
    TYPE = {k: {"c": TYPE_COLORS[k], "tag": L.get("tag_" + k, k)} for k in TYPE_COLORS}

    DATA = {
        "meta": meta, "types": TYPE, "icons": ICONS, "legend": legend,
        "main": MAIN, "subs": SUBS, "shots": SHOTS, "listing": LISTING,
    }

    # ---- inject into template ----
    tpl_path = args.template or os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                             "presentation_template.html")
    with open(tpl_path, "r", encoding="utf-8") as f:
        tpl = f.read()

    def js_json(obj):
        # safe to embed inside <script>: neutralize any "</" that could close the tag
        return json.dumps(obj, ensure_ascii=False).replace("</", "<\\/")

    payload = "const DATA = " + js_json(DATA) + ";\nconst LBL = " + js_json(L) + ";"
    if "/*%%DATA%%*/" not in tpl:
        print("[!!] template has no /*%%DATA%%*/ placeholder", file=sys.stderr)
        sys.exit(2)
    html = tpl.replace("/*%%DATA%%*/", payload)

    os.makedirs(os.path.dirname(os.path.abspath(args.output_html)) or ".", exist_ok=True)
    with open(args.output_html, "w", encoding="utf-8") as f:
        f.write(html)

    size = os.path.getsize(args.output_html)
    listings = len(shot_keys) - shot_count
    print(f"[ok] html        {args.output_html} ({size/1024:.0f} KB, self-contained)")
    print(f"[ok] sequences   main='{main_name}' + {len(sub_seqs)} subs, {len(ext_registry)} external targets")
    print(f"[ok] icons       {icon_count} real TestStand icons embedded"
          + ("" if icons.ok else "  [!] no TestStand/Pillow -> icons omitted"))
    print(f"[ok] editor view {shot_count} embedded screenshots, {listings} rendered step-listings")
    if not components:
        print("[--] icons: no TestStand Components folder found (pass --teststand-dir)")


if __name__ == "__main__":
    main()
