/*
 * jgtestlib_win.h: jgtestlib.h behind a real #include <windows.h>, so the header parser meets
 * the Windows SDK. Probes load the library through this header to record what R2025b does with
 * a header that includes windows.h: how long it takes and which functions it records.
 */

#ifndef JGTESTLIB_WIN_H
#define JGTESTLIB_WIN_H

#include <windows.h>
#include "jgtestlib.h"

JG_API DWORD jg_tick(void);
JG_API BOOL jg_is_even(DWORD x);

#endif /* JGTESTLIB_WIN_H */
