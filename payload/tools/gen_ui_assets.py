"""
Génère payload/src/ui_assets.h : polices (Inter, licence OFL, tools/fonts), logo OXYN et icônes du menu,
pré-rendus en masques d'opacité 4 bits compressés (RLE). À relancer après un changement de police, de
taille ou de jeu de caractères :  python payload/tools/gen_ui_assets.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
FONT = ROOT / "fonts" / "Inter.ttf"
OUT = ROOT.parent / "src" / "ui_assets.h"

ASCII = "".join(chr(c) for c in range(32, 127))
ACCENTS = "àâäçéèêëîïôöùûüÿÀÂÇÉÈÊÎÔÙÛœ«»…’−·"
CHARSET = ASCII + ACCENTS

# Textes affichés en grand : seuls leurs caractères sont gardés (taille du payload).
TITLES = ["En attente du PC", "Connecté au PC", "Erreur USB", "Éteindre", "Redémarrer", "Retour à hekate",
          "Fermer le menu", "Menu"]
TITLE_CHARS = "".join(sorted(set("".join(TITLES)) | {" ", "…", "."}))

# nom C, taille en px, graisse, caractères
FONTS = [
    ("font_body", 19, 400, CHARSET),       # sous-titres, aides, batterie
    ("font_title", 28, 600, TITLE_CHARS),  # états et titres des boutons du menu
    ("font_mark", 62, 600, "OXYN"),        # nom OXYN sous le logo
]


def rle4(mask: Image.Image) -> bytes:
    """
    Opacité sur 4 bits (0-15), ligne après ligne :
      0x00-0x3F : (n+1) pixels transparents      0x40-0x7F : (n+1) pixels opaques
      0x80-0xFF : (bits 4-6 + 1) pixels d'opacité (bits 0-3)
    """
    px = [round(v * 15 / 255) for v in mask.get_flattened_data()]
    out = bytearray()
    i = 0
    while i < len(px):
        v = px[i]
        limit = 64 if v in (0, 15) else 8
        n = 1
        while i + n < len(px) and px[i + n] == v and n < limit:
            n += 1
        if v == 0:
            out.append(n - 1)
        elif v == 15:
            out.append(0x40 | (n - 1))
        else:
            out.append(0x80 | ((n - 1) << 4) | v)
        i += n
    return bytes(out)


def load_font(size, weight):
    f = ImageFont.truetype(str(FONT), size)
    f.set_variation_by_axes([min(max(size, 14), 32), weight])
    return f


def gen_font(name, size, weight, chars, data):
    f = load_font(size, weight)
    ascent, descent = f.getmetrics()
    glyphs = []
    for ch in chars:
        adv = round(f.getlength(ch))
        l, t, r, b = f.getbbox(ch, anchor="ls")
        w, h = max(0, r - l), max(0, b - t)
        off = len(data)
        if w and h:
            m = Image.new("L", (w, h), 0)
            ImageDraw.Draw(m).text((-l, -t), ch, font=f, fill=255, anchor="ls")
            data += rle4(m)
        glyphs.append((ord(ch), w, h, l, t, adv, off))
    lines = [f"static const ui_glyph_t {name}_glyphs[] = {{"]
    for cp, w, h, l, t, adv, off in glyphs:
        lines.append(f"\t{{ 0x{cp:04X}, {w}, {h}, {l}, {t}, {adv}, {off} }},")
    lines.append("};")
    lines.append(f"static const ui_font_t {name} = {{ {name}_glyphs, {len(glyphs)}, {ascent}, {descent} }};")
    return "\n".join(lines)


def supersampled(size, draw_fn, scale=4):
    big = Image.new("L", (size * scale, size * scale), 0)
    draw_fn(ImageDraw.Draw(big), size * scale)
    return big.resize((size, size), Image.LANCZOS)


def logo_layers(size):
    """Trois couches du logo : carré sombre, anneau + 3 touches claires, touche du haut grise."""
    s4 = size * 4

    def square(d, s):
        d.rounded_rectangle([0, 0, s - 1, s - 1], radius=s * 0.22, fill=255)

    def diamond(d, s, cx, cy, k):
        tile = Image.new("L", (int(k * 2.2), int(k * 2.2)), 0)
        c = k * 1.1
        ImageDraw.Draw(tile).rounded_rectangle([c - k * 0.575, c - k * 0.575, c + k * 0.575, c + k * 0.575],
                                               radius=k * 0.28, fill=255)
        tile = tile.rotate(45, resample=Image.BICUBIC)
        d._image.paste(255, (int(cx - c), int(cy - c)), tile)

    def light(d, s):
        rr = s * 0.39
        w = max(2, int(s * 0.024))
        d.ellipse([s / 2 - rr, s / 2 - rr, s / 2 + rr, s / 2 + rr], outline=255, width=w)
        k, off = s * 0.165, s * 0.165
        for dx, dy in [(-1, 0), (1, 0), (0, 1)]:
            diamond(d, s, s / 2 + dx * off, s / 2 + dy * off, k)

    def gray(d, s):
        k, off = s * 0.165, s * 0.165
        diamond(d, s, s / 2, s / 2 - off, k)

    return [supersampled(size, fn) for fn in (square, light, gray)]


def icons(size):
    """Icônes du menu, traits fins : éteindre, redémarrer, retour, fermer."""
    def power(d, s):
        w = int(s * 0.075)
        m = s * 0.2
        d.arc([m, m, s - m, s - m], start=-60, end=240, fill=255, width=w)
        d.line([s / 2, s * 0.12, s / 2, s * 0.5], fill=255, width=w)

    def restart(d, s):
        w = int(s * 0.075)
        m = s * 0.2
        d.arc([m, m, s - m, s - m], start=-150, end=150, fill=255, width=w)
        # pointe de flèche au bout de l'arc (en haut à gauche)
        x, y = s * 0.24, s * 0.28
        d.polygon([(x - s * 0.13, y - s * 0.02), (x + s * 0.06, y - s * 0.1), (x + s * 0.03, y + s * 0.11)], fill=255)

    def back(d, s):
        w = int(s * 0.075)
        d.line([s * 0.2, s / 2, s * 0.82, s / 2], fill=255, width=w)
        d.line([s * 0.2 + w * 0.3, s / 2, s * 0.46, s * 0.24], fill=255, width=w)
        d.line([s * 0.2 + w * 0.3, s / 2, s * 0.46, s * 0.76], fill=255, width=w)

    def close(d, s):
        w = int(s * 0.075)
        d.line([s * 0.24, s * 0.24, s * 0.76, s * 0.76], fill=255, width=w)
        d.line([s * 0.76, s * 0.24, s * 0.24, s * 0.76], fill=255, width=w)

    return [supersampled(size, fn) for fn in (power, restart, back, close)]


def gen_masks(name, masks, data):
    lines = [f"static const ui_mask_t {name}[] = {{"]
    for m in masks:
        off = len(data)
        data += rle4(m)
        lines.append(f"\t{{ {m.width}, {m.height}, {off} }},")
    lines.append("};")
    return "\n".join(lines)


def main():
    data = bytearray()
    parts = [gen_font(*spec, data) for spec in FONTS]
    parts.append(gen_masks("ui_logo_masks", logo_layers(180), data))
    parts.append(gen_masks("ui_icons", icons(36), data))

    body = ",".join(str(b) for b in data)
    header = f"""/*
 * Généré par payload/tools/gen_ui_assets.py : ne pas modifier à la main.
 * Police Inter (c) The Inter Project Authors, SIL Open Font License 1.1 (payload/tools/fonts/OFL.txt).
 * Masques d'opacité 4 bits compressés (voir rle4() dans le script et _mask() dans ui.c).
 */
#ifndef _UI_ASSETS_H_
#define _UI_ASSETS_H_

#include "ui.h"

static const u8 ui_data[{len(data)}] = {{{body}}};

{chr(10).join(parts)}

// Identifiants des icônes et taille du logo : voir ui.h.

#endif
"""
    OUT.write_text(header, encoding="utf-8", newline="\n")
    print(f"{OUT.name} : {len(data)} octets de données")


if __name__ == "__main__":
    main()
