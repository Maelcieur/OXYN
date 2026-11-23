/*
 * SwitchDeck - écran paysage 1280x720.
 * On dessine dans un tampon paysage, que le VIC tourne et recopie vers l'écran.
 */

#ifndef _SCREEN_H_
#define _SCREEN_H_

#include <utils/types.h>

#include "deck_proto.h"

#define SCREEN_W DECK_SCREEN_W
#define SCREEN_H DECK_SCREEN_H

#define COL_BG     0xFF101418
#define COL_PANEL  0xFF1C232B
#define COL_ACCENT 0xFF4FA3FF
#define COL_OK     0xFF3FB950
#define COL_TEXT   0xFFE6EDF3
#define COL_DIM    0xFF7D8590

// Luminosité BDK (0-255).
#define BRIGHTNESS_NORMAL 100
#define BRIGHTNESS_IDLE   25

void screen_init();
u32 *screen_fb();
void screen_present();
void screen_brightness(u32 value);
void screen_fill_rect(u32 x, u32 y, u32 w, u32 h, u32 color);
void screen_blit(u32 x, u32 y, u32 w, u32 h, const u32 *pixels);

int  screen_battery_percent();
bool screen_charging();
bool screen_has_external_power();

void screen_draw_waiting(bool usb_ready);
void screen_draw_connected(bool ram_fast);
// Menu : 0 Éteindre, 1 Redémarrer, 2 Retour à hekate, 3 Fermer.
#define SCREEN_MENU_COUNT 4
void screen_draw_menu(int selected, const char *note);
int  screen_menu_hit(u32 x, u32 y); // Bouton du menu sous ce point, -1 si aucun.

// Anime l'écran d'attente (points) ; à appeler souvent pendant l'attente.
void screen_tick();
// Un autre affichage (nuit, image du PC) a remplacé l'écran d'attente : plus d'animation.
void screen_forget();

#endif
