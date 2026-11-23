/*
 * SwitchDeck - petit moteur de dessin des écrans du payload. Tout est en entiers (le BPMP n'a pas de FPU) ;
 * les bords arrondis sont lissés par sous-échantillonnage 4x4 des seuls pixels de bord.
 */

#include <string.h>

#include "screen.h"
#include "ui.h"
#include "ui_assets.h"

#define SUB 4 // Sous-échantillons par axe pour le lissage des bords.

static const ui_font_t *const fonts[] = { &font_body, &font_title, &font_mark };

static inline u32 _mix(u32 dst, u32 color, u32 a) // a : 0-256
{
	u32 r = ((dst >> 16) & 0xFF) * (256 - a) + ((color >> 16) & 0xFF) * a;
	u32 g = ((dst >> 8) & 0xFF) * (256 - a) + ((color >> 8) & 0xFF) * a;
	u32 b = (dst & 0xFF) * (256 - a) + (color & 0xFF) * a;
	return 0xFF000000 | ((r >> 8) << 16) | ((g >> 8) << 8) | (b >> 8);
}

static inline void _blend(int x, int y, u32 color, u32 a)
{
	if (!a || x < 0 || y < 0 || x >= SCREEN_W || y >= SCREEN_H)
		return;
	u32 *p = screen_fb() + y * SCREEN_W + x;
	*p = a >= 256 ? color : _mix(*p, color, a);
}

// Fond : dégradé vertical sombre + halo bleu doux centré derrière le logo.
#define BG_TOP_R 14
#define BG_TOP_G 17
#define BG_TOP_B 22
#define BG_BOT_R 22
#define BG_BOT_G 27
#define BG_BOT_B 34
#define GLOW_X 640
#define GLOW_Y 250
#define GLOW_R 640

u32 ui_bg_color(u32 x, u32 y)
{
	u32 r = BG_TOP_R + (BG_BOT_R - BG_TOP_R) * y / SCREEN_H;
	u32 g = BG_TOP_G + (BG_BOT_G - BG_TOP_G) * y / SCREEN_H;
	u32 b = BG_TOP_B + (BG_BOT_B - BG_TOP_B) * y / SCREEN_H;

	int dx = (int)x - GLOW_X, dy = ((int)y - GLOW_Y) * 5 / 4;
	u32 d2 = dx * dx + dy * dy;
	if (d2 < GLOW_R * GLOW_R)
	{
		u32 t = 256 - d2 / (GLOW_R * GLOW_R / 256); // 256 au centre, 0 au bord
		u32 k = t * t >> 8;
		r += 20 * k >> 8;
		g += 32 * k >> 8;
		b += 56 * k >> 8;
	}
	return 0xFF000000 | (r << 16) | (g << 8) | b;
}

void ui_restore(int x, int y, int w, int h)
{
	for (int j = MAX(y, 0); j < MIN(y + h, SCREEN_H); j++)
	{
		u32 *line = screen_fb() + j * SCREEN_W;
		for (int i = MAX(x, 0); i < MIN(x + w, SCREEN_W); i++)
			line[i] = ui_bg_color(i, j);
	}
}

void ui_background()
{
	ui_restore(0, 0, SCREEN_W, SCREEN_H);
}

// Couverture (0-256) d'un pixel de coin : part de ses sous-échantillons dans le disque de centre (cx, cy), en 1/SUB px.
static u32 _corner_cover(int px, int py, int cx, int cy, int r)
{
	u32 in = 0;
	int r2 = r * r * SUB * SUB;
	for (int sy = 0; sy < SUB; sy++)
		for (int sx = 0; sx < SUB; sx++)
		{
			int dx = (px * SUB + sx) * 2 + 1 - cx * SUB * 2;
			int dy = (py * SUB + sy) * 2 + 1 - cy * SUB * 2;
			if (dx * dx + dy * dy <= r2 * 4)
				in++;
		}
	return in * 256 / (SUB * SUB);
}

void ui_rrect(int x, int y, int w, int h, int r, u32 color)
{
	r = MIN(r, MIN(w, h) / 2);
	for (int j = 0; j < h; j++)
		for (int i = 0; i < w; i++)
		{
			int cx = -1, cy = -1;
			if (i < r)          cx = x + r;
			else if (i >= w - r) cx = x + w - r;
			if (j < r)          cy = y + r;
			else if (j >= h - r) cy = y + h - r;

			u32 a = 256;
			if (cx >= 0 && cy >= 0)
				a = _corner_cover(x + i, y + j, cx, cy, r);
			_blend(x + i, y + j, color, a);
		}
}

void ui_circle(int cx, int cy, int r, u32 color)
{
	int in2 = (r - 1) * (r - 1), out2 = (r + 1) * (r + 1);
	for (int y = cy - r - 1; y <= cy + r; y++)
		for (int x = cx - r - 1; x <= cx + r; x++)
		{
			int dx = x - cx, dy = y - cy;
			int d2 = dx * dx + dy * dy;
			if (d2 >= out2)
				continue;
			_blend(x, y, color, d2 <= in2 ? 256 : _corner_cover(x, y, cx, cy, r));
		}
}

// Masque 4 bits compressé (voir gen_ui_assets.py) posé en (x, y).
static void _mask(int x, int y, u32 w, u32 h, u32 off, u32 color)
{
	const u8 *p = ui_data + off;
	u32 n = 0, total = w * h;
	while (n < total)
	{
		u8 b = *p++;
		u32 count, alpha;
		if (b < 0x40)      { count = b + 1;                 alpha = 0;  }
		else if (b < 0x80) { count = (b & 0x3F) + 1;        alpha = 15; }
		else               { count = ((b >> 4) & 7) + 1;    alpha = b & 0xF; }

		if (!alpha)
		{
			n += count;
			continue;
		}
		u32 a = alpha * 256 / 15;
		while (count-- && n < total)
		{
			_blend(x + (int)(n % w), y + (int)(n / w), color, a);
			n++;
		}
	}
}

void ui_logo(int cx, int cy)
{
	static const u32 colors[] = { 0xFF1B1C1F, 0xFFE6E7EB, 0xFF7A7D86 };
	int x = cx - UI_LOGO_SIZE / 2, y = cy - UI_LOGO_SIZE / 2;
	for (u32 i = 0; i < ARRAY_SIZE(colors); i++)
		_mask(x, y, ui_logo_masks[i].w, ui_logo_masks[i].h, ui_logo_masks[i].off, colors[i]);
}

void ui_icon(int cx, int cy, u32 id, u32 color)
{
	const ui_mask_t *m = &ui_icons[id];
	_mask(cx - m->w / 2, cy - m->h / 2, m->w, m->h, m->off, color);
}

// Décode un caractère UTF-8 et avance le pointeur.
static u32 _utf8(const char **s)
{
	const u8 *p = (const u8 *)*s;
	u32 c = *p++;
	if (c >= 0xE0 && p[0] && p[1])
	{
		c = ((c & 0x0F) << 12) | ((p[0] & 0x3F) << 6) | (p[1] & 0x3F);
		p += 2;
	}
	else if (c >= 0xC0 && p[0])
	{
		c = ((c & 0x1F) << 6) | (p[0] & 0x3F);
		p += 1;
	}
	*s = (const char *)p;
	return c;
}

static const ui_glyph_t *_glyph(const ui_font_t *f, u32 cp)
{
	for (u32 i = 0; i < f->count; i++)
		if (f->glyphs[i].cp == cp)
			return &f->glyphs[i];
	// Caractère absent de la police : point d'interrogation s'il existe, sinon rien.
	return cp != '?' ? _glyph(f, '?') : NULL;
}

int ui_text_width(ui_font_id_t font, const char *s, int spacing)
{
	const ui_font_t *f = fonts[font];
	int w = 0;
	bool first = true;
	while (*s)
	{
		const ui_glyph_t *g = _glyph(f, _utf8(&s));
		if (!g)
			continue;
		w += (first ? 0 : spacing) + g->adv;
		first = false;
	}
	return w;
}

int ui_text(int x, int baseline, ui_font_id_t font, const char *s, u32 color, int spacing)
{
	const ui_font_t *f = fonts[font];
	int start = x;
	while (*s)
	{
		const ui_glyph_t *g = _glyph(f, _utf8(&s));
		if (!g)
			continue;
		if (g->w && g->h)
			_mask(x + g->x, baseline + g->y, g->w, g->h, g->off, color);
		x += g->adv + spacing;
	}
	return x - start;
}

void ui_text_center(int cx, int baseline, ui_font_id_t font, const char *s, u32 color, int spacing)
{
	ui_text(cx - ui_text_width(font, s, spacing) / 2, baseline, font, s, color, spacing);
}

void ui_text_right(int right, int baseline, ui_font_id_t font, const char *s, u32 color, int spacing)
{
	ui_text(right - ui_text_width(font, s, spacing), baseline, font, s, color, spacing);
}
