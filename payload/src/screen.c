/*
 * SwitchDeck - écran paysage 1280x720 (rotation matérielle VIC, comme Nyx).
 */

#include <string.h>

#include <bdk.h>
#include <power/bq24193.h>

#include "gfx/gfx.h"
#include "screen.h"
#include "ui.h"

// Tampon paysage où l'on dessine, recopié et tourné par le VIC vers le tampon affiché.
static u32 *const fb_land = (u32 *)NYX_FB2_ADDRESS;

static u32 brightness_cur;

void screen_init()
{
	// Fenêtre A en portrait (720x1280), comme hekate.
	display_init_window_a_pitch();
	display_backlight_pwm_init();

	memset(fb_land, 0, SCREEN_W * SCREEN_H * 4);

	// Même configuration que Nyx : on dessine en 1280x720, le VIC tourne de 270° vers l'écran.
	vic_surface_t vic_sfc;
	vic_sfc.src_buf  = NYX_FB2_ADDRESS;
	vic_sfc.dst_buf  = NYX_FB_ADDRESS;
	vic_sfc.width    = SCREEN_W;
	vic_sfc.height   = SCREEN_H;
	vic_sfc.pix_fmt  = VIC_PIX_FORMAT_X8R8G8B8;
	vic_sfc.rotation = VIC_ROTATION_270;

	vic_init();
	vic_set_surface(&vic_sfc);
	screen_present();

	display_init_window_a_pitch_vic();

	// La console texte de hekate dessine directement dans le tampon paysage.
	gfx_init_ctxt(fb_land, SCREEN_W, SCREEN_H, SCREEN_W);
	gfx_con_init();

	screen_brightness(BRIGHTNESS_NORMAL);
}

u32 *screen_fb()
{
	return fb_land;
}

void screen_present()
{
	vic_compose();
	vic_wait_idle();
}

void screen_brightness(u32 value)
{
	if (value == brightness_cur)
		return;

	display_backlight_brightness(value, 1000);
	brightness_cur = value;
}

void screen_fill_rect(u32 x, u32 y, u32 w, u32 h, u32 color)
{
	if (x >= SCREEN_W || y >= SCREEN_H)
		return;
	if (x + w > SCREEN_W)
		w = SCREEN_W - x;
	if (y + h > SCREEN_H)
		h = SCREEN_H - y;

	for (u32 j = 0; j < h; j++)
	{
		u32 *line = fb_land + (y + j) * SCREEN_W + x;
		for (u32 i = 0; i < w; i++)
			line[i] = color;
	}
}

// Le rectangle doit tenir dans l'écran (vérifié par l'appelant).
void screen_blit(u32 x, u32 y, u32 w, u32 h, const u32 *pixels)
{
	for (u32 j = 0; j < h; j++)
		memcpy(fb_land + (y + j) * SCREEN_W + x, pixels + j * w, w * 4);
}

int screen_battery_percent()
{
	int val = 0;
	if (max17050_get_property(MAX17050_RepSOC, &val))
		return -1;

	return val >> 8;
}

bool screen_charging()
{
	int status = 0;
	if (bq24193_get_property(BQ24193_ChargeStatus, &status))
		return false;

	// 1 : pré-charge, 2 : charge rapide.
	return status == 1 || status == 2;
}

bool screen_has_external_power()
{
	u8 status = i2c_recv_byte(I2C_1, BQ24193_I2C_ADDR, BQ24193_Status);
	return (status & BQ24193_STATUS_PG_MASK) != 0;
}

// ---------- Écrans du payload (maquettes validées le 23/11) ----------

static bool waiting_shown; // Écran d'attente affiché : les points s'animent (screen_tick).
static u32 dots_phase, next_dots;

// Batterie en haut à droite : « NN % » puis icône (vert, orange sous 20 %, bleu en charge).
static void _draw_battery()
{
	int pct = screen_battery_percent();
	if (pct < 0)
		return;
	pct = MIN(pct, 100);
	bool charging = screen_charging();

	const int x = 1194, y = 29, w = 46, h = 22;
	ui_rrect(x, y, w, h, 5, UI_DIM);
	ui_rrect(x + 2, y + 2, w - 4, h - 4, 3, ui_bg_color(x + w / 2, y + h / 2));
	ui_rrect(x + w, y + 7, 4, h - 14, 1, UI_DIM);
	int fill = (w - 8) * pct / 100;
	if (fill > 0)
		ui_rrect(x + 4, y + 4, fill, h - 8, 2, charging ? UI_ACCENT : pct > 20 ? UI_OK : UI_WARN);

	char text[16];
	s_printf(text, charging ? "Charge %d %%" : "%d %%", pct);
	ui_text_right(x - 12, y + 18, UI_FONT_BODY, text, UI_TEXT, 0);
}

// Fond, petit « OXYN » en haut à gauche et batterie.
static void _draw_frame()
{
	ui_background();
	ui_text(40, 47, UI_FONT_BODY, "OXYN", UI_DIM, 4);
	_draw_battery();
}

// Logo centré et nom OXYN espacé dessous.
static void _draw_brand(int cx, int cy)
{
	ui_logo(cx, cy);
	ui_text_center(cx, cy + 177, UI_FONT_MARK, "OXYN", UI_TEXT, 18);
}

// Pastille d'aide en bas de l'écran.
static void _draw_pill(int cx, const char *text)
{
	int w = ui_text_width(UI_FONT_BODY, text, 0) + 44;
	ui_rrect(cx - w / 2, 645, w, 40, 20, UI_PANEL);
	ui_text_center(cx, 671, UI_FONT_BODY, text, UI_DIM, 0);
}

#define DOTS_X  640
#define DOTS_Y  562
#define DOTS_MS 350

static void _draw_dots()
{
	static const u32 alpha[] = { 256, 150, 60 };
	ui_restore(DOTS_X - 40, DOTS_Y - 10, 80, 20);
	for (u32 i = 0; i < 3; i++)
	{
		int x = DOTS_X - 22 + (int)i * 22;
		u32 bg = ui_bg_color(x, DOTS_Y);
		u32 a = alpha[(i + 3 - dots_phase) % 3];
		u32 r = (((UI_ACCENT >> 16) & 0xFF) * a + ((bg >> 16) & 0xFF) * (256 - a)) >> 8;
		u32 g = (((UI_ACCENT >> 8) & 0xFF) * a + ((bg >> 8) & 0xFF) * (256 - a)) >> 8;
		u32 b = ((UI_ACCENT & 0xFF) * a + (bg & 0xFF) * (256 - a)) >> 8;
		ui_circle(x, DOTS_Y, 5, 0xFF000000 | (r << 16) | (g << 8) | b);
	}
}

void screen_draw_waiting(bool usb_ready)
{
	_draw_frame();
	_draw_brand(640, 245);
	ui_text_center(640, 488, UI_FONT_TITLE, usb_ready ? "En attente du PC" : "Erreur USB", usb_ready ? UI_TEXT : UI_ERROR, 0);
	ui_text_center(640, 526, UI_FONT_BODY,
		usb_ready ? "Branche le câble USB-C et lance OXYN sur le PC." : "Initialisation de l'USB impossible, nouvel essai…",
		UI_DIM, 0);
	dots_phase = 0;
	_draw_dots();
	_draw_pill(640, "VOL+ et VOL−  ·  menu");
	screen_present();

	waiting_shown = true;
	next_dots = get_tmr_ms() + DOTS_MS;
}

void screen_draw_connected(bool ram_fast)
{
	waiting_shown = false;
	_draw_frame();
	_draw_brand(640, 245);
	int w = ui_text_width(UI_FONT_TITLE, "Connecté au PC", 0);
	ui_circle(640 - w / 2 - 6, 478, 7, UI_OK);
	ui_text_center(652, 488, UI_FONT_TITLE, "Connecté au PC", UI_TEXT, 0);
	ui_text_center(640, 526, UI_FONT_BODY, "Chargement de l'affichage…", UI_DIM, 0);
	_draw_pill(640, ram_fast ? "Mémoire 1600 MHz" : "Mémoire 204 MHz (libsys_minerva.bso introuvable)");
	screen_present();
}

// Menu : boutons à droite du logo.
#define MENU_X   480
#define MENU_Y   126
#define MENU_W   740
#define MENU_H   104
#define MENU_GAP 16

static const struct { u32 icon; const char *title, *sub; } menu_items[SCREEN_MENU_COUNT] = {
	{ UI_ICON_POWER,   "Éteindre",        "Éteint la Switch" },
	{ UI_ICON_RESTART, "Redémarrer",      "Redémarre la Switch" },
	{ UI_ICON_BACK,    "Retour à hekate", "Relance hekate depuis la carte SD" },
	{ UI_ICON_CLOSE,   "Fermer le menu",  "Reprend l'attente du PC" },
};

void screen_draw_menu(int selected, const char *note)
{
	waiting_shown = false;
	_draw_frame();
	ui_logo(250, 290);
	ui_text_center(250, 448, UI_FONT_MARK, "OXYN", UI_TEXT, 12);
	ui_text_center(250, 490, UI_FONT_BODY, "Menu", UI_DIM, 0);

	for (int i = 0; i < SCREEN_MENU_COUNT; i++)
	{
		int y = MENU_Y + i * (MENU_H + MENU_GAP), cy = y + MENU_H / 2;
		bool sel = i == selected;
		if (sel)
		{
			ui_rrect(MENU_X, y, MENU_W, MENU_H, 22, UI_ACCENT);
			ui_rrect(MENU_X + 3, y + 3, MENU_W - 6, MENU_H - 6, 19, UI_PANEL2);
		}
		else
			ui_rrect(MENU_X, y, MENU_W, MENU_H, 22, UI_PANEL);

		ui_circle(MENU_X + 60, cy, 30, sel ? UI_ACCENT : 0xFF2C3541);
		ui_icon(MENU_X + 60, cy, menu_items[i].icon, sel ? 0xFFFFFFFF : UI_TEXT);
		ui_text(MENU_X + 112, cy - 6, UI_FONT_TITLE, menu_items[i].title, UI_TEXT, 0);
		ui_text(MENU_X + 112, cy + 28, UI_FONT_BODY, menu_items[i].sub, UI_DIM, 0);
		if (sel)
			ui_text_right(MENU_X + MENU_W - 30, cy + 7, UI_FONT_BODY, "POWER", UI_ACCENT, 1);
	}

	if (note)
		ui_text_center(850, 622, UI_FONT_BODY, note, UI_ERROR, 0);
	_draw_pill(850, "Touche un choix  ·  VOL+ / VOL− pour se déplacer  ·  POWER pour valider");
	screen_present();
}

int screen_menu_hit(u32 x, u32 y)
{
	if (x < MENU_X || x >= MENU_X + MENU_W || y < MENU_Y)
		return -1;
	u32 i = (y - MENU_Y) / (MENU_H + MENU_GAP);
	u32 in = (y - MENU_Y) % (MENU_H + MENU_GAP);
	return i < SCREEN_MENU_COUNT && in < MENU_H ? (int)i : -1;
}

void screen_tick()
{
	if (!waiting_shown || get_tmr_ms() < next_dots)
		return;
	next_dots = get_tmr_ms() + DOTS_MS;
	dots_phase = (dots_phase + 1) % 3;
	_draw_dots();
	screen_present();
}

void screen_forget()
{
	waiting_shown = false;
}
