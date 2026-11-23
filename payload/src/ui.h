/*
 * SwitchDeck - petit moteur de dessin des écrans du payload (attente, connecté, menu) :
 * fond dégradé, rectangles arrondis et cercles lissés, texte Inter et masques pré-rendus (ui_assets.h).
 */

#ifndef _UI_H_
#define _UI_H_

#include <utils/types.h>

typedef struct _ui_glyph_t
{
	u16 cp;     // Point de code Unicode.
	u16 w, h;   // Taille du masque.
	s16 x, y;   // Décalage du masque depuis le point de départ sur la ligne de base.
	u16 adv;    // Avance horizontale.
	u32 off;    // Position des données dans ui_data.
} ui_glyph_t;

typedef struct _ui_font_t
{
	const ui_glyph_t *glyphs;
	u16 count;
	u16 ascent, descent;
} ui_font_t;

typedef struct _ui_mask_t
{
	u16 w, h;
	u32 off;
} ui_mask_t;

typedef enum _ui_font_id_t
{
	UI_FONT_BODY,  // 19 px : sous-titres, aides, batterie.
	UI_FONT_TITLE, // 28 px semi-gras : états et boutons du menu (caractères limités).
	UI_FONT_MARK   // 62 px semi-gras : « OXYN » seulement.
} ui_font_id_t;

// Logo (180 px) et icônes du menu (36 px), pré-rendus par tools/gen_ui_assets.py.
#define UI_LOGO_SIZE    180
#define UI_ICON_POWER   0
#define UI_ICON_RESTART 1
#define UI_ICON_BACK    2
#define UI_ICON_CLOSE   3

// Couleurs XRGB8888.
#define UI_TEXT   0xFFE6EDF3
#define UI_DIM    0xFF7D8590
#define UI_ACCENT 0xFF5B8DEF
#define UI_OK     0xFF3FB950
#define UI_WARN   0xFFF0A03C
#define UI_ERROR  0xFFF07A75
#define UI_PANEL  0xFF1C232B
#define UI_PANEL2 0xFF242D3A

u32  ui_bg_color(u32 x, u32 y);
void ui_background();
void ui_restore(int x, int y, int w, int h);
void ui_rrect(int x, int y, int w, int h, int r, u32 color);
void ui_circle(int cx, int cy, int r, u32 color);
void ui_logo(int cx, int cy);
void ui_icon(int cx, int cy, u32 id, u32 color);
int  ui_text_width(ui_font_id_t font, const char *s, int spacing);
int  ui_text(int x, int baseline, ui_font_id_t font, const char *s, u32 color, int spacing);
void ui_text_center(int cx, int baseline, ui_font_id_t font, const char *s, u32 color, int spacing);
void ui_text_right(int right, int baseline, ui_font_id_t font, const char *s, u32 color, int spacing);

#endif
