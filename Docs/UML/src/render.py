import re, sys, html, math, importlib
from PIL import ImageFont

sys.path.insert(0, __file__.rsplit('/', 1)[0])
import model
importlib.reload(model)
from model import CLASSES, ORDER, PACKAGES

FONT_DIR = "/usr/share/fonts/truetype/liberation/"
F_REG = ImageFont.truetype(FONT_DIR + "LiberationSans-Regular.ttf", 12)
F_BOLD13 = ImageFont.truetype(FONT_DIR + "LiberationSans-Bold.ttf", 13)
F_BOLDI13 = ImageFont.truetype(FONT_DIR + "LiberationSans-BoldItalic.ttf", 13)
F_BOLD11 = ImageFont.truetype(FONT_DIR + "LiberationSans-Bold.ttf", 11)
F_THAI = ImageFont.truetype("/usr/share/fonts/opentype/tlwg/Loma.otf", 13)

FAMILY = "'Liberation Sans', Arial, Helvetica, Loma, sans-serif"
BG = "#141414"
INK = "#F2F2F2"
LINE = "#E3E3E3"
MUTED = "#A9A9A9"
LABEL = "#C4C4C4"

LH = 16          # member line height
PADX = 9


def tw(text, font=F_REG):
    # Thai glyphs measured with Loma, everything else with Liberation Sans.
    w = 0.0
    run, thai = "", None
    for ch in text + "\0":
        is_thai = "฀" <= ch <= "๿"
        if ch == "\0" or (thai is not None and is_thai != thai):
            if run:
                w += (F_THAI if thai else font).getlength(run)
            run = ""
        if ch != "\0":
            run += ch
            thai = is_thai
    return w


def esc(t):
    return html.escape(t, quote=False)


# ---------------------------------------------------------------- geometry
def member_text(m):
    static = m.startswith("_")
    t = m[1:] if static else m
    abstract = t.endswith("{abstract}")
    return t, static, abstract


def box_size(c):
    lines = []
    head = []
    if c["stereo"]:
        head.append(("<<%s>>" % c["stereo"], F_BOLD11))
    head.append((c["id"], F_BOLDI13 if c["abstract"] else F_BOLD13))
    if c["abstract"]:
        head.append(("{abstract}", F_REG))
    hw = max(tw(t, f) for t, f in head)
    body = c["enum"] if c["enum"] else c["attrs"] + c["ops"]
    bw = max([tw(member_text(m)[0]) for m in body] or [0])
    w = max(hw + 2 * PADX + 10, bw + 2 * PADX, 150)
    w = int(math.ceil(w / 10.0) * 10)
    hh = 7 + (13 if c["stereo"] else 0) + 16 + (14 if c["abstract"] else 0) + 6
    def comp(n):
        return 6 + n * LH + 3 if n else 10
    if c["enum"]:
        h = hh + comp(len(c["enum"]))
    else:
        h = hh + comp(len(c["attrs"])) + comp(len(c["ops"]))
    return w, h, hh


for c in CLASSES.values():
    c["w"], c["h"], c["hh"] = box_size(c)

import layout
importlib.reload(layout)
from layout import POS, EDGES, NOTES, CANVAS, LEGEND_AT, TITLE_AT

for cid, (x, y) in POS.items():
    CLASSES[cid]["x"], CLASSES[cid]["y"] = x, y
missing = [c for c in ORDER if c not in POS]
assert not missing, "no position for %s" % missing


def rect(c):
    return c["x"], c["y"], c["x"] + c["w"], c["y"] + c["h"]


# ---------------------------------------------------------------- edges
def anchor(cid, spec):
    c = CLASSES[cid]
    side, pos = spec
    x0, y0, x1, y1 = rect(c)
    if side in "tb":
        x = x0 + pos * (x1 - x0) if isinstance(pos, float) else pos
        return (x, y0 if side == "t" else y1)
    y = y0 + pos * (y1 - y0) if isinstance(pos, float) else pos
    return (x0 if side == "l" else x1, y)


def route(e):
    A = anchor(e["a"], e.get("sa", ("b", 0.5)))
    B = anchor(e["b"], e.get("sb", ("t", 0.5)))
    sa, sb = e.get("sa", ("b", 0.5))[0], e.get("sb", ("t", 0.5))[0]
    pts = [A]
    for v in e.get("via", []):
        px, py = pts[-1]
        if v[0] == "x":
            pts.append((v[1], py))
        elif v[0] == "y":
            pts.append((px, v[1]))
        else:
            pts.append(v)
    px, py = pts[-1]
    if "via" not in e:
        if sa in "tb" and sb in "tb":
            if abs(px - B[0]) > 0.5:
                mid = e.get("mid", (py + B[1]) / 2)
                pts += [(px, mid), (B[0], mid)]
        elif sa in "lr" and sb in "lr":
            if abs(py - B[1]) > 0.5:
                mid = e.get("mid", (px + B[0]) / 2)
                pts += [(mid, py), (mid, B[1])]
        elif sa in "tb":
            pts.append((px, B[1]))
        else:
            pts.append((B[0], py))
    else:
        if abs(px - B[0]) > 0.5 and abs(py - B[1]) > 0.5:
            pts.append((B[0], py) if sb in "tb" else (px, B[1]))
    pts.append(B)
    # drop duplicate points
    out = [pts[0]]
    for p in pts[1:]:
        if abs(p[0] - out[-1][0]) > 0.01 or abs(p[1] - out[-1][1]) > 0.01:
            out.append(p)
    return out


def unit(p, q):
    dx, dy = q[0] - p[0], q[1] - p[1]
    d = math.hypot(dx, dy) or 1
    return dx / d, dy / d


def fmt(v):
    return ("%.1f" % v).rstrip("0").rstrip(".")


def hop_path(pts, hops):
    # pts: polyline; hops: dict seg_index -> sorted list of x where a vertical line crosses
    d = "M%s,%s" % (fmt(pts[0][0]), fmt(pts[0][1]))
    for i in range(1, len(pts)):
        p, q = pts[i - 1], pts[i]
        xs = hops.get(i - 1, [])
        if xs and abs(p[1] - q[1]) < 0.5:
            sgn = 1 if q[0] > p[0] else -1
            for x in sorted(xs, key=lambda v: sgn * v):
                d += " L%s,%s" % (fmt(x - sgn * 5), fmt(p[1]))
                d += " a5,5 0 0 %d %s,0" % (1 if sgn > 0 else 0, fmt(sgn * 10))
        d += " L%s,%s" % (fmt(q[0]), fmt(q[1]))
    return d


EDGE_STYLE = {
    "assoc": dict(dash=False), "dep": dict(dash=True), "comp": dict(dash=False),
    "aggr": dict(dash=False), "inh": dict(dash=False), "real": dict(dash=True),
}


def build_edges():
    built = []
    for e in EDGES:
        pts = route(e)
        k = e["k"]
        draw = list(pts)
        marks = []
        if getattr(layout, "SIMPLE_LINES", False):
            # solid lines only; hollow triangle = inherits / implements, filled arrow = everything else
            ux, uy = unit(pts[-2], pts[-1])
            is_parent = k in ("inh", "real")
            if not e.get("shared_tip"):
                marks.append(("tri" if is_parent else "classic", pts[-1], (ux, uy)))
            back = 14 if is_parent else 8
            draw[-1] = (pts[-1][0] - ux * back, pts[-1][1] - uy * back)
            built.append(dict(e=e, pts=pts, draw=draw, marks=marks))
            continue
        if k in ("inh", "real"):
            ux, uy = unit(pts[-2], pts[-1])
            tip = pts[-1]
            base = (tip[0] - ux * 14, tip[1] - uy * 14)
            if not e.get("shared_tip"):
                marks.append(("tri", tip, (ux, uy)))
            draw[-1] = base
        elif k in ("assoc", "dep"):
            ux, uy = unit(pts[-2], pts[-1])
            marks.append(("open", pts[-1], (ux, uy)))
        if k in ("comp", "aggr"):
            ux, uy = unit(pts[1], pts[0])
            tip = pts[0]
            marks.append(("dia", tip, (ux, uy), k == "comp"))
            draw[0] = (tip[0] - ux * 18, tip[1] - uy * 18)
        built.append(dict(e=e, pts=pts, draw=draw, marks=marks))
    return built


# ---------------------------------------------------------------- checks
BUILTIN = set("""void bool int float string ulong Vector3 Vector2 Quaternion Pose Rigidbody Transform
Collider Collision RaycastHit FixedJoint ConfigurableJoint GameObject Sprite Action NetworkVariable
NetworkList List IEnumerable Task NavMeshAgent NetworkObjectReference""".split())
VALUE_KINDS = ("Enumeration", "Struct")


def member_types(m):
    t, _, _ = member_text(m)
    t = re.sub(r"\{[^}]*\}", "", t)
    if "(" in t:
        params = t[t.index("(") + 1:t.rindex(")")]
        ret = t[t.rindex(")") + 1:]
        types = [p.split(":", 1)[1] for p in params.split(",") if ":" in p]
        types.append(ret.split(":", 1)[1] if ":" in ret else "")
    else:
        types = [t.split(":", 1)[1]] if ":" in t else []
    return [tok for ty in types for tok in re.findall(r"[A-Za-z_]\w*", ty)]


def attr_refs(c):
    refs = set()
    for m in c["attrs"]:
        for tok in member_types(m):
            if tok in CLASSES and tok != c["id"]:
                refs.add(tok)
    return refs


def op_refs(c):
    refs = set()
    for m in c["ops"]:
        for tok in member_types(m):
            if tok in CLASSES:
                refs.add(tok)
    return refs


def run_checks(built):
    problems = []
    for c in CLASSES.values():
        for m in c["attrs"] + c["ops"]:
            for tok in member_types(m):
                if tok not in CLASSES and tok not in BUILTIN:
                    problems.append("undefined type %s in %s" % (tok, c["id"]))
    structural = {}
    for b in built:
        e = b["e"]
        structural.setdefault((e["a"], e["b"]), []).append(e["k"])
    for b in built:
        e = b["e"]
        a, t, k = CLASSES[e["a"]], CLASSES[e["b"]], e["k"]
        if k == "assoc" and e["b"] not in attr_refs(a):
            problems.append("association %s -> %s but %s has no such field" % (a["id"], t["id"], a["id"]))
        if k in ("comp", "aggr") and e["b"] not in attr_refs(a):
            problems.append("%s %s -> %s: whole has no field" % (k, a["id"], t["id"]))
        if k == "dep":
            if e["b"] in attr_refs(a):
                problems.append("dependency %s ..> %s but it is a field (should be association)" % (a["id"], t["id"]))
        if k == "real":
            want = {member_text(m)[0].split("(")[0][2:] for m in t["ops"]}
            have = {member_text(m)[0].split("(")[0][2:] for m in a["ops"]}
            if not want <= have:
                problems.append("%s realizes %s but lacks %s" % (a["id"], t["id"], sorted(want - have)))
    for c in CLASSES.values():
        for r in attr_refs(c):
            if CLASSES[r]["stereo"] in VALUE_KINDS:
                continue
            ks = structural.get((c["id"], r), []) + [k for k in structural.get((r, c["id"]), []) if k in ("comp", "aggr")]
            if not any(k in ("assoc", "comp", "aggr") for k in ks):
                problems.append("field in %s of type %s has no association line" % (c["id"], r))
    # overlaps
    ids = list(CLASSES)
    for i in range(len(ids)):
        for j in range(i + 1, len(ids)):
            a, b = rect(CLASSES[ids[i]]), rect(CLASSES[ids[j]])
            if a[0] < b[2] + 20 and b[0] < a[2] + 20 and a[1] < b[3] + 20 and b[1] < a[3] + 20:
                problems.append("boxes too close/overlap: %s %s" % (ids[i], ids[j]))
    # segments through boxes
    for b in built:
        e = b["e"]
        pts = b["pts"]
        for i in range(1, len(pts)):
            (x1, y1), (x2, y2) = pts[i - 1], pts[i]
            lo_x, hi_x, lo_y, hi_y = min(x1, x2), max(x1, x2), min(y1, y2), max(y1, y2)
            for cid, c in CLASSES.items():
                bx0, by0, bx1, by1 = rect(c)
                if cid in (e["a"], e["b"]):
                    bx0, by0, bx1, by1 = bx0 + 2, by0 + 2, bx1 - 2, by1 - 2
                if lo_x < bx1 and hi_x > bx0 and lo_y < by1 and hi_y > by0:
                    problems.append("edge %s->%s passes through %s" % (e["a"], e["b"], cid))
    frames = package_frames()
    for p, (x0, y0, x1, y1) in frames.items():
        name, sub = PACKAGES[p]
        tx0, ty0, tx1, ty1 = x0 + 12, y0 + 8, x0 + 20 + tw(sub, F_THAI), y0 + 30
        for b in built:
            pts = b["pts"]
            for i in range(1, len(pts)):
                (ax, ay), (bx, by) = pts[i - 1], pts[i]
                if min(ax, bx) < tx1 and max(ax, bx) > tx0 and min(ay, by) < ty1 and max(ay, by) > ty0:
                    problems.append("edge %s->%s crosses subtitle of %s" % (b["e"]["a"], b["e"]["b"], p))
    return problems


# ---------------------------------------------------------------- svg
def svg_text(x, y, t, size=12, weight=None, anchor=None, italic=False, underline=False, fill=INK):
    a = ' x="%s" y="%s"' % (fmt(x), fmt(y))
    a += ' font-size="%s"' % size
    if weight:
        a += ' font-weight="%s"' % weight
    if anchor:
        a += ' text-anchor="%s"' % anchor
    if italic:
        a += ' font-style="italic"'
    if underline:
        a += ' text-decoration="underline"'
    if fill != INK:
        a += ' fill="%s"' % fill
    return "<text%s>%s</text>" % (a, esc(t))


def draw_box(c, out):
    x, y, w, h, hh = c["x"], c["y"], c["w"], c["h"], c["hh"]
    out.append('<rect x="%d" y="%d" width="%d" height="%d" fill="%s" stroke="%s" stroke-width="1.2"/>' % (x, y, w, h, BOX_FILL, LINE))
    cy = y + 7
    cx = x + w / 2
    if c["stereo"]:
        out.append(svg_text(cx, cy + 10, "<<%s>>" % c["stereo"], 11, "700", "middle", fill=STEREO))
        cy += 13
    out.append(svg_text(cx, cy + 13, c["id"], 13, "700", "middle", italic=c["abstract"]))
    cy += 16
    if c["abstract"]:
        out.append(svg_text(cx, cy + 11, "{abstract}", 11, None, "middle", fill=MUTED))
        cy += 14
    yy = y + hh
    comps = [c["enum"]] if c["enum"] else [c["attrs"], c["ops"]]
    for comp in comps:
        out.append('<line x1="%d" y1="%s" x2="%d" y2="%s" stroke="%s"/>' % (x, fmt(yy), x + w, fmt(yy), LINE))
        if not comp:
            yy += 10
            continue
        ty = yy + 6
        for m in comp:
            t, static, abstract = member_text(m)
            out.append(svg_text(x + PADX, ty + 12, t, 12, italic=abstract, underline=static))
            ty += LH
        yy = ty + 3


def draw_marker(m, out, dash):
    kind, tip, (ux, uy) = m[0], m[1], m[2]
    px, py = -uy, ux
    if kind == "classic":
        b = (tip[0] - ux * 12, tip[1] - uy * 12)
        notch = (tip[0] - ux * 8, tip[1] - uy * 8)
        p1 = (b[0] + px * 5, b[1] + py * 5)
        p2 = (b[0] - px * 5, b[1] - py * 5)
        out.append('<polygon points="%s,%s %s,%s %s,%s %s,%s" fill="%s" stroke="%s" stroke-width="1"/>' % (
            fmt(tip[0]), fmt(tip[1]), fmt(p1[0]), fmt(p1[1]), fmt(notch[0]), fmt(notch[1]), fmt(p2[0]), fmt(p2[1]), LINE, LINE))
    elif kind == "tri":
        base = (tip[0] - ux * 14, tip[1] - uy * 14)
        p1 = (base[0] + px * 8, base[1] + py * 8)
        p2 = (base[0] - px * 8, base[1] - py * 8)
        out.append('<polygon points="%s,%s %s,%s %s,%s" fill="%s" stroke="%s" stroke-width="1.2"/>' % (
            fmt(tip[0]), fmt(tip[1]), fmt(p1[0]), fmt(p1[1]), fmt(p2[0]), fmt(p2[1]), BG_EDGE, LINE))
    elif kind == "open":
        b = (tip[0] - ux * 11, tip[1] - uy * 11)
        p1 = (b[0] + px * 6, b[1] + py * 6)
        p2 = (b[0] - px * 6, b[1] - py * 6)
        out.append('<polyline points="%s,%s %s,%s %s,%s" fill="none" stroke="%s" stroke-width="1.2"/>' % (
            fmt(p1[0]), fmt(p1[1]), fmt(tip[0]), fmt(tip[1]), fmt(p2[0]), fmt(p2[1]), LINE))
    elif kind == "dia":
        filled = m[3]
        mid = (tip[0] - ux * 9, tip[1] - uy * 9)
        end = (tip[0] - ux * 18, tip[1] - uy * 18)
        p1 = (mid[0] + px * 6, mid[1] + py * 6)
        p2 = (mid[0] - px * 6, mid[1] - py * 6)
        out.append('<polygon points="%s,%s %s,%s %s,%s %s,%s" fill="%s" stroke="%s" stroke-width="1.2"/>' % (
            fmt(tip[0]), fmt(tip[1]), fmt(p1[0]), fmt(p1[1]), fmt(end[0]), fmt(end[1]), fmt(p2[0]), fmt(p2[1]),
            LINE if filled else BG_EDGE, LINE))


def label_box(x, y, t, out, size=11, fill=LABEL, anchor="middle"):
    w = tw(t, F_REG) * size / 12 + 8
    x0 = x - w / 2 if anchor == "middle" else (x - 4 if anchor == "start" else x - w + 4)
    out.append('<rect x="%s" y="%s" width="%s" height="15" fill="%s"/>' % (fmt(x0), fmt(y - 11), fmt(w), LABEL_BG))
    out.append(svg_text(x, y, t, size, anchor=anchor, fill=fill))


def package_frames():
    frames = {}
    for cid, c in CLASSES.items():
        x0, y0, x1, y1 = rect(c)
        f = frames.setdefault(c["pkg"], [x0, y0, x1, y1])
        f[0], f[1], f[2], f[3] = min(f[0], x0), min(f[1], y0), max(f[2], x1), max(f[3], y1)
    for n in NOTES:
        f = frames[n["pkg"]]
        f[0], f[1] = min(f[0], n["x"]), min(f[1], n["y"])
        f[2], f[3] = max(f[2], n["x"] + n["w"]), max(f[3], n["y"] + 14 + 18 * len(n["lines"]))
    for p, f in frames.items():
        pad = layout.PKG_PAD.get(p, (28, 46, 28, 28))
        f[0] -= pad[0]; f[1] -= pad[1]; f[2] += pad[2]; f[3] += pad[3]
    return frames


BOX_FILL = "#161616"
BG_EDGE = "#161616"
STEREO = "#F2F2F2"
LABEL_BG = "#111111"
PKG_FILL = "#111111"


def render(path):
    built = build_edges()
    problems = run_checks(built)
    W, H = CANVAS
    out = []
    out.append('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 %d %d" width="%d" height="%d" font-family="%s" fill="%s">' % (W, H, W, H, FAMILY, INK))
    out.append('<rect width="100%%" height="100%%" fill="%s"/>' % "#0C0C0C")
    # title
    tx, ty = TITLE_AT
    out.append(svg_text(tx, ty, layout.TITLE, 30, "700"))
    out.append(svg_text(tx, ty + 30, layout.SUBTITLE, 15, fill=MUTED))
    # packages
    frames = package_frames()
    for p, (x0, y0, x1, y1) in frames.items():
        name, sub = PACKAGES[p]
        tabw = tw(name, F_BOLD13) * 14 / 13 + 28
        out.append('<rect x="%d" y="%d" width="%d" height="%d" fill="%s" stroke="#4A4A4A" stroke-width="1.2"/>' % (x0, y0 - 26, tabw, 26, "#1A1A1A"))
        out.append('<rect x="%d" y="%d" width="%d" height="%d" fill="%s" stroke="#4A4A4A" stroke-width="1.2"/>' % (x0, y0, x1 - x0, y1 - y0, PKG_FILL))
        out.append(svg_text(x0 + 14, y0 - 8, name, 14, "700"))
        out.append(svg_text(x0 + 16, y0 + 24, sub, 13, fill=MUTED))
    # hops: horizontal segments hop over vertical segments of other edges
    verticals = []
    for i, b in enumerate(built):
        d = b["draw"]
        for j in range(1, len(d)):
            (x1, y1), (x2, y2) = d[j - 1], d[j]
            if abs(x1 - x2) < 0.5:
                verticals.append((i, x1, min(y1, y2), max(y1, y2)))
    for i, b in enumerate(built):
        d = b["draw"]
        hops = {}
        for j in range(1, len(d)):
            (x1, y1), (x2, y2) = d[j - 1], d[j]
            if abs(y1 - y2) < 0.5:
                lo, hi = min(x1, x2), max(x1, x2)
                for (k, vx, vy0, vy1) in verticals:
                    if k == i:
                        continue
                    same = built[k]["e"].get("group") and built[k]["e"].get("group") == b["e"].get("group")
                    if not same and lo + 7 < vx < hi - 7 and vy0 + 1 < y1 < vy1 - 1:
                        hops.setdefault(j - 1, []).append(vx)
        b["hops"] = hops
    # edges
    for b in built:
        dash = ' stroke-dasharray="7 5"' if EDGE_STYLE[b["e"]["k"]]["dash"] and not getattr(layout, "SIMPLE_LINES", False) else ""
        out.append('<path d="%s" fill="none" stroke="%s" stroke-width="1.2"%s/>' % (hop_path(b["draw"], b["hops"]), LINE, dash))
    for b in built:
        for m in b["marks"]:
            draw_marker(m, out, False)
    # notes
    for n in NOTES:
        x, y, w, lines = n["x"], n["y"], n["w"], n["lines"]
        h = 14 + 18 * len(lines)
        out.append('<path d="M%d,%d H%d L%d,%d V%d H%d Z" fill="#1E1E1E" stroke="#8C8C8C"/>' % (x, y, x + w - 12, x + w, y + 12, y + h, x))
        out.append('<path d="M%d,%d V%d H%d" fill="none" stroke="#8C8C8C"/>' % (x + w - 12, y, y + 12, x + w))
        for i, t in enumerate(lines):
            out.append(svg_text(x + 10, y + 22 + 18 * i, t, 12.5, fill="#D4D4D4"))
        out.append('<polyline points="%s" fill="none" stroke="#8C8C8C" stroke-dasharray="4 4"/>' % " ".join("%s,%s" % (fmt(a), fmt(b_)) for a, b_ in n["conn"]))
    # boxes
    for cid in ORDER:
        draw_box(CLASSES[cid], out)
    # labels and multiplicities
    for b in (built if getattr(layout, "SHOW_LINE_TEXT", True) else []):
        e = b["e"]
        if e.get("label"):
            lx, ly = e["lpos"]
            label_box(lx, ly, e["label"], out, anchor=e.get("lanchor", "middle"))
        for key, end in (("ma", 0), ("mb", -1)):
            if e.get(key):
                t, dx, dy = e[key]
                ex, ey = b["pts"][end]
                out.append(svg_text(ex + dx, ey + dy, t, 12, "700", "middle", fill=LABEL))
    # legend (optional)
    if LEGEND_AT:
        draw_legend(out)
    out.append("</svg>")
    open(path, "w").write("\n".join(out))
    return problems


def draw_legend(out):
    x, y = LEGEND_AT
    w, h = layout.LEGEND_SIZE
    out.append('<rect x="%d" y="%d" width="%d" height="%d" fill="#161616" stroke="#6A6A6A" stroke-width="1.2"/>' % (x, y, w, h))
    out.append(svg_text(x + 18, y + 28, "สัญลักษณ์ในแผนภาพ", 15, "700"))
    rows = [
        ("inh", "Inheritance", "สืบทอดคลาส"),
        ("real", "Realization", "implement อินเทอร์เฟซ"),
        ("assoc", "Association", "ถือ reference ไว้เป็น field"),
        ("dep", "Dependency", "ใช้ชั่วคราว: พารามิเตอร์ ค่าที่คืน static"),
        ("comp", "Composition", "เป็นเจ้าของ หายไปพร้อมเจ้าของ"),
        ("aggr", "Aggregation", "รวมไว้ แต่ละชิ้นมีอายุของตัวเอง"),
    ]
    yy = y + 58
    for k, en, th in rows:
        x0, x1 = x + 20, x + 100
        dash = ' stroke-dasharray="7 5"' if k in ("dep", "real") else ""
        if k in ("comp", "aggr"):
            out.append('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="%s" stroke-width="1.2"/>' % (x0 + 18, yy, x1, yy, LINE))
            draw_marker(("dia", (x0, yy), (-1, 0), k == "comp"), out, False)
        elif k in ("inh", "real"):
            out.append('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="%s" stroke-width="1.2"%s/>' % (x0, yy, x1 - 14, yy, LINE, dash))
            draw_marker(("tri", (x1, yy), (1, 0)), out, False)
        else:
            out.append('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="%s" stroke-width="1.2"%s/>' % (x0, yy, x1, yy, LINE, dash))
            draw_marker(("open", (x1, yy), (1, 0)), out, False)
        out.append(svg_text(x + 116, yy + 4, en, 13, "700"))
        out.append(svg_text(x + 212, yy + 4, th, 13, fill="#D0D0D0"))
        yy += 36
    cx = x + 520
    out.append('<line x1="%d" y1="%d" x2="%d" y2="%d" stroke="#3A3A3A"/>' % (cx - 20, y + 44, cx - 20, y + h - 16))
    right = [
        ("+  -  #", "public / private / protected", {}),
        ("ขีดเส้นใต้", "สมาชิกแบบ static", {"underline": True}),
        ("ตัวเอียง", "abstract ต้อง override ในคลาสลูก", {"italic": True}),
        ("{override} {event}", "override คลาสแม่ / C# event", {}),
        ("ไม่มี stereotype", "MonoBehaviour", {}),
        ("<<NetworkBehaviour>>", "คอมโพเนนต์ที่ sync ผ่านเน็ต (NGO)", {}),
        ("<<ScriptableObject>>", "ไฟล์ค่าตั้งเก็บใน Assets", {}),
        ("<<Plain C#>>", "คลาส C# ธรรมดา เทสได้ไม่ต้องเปิด Unity", {}),
        ("<<Static>>  <<Struct>>", "static class / ชนิดข้อมูลแบบค่า", {}),
    ]
    for i, (a, b, st) in enumerate(right):
        ty = y + 62 + i * 24
        out.append(svg_text(cx, ty, a, 12.5, "700", **st))
        out.append(svg_text(cx + 180, ty, b, 13, fill="#D0D0D0"))


if __name__ == "__main__":
    probs = render(sys.argv[1] if len(sys.argv) > 1 else "classdiagram.svg")
    for p in probs:
        print("!", p)
    print("problems:", len(probs))
